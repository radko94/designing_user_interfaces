using SecureMesh.Core;
using SecureMesh.Core.Domain;
using SecureMesh.Core.Domain.Persistence;

namespace SecureMesh.Core.Tests;

/// <summary>
/// Tests for the qa-wave1c forward-secrecy reliability fixes: (a) send-ratchet
/// key-reuse under FlushQueuedAsync/SendText interleaving, (b) the explicit
/// bare-prekey ACK, and (c) transport failure marking a message Failed (never
/// silently Delivered).
/// </summary>
public class ForwardSecrecyWave1cTests
{
    // ---------------------------------------------------------------------
    // (a) Concurrency: a responder's flush of queued messages interleaved with a
    // concurrent SendText must not reuse a send-ratchet key or lose a message.
    // Uses an async-yielding transport (NOT the synchronous MockTransport) so the
    // flush genuinely suspends at the transport await.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task FlushQueued_InterleavedWithConcurrentSendText_NoKeyReuse_NoMessageLoss()
    {
        var (alice, bob) = TestData.OrderedIdentities(); // alice = initiator
        using var aDb = Database.InMemory();
        using var bDb = Database.InMemory();

        var aT = new BufferingTransport();          // Alice: records her prekey frame
        var bT = new GatedAsyncTransport();         // Bob: yields at every send

        var aContacts = new ContactsService(aDb);
        var bContacts = new ContactsService(bDb);
        var bobOnAlice = aContacts.AddContact("Bob", bob.SigningPublicKey, bob.DhPublicKey, bob.EphemeralDhPublicKey);
        var aliceOnBob = bContacts.AddContact("Alice", alice.SigningPublicKey, alice.DhPublicKey, alice.EphemeralDhPublicKey);

        var aChat = new ChatService(alice, aT, aDb);
        var bChat = new ChatService(bob, bT, bDb);
        var aDelivered = new List<IncomingMessage>();
        var bDelivered = new List<IncomingMessage>();
        aChat.MessageDelivered += aDelivered.Add;
        bChat.MessageDelivered += bDelivered.Add;

        var aConv = aChat.EnsureConversation(bobOnAlice);
        var bConv = bChat.EnsureConversation(aliceOnBob);

        // Bob (responder) queues two messages before the session exists.
        var q1 = await bChat.SendText(bConv.Id, "q1");
        var q2 = await bChat.SendText(bConv.Id, "q2");
        Assert.Equal(MessageStatus.Pending, q1.Status);
        Assert.Equal(MessageStatus.Pending, q2.Status);
        Assert.Empty(bT.Outbox); // nothing sealed/sent yet

        // Alice (initiator) sends a real prekey carrying her first message.
        await aChat.SendText(aConv.Id, "hello");
        Assert.Single(aT.Outbox);
        Assert.Equal(FrameType.Prekey, WireMessage.Deserialize(aT.Outbox[0]).FrameType);

        // Bob receives the prekey → establishes → starts flushing q1, q2. The flush
        // seals q1 and then suspends at the transport await.
        bChat.HandleIncoming(aT.Outbox[0]);
        Assert.Equal("hello", bDelivered.Single().Plaintext);

        await WaitUntilAsync(() => bT.Outbox.Count == 1); // q1's send entered (suspended)

        // Concurrent SendText while the flush is mid-await.
        var thirdTask = bChat.SendText(bConv.Id, "third");
        await WaitUntilAsync(() => bT.Outbox.Count == 2); // "third"'s send entered

        // Release the gate so all sends complete and the flush proceeds to q2.
        bT.Release();
        var third = await thirdTask;

        // The flush finishes q2 (and the concurrent message finishes) — all delivered.
        await WaitUntilAsync(() => bChat.ListPending(bConv.Id).Count == 0);
        Assert.Equal(3, bT.Outbox.Count);

        Assert.Equal(MessageStatus.Delivered, third.Status);

        // Feed Bob's three frames to Alice. If any two shared a ratchet key, one
        // would fail GCM authentication and be silently dropped; if the flush had
        // re-sealed a key, the receiver would lose a message. All three must decrypt.
        foreach (var wire in bT.Outbox)
            aChat.HandleIncoming(wire);

        Assert.Equal(3, aDelivered.Count);
        Assert.Equal(new[] { "q1", "q2", "third" }, aDelivered.Select(d => d.Plaintext).OrderBy(x => x).ToArray());
        Assert.Equal(3, aDelivered.Select(d => d.Seq).Distinct().Count()); // one distinct send-ratchet key per message
    }

    // ---------------------------------------------------------------------
    // (b) Bare-prekey ACK: the responder answers an auto-initiated bare prekey
    // with an explicit ACK, so the initiator's SessionEstablished fires and the
    // bare prekey leaves the pending outbox instead of staying Pending forever.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task BarePrekey_AutoInitiated_IsAcked_InitiatorSessionEstablished_AndPrekeyDelivered()
    {
        var (alice, bob) = TestData.OrderedIdentities(); // alice = initiator
        using var aDb = Database.InMemory();
        using var bDb = Database.InMemory();

        var aT = new MockTransport("alice");
        var bT = new MockTransport("bob");

        var aContacts = new ContactsService(aDb);
        var bContacts = new ContactsService(bDb);
        var bobOnAlice = aContacts.AddContact("Bob", bob.SigningPublicKey, bob.DhPublicKey, bob.EphemeralDhPublicKey);
        var aliceOnBob = bContacts.AddContact("Alice", alice.SigningPublicKey, alice.DhPublicKey, alice.EphemeralDhPublicKey);

        var aChat = new ChatService(alice, aT, aDb);
        var bChat = new ChatService(bob, bT, bDb);
        var aEstablished = new List<int>();
        var bEstablished = new List<int>();
        aChat.SessionEstablished += aEstablished.Add;
        bChat.SessionEstablished += bEstablished.Add;
        await aChat.StartAsync();
        await bChat.StartAsync();

        var aConv = aChat.EnsureConversation(bobOnAlice);
        var bConv = bChat.EnsureConversation(aliceOnBob);

        // Connect: Alice auto-initiates a BARE prekey (empty plaintext). Bob
        // establishes from it and answers with an explicit ACK; Alice's
        // SessionEstablished fires and her bare prekey leaves the outbox.
        aT.Connect(bT);

        // Alice's last frame is the bare prekey, marked explicitly.
        var aliceFrame = WireMessage.Deserialize(aT.LastSentPayload!);
        Assert.Equal(FrameType.Prekey, aliceFrame.FrameType);
        Assert.True(aliceFrame.IsBarePrekey);

        // Bob's last frame is the explicit ACK (never a prekey).
        var bobFrame = WireMessage.Deserialize(bT.LastSentPayload!);
        Assert.Equal(FrameType.Ack, bobFrame.FrameType);
        Assert.True(bobFrame.IsAck);

        // Both sides became established, and BOTH fired SessionEstablished.
        Assert.True(aChat.IsEstablished(aConv.Id));
        Assert.True(bChat.IsEstablished(bConv.Id));
        Assert.Contains(aConv.Id, aEstablished); // initiator fires on the explicit ACK
        Assert.Contains(bConv.Id, bEstablished); // responder fires on the prekey

        // The initiator's bare prekey is no longer pending (it was ACKed and removed
        // from the outbox); its System marker row remains to reserve sequence 1.
        var alicePendingPrekeys = new PendingMessageRepository(aDb.Connection)
            .ForConversation(aConv.Id).Where(p => p.IsPrekey).ToList();
        Assert.Empty(alicePendingPrekeys);
        var aliceMessages = new MessageRepository(aDb.Connection).ForConversation(aConv.Id);
        Assert.Single(aliceMessages);
        Assert.Equal(MessageStatus.System, aliceMessages[0].Status);
        Assert.Equal(1, aliceMessages[0].SenderSeq);
    }

    // ---------------------------------------------------------------------
    // (c) A transport throw marks the message Failed — never silently Delivered.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task SendText_TransportThrows_MarksFailed()
    {
        var (alice, bob) = TestData.OrderedIdentities(); // alice = initiator
        using var aDb = Database.InMemory();

        var aT = new ThrowingTransport();

        var aContacts = new ContactsService(aDb);
        var bobOnAlice = aContacts.AddContact("Bob", bob.SigningPublicKey, bob.DhPublicKey, bob.EphemeralDhPublicKey);

        var aChat = new ChatService(alice, aT, aDb);

        var aConv = aChat.EnsureConversation(bobOnAlice);

        // The initiator's first (prekey) send throws in the transport.
        var message = await aChat.SendText(aConv.Id, "hello, can you hear me?");

        Assert.Equal(MessageStatus.Failed, message.Status);
        Assert.NotEqual(MessageStatus.Delivered, message.Status);

        // The sealed prekey stays in the outbox so it can be re-sent identically.
        Assert.Single(aChat.ListPending(aConv.Id));
        Assert.Single(new PendingMessageRepository(aDb.Connection).ForConversation(aConv.Id));
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("Condition was not met within the timeout.");
            await Task.Delay(10);
        }
    }

    /// <summary>A synchronous in-memory transport that only records sent payloads.</summary>
    private sealed class BufferingTransport : ITransport
    {
        public event Action<byte[]>? MessageReceived;
        public event Action<string>? PeerConnected;
        public event Action<string>? PeerDisconnected;
        public bool IsSearching => false;
        public event Action<bool>? SearchingChanged;

        public List<byte[]> Outbox { get; } = new();

        public Task StartAsync() => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;

        public Task SendAsync(byte[] payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            Outbox.Add((byte[])payload.Clone());
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }

    /// <summary>
    /// An async-yielding transport: <see cref="SendAsync"/> records the payload and
    /// then suspends until <see cref="Release"/> is called, so the caller genuinely
    /// yields at every transport await (unlike the synchronous MockTransport).
    /// </summary>
    private sealed class GatedAsyncTransport : ITransport
    {
        private readonly object _sync = new();
        private readonly List<byte[]> _outbox = new();
        private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action<byte[]>? MessageReceived;
        public event Action<string>? PeerConnected;
        public event Action<string>? PeerDisconnected;
        public bool IsSearching => false;
        public event Action<bool>? SearchingChanged;

        public IReadOnlyList<byte[]> Outbox
        {
            get { lock (_sync) return _outbox.ToList(); }
        }

        public async Task SendAsync(byte[] payload)
        {
            ArgumentNullException.ThrowIfNull(payload);

            Task release;
            lock (_sync)
            {
                _outbox.Add((byte[])payload.Clone());
                release = _release.Task;
            }
            await release;
        }

        public void Release() => _release.TrySetResult();

        public Task StartAsync() => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public void Dispose() { }
    }

    /// <summary>A transport whose <see cref="SendAsync"/> always throws.</summary>
    private sealed class ThrowingTransport : ITransport
    {
        public event Action<byte[]>? MessageReceived;
        public event Action<string>? PeerConnected;
        public event Action<string>? PeerDisconnected;
        public bool IsSearching => false;
        public event Action<bool>? SearchingChanged;

        public Task StartAsync() => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;

        public Task SendAsync(byte[] payload) =>
            throw new InvalidOperationException("Transport is offline.");

        public void Dispose() { }
    }
}
