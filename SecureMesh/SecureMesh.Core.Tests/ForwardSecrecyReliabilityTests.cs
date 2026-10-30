using SecureMesh.Core;
using SecureMesh.Core.Domain;
using SecureMesh.Core.Domain.Persistence;

namespace SecureMesh.Core.Tests;

/// <summary>
/// Spec tests for the forward-secrecy reliability fix (fix-wave1b): deterministic
/// initiator, identity Hello + auto-initiation, prekey ACK/retry, responder
/// queueing + re-seal, and the FrameType wire discriminator.
/// </summary>
public class ForwardSecrecyReliabilityTests
{
    // ---------------------------------------------------------------------
    // (a) Simultaneous first-sends converge to ONE session.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task SimultaneousFirstSends_ConvergeToOneSession()
    {
        var (alice, bob) = TestData.OrderedIdentities(); // alice = initiator
        using var aDb = Database.InMemory();
        using var bDb = Database.InMemory();
        var aT = new BufferingTransport();
        var bT = new BufferingTransport();

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

        // Both send their first message before receiving the other's.
        await aChat.SendText(aConv.Id, "from alice");
        var bobMsg = await bChat.SendText(bConv.Id, "from bob");

        // Only the initiator (Alice) sent a prekey; Bob queued without sending.
        Assert.Equal(FrameType.Prekey, WireMessage.Deserialize(aT.Outbox.Single()).FrameType);
        Assert.Empty(bT.Outbox);
        Assert.Equal(MessageStatus.Pending, bobMsg.Status);

        // Bob receives Alice's prekey → establishes the ONE session and flushes its queue.
        bChat.HandleIncoming(aT.Outbox[0]);
        Assert.Equal("from alice", bDelivered.Single().Plaintext);
        Assert.Single(bT.Outbox); // Bob's queued message is now sealed + sent

        // Alice receives Bob's flushed reply → delivers + ACKs her prekey.
        aChat.HandleIncoming(bT.Outbox[0]);
        Assert.Equal("from bob", aDelivered.Single().Plaintext);
        Assert.Equal(
            MessageStatus.Delivered,
            new MessageRepository(aDb.Connection).ForConversation(aConv.Id).Single(m => m.SenderId == 0).Status);

        // Both can keep chatting over the single shared session.
        await aChat.SendText(aConv.Id, "second from alice");
        bChat.HandleIncoming(aT.Outbox[1]);
        Assert.Equal("second from alice", bDelivered[1].Plaintext);
    }

    // ---------------------------------------------------------------------
    // (b) Initiator retries the prekey until the responder replies.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Initiator_RetriesPrekey_UntilResponderReplies()
    {
        var (alice, bob) = TestData.OrderedIdentities(); // alice = initiator
        using var aDb = Database.InMemory();
        using var bDb = Database.InMemory();

        var aT = new MockTransport("alice");
        var bT = new MockTransport("bob");
        aT.Connect(bT);

        var aContacts = new ContactsService(aDb);
        var bContacts = new ContactsService(bDb);
        var bobOnAlice = aContacts.AddContact("Bob", bob.SigningPublicKey, bob.DhPublicKey, bob.EphemeralDhPublicKey);
        var aliceOnBob = bContacts.AddContact("Alice", alice.SigningPublicKey, alice.DhPublicKey, alice.EphemeralDhPublicKey);

        var aChat = new ChatService(alice, aT, aDb);
        var bChat = new ChatService(bob, bT, bDb);
        var bDelivered = new List<IncomingMessage>();
        bChat.MessageDelivered += bDelivered.Add;
        await bChat.StartAsync(); // Bob only; Alice is driven manually (no auto-retry)

        var aConv = aChat.EnsureConversation(bobOnAlice);
        var bConv = bChat.EnsureConversation(aliceOnBob);

        // Alice sends her first (prekey) message while the peer is disconnected.
        aT.Disconnect();
        var m1 = await aChat.SendText(aConv.Id, "hello?");
        Assert.Equal(MessageStatus.Failed, m1.Status);
        var prekeyWire = (byte[])aT.LastSentPayload!.Clone();

        // The prekey stays in the outbox, ready to be re-sent.
        Assert.Single(aChat.ListPending(aConv.Id));

        // Reconnect: re-send the IDENTICAL prekey envelope (lost-prekey recovery).
        aT.Connect(bT);
        var retried = await aChat.RetryPending(aConv.Id);
        Assert.Equal(1, retried);
        Assert.Equal(prekeyWire, aT.LastSentPayload!);

        // Bob now receives the prekey and establishes; the message is delivered.
        Assert.Equal("hello?", bDelivered.Single().Plaintext);

        // Still Pending on Alice's side: Bob has not replied (no implicit ACK yet).
        Assert.Single(aChat.ListPending(aConv.Id));

        // Bob replies → Alice's prekey is ACKed → Delivered and removed from the outbox.
        await bChat.SendText(bConv.Id, "got it");
        aChat.HandleIncoming(bT.LastSentPayload!);
        Assert.Empty(aChat.ListPending(aConv.Id));
        Assert.Equal(
            MessageStatus.Delivered,
            new MessageRepository(aDb.Connection).ForConversation(aConv.Id).Single(m => m.SenderId == 0).Status);
    }

    // ---------------------------------------------------------------------
    // (c) Responder's first message queues and is delivered after the prekey.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ResponderFirstMessage_Queues_AndIsDeliveredAfterPrekey()
    {
        var (alice, bob) = TestData.OrderedIdentities(); // alice = initiator
        using var aDb = Database.InMemory();
        using var bDb = Database.InMemory();
        var aT = new BufferingTransport();
        var bT = new BufferingTransport();

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

        // Bob (responder) sends FIRST: it queues unsealed (no frame on the wire yet).
        var bobMsg = await bChat.SendText(bConv.Id, "bob first");
        Assert.Equal(MessageStatus.Pending, bobMsg.Status);
        Assert.Empty(bT.Outbox); // the responder never sends a prekey

        // Alice (initiator) now sends her prekey.
        await aChat.SendText(aConv.Id, "alice says hi");
        Assert.Single(aT.Outbox);
        Assert.Equal(FrameType.Prekey, WireMessage.Deserialize(aT.Outbox[0]).FrameType);

        // Bob receives the prekey → establishes → flushes his queued message.
        bChat.HandleIncoming(aT.Outbox[0]);
        Assert.Equal("alice says hi", bDelivered.Single().Plaintext);
        Assert.Single(bT.Outbox); // Bob's queued message is now sealed + sent

        // Alice receives Bob's flushed message.
        aChat.HandleIncoming(bT.Outbox[0]);
        Assert.Equal("bob first", aDelivered.Single().Plaintext);

        // Bob's message is now Delivered (sealed once, sent once).
        var bobSent = new MessageRepository(bDb.Connection).ForConversation(bConv.Id).Single(m => m.SenderId == 0);
        Assert.Equal(MessageStatus.Delivered, bobSent.Status);
    }

    // ---------------------------------------------------------------------
    // (d) Hello frames map to the right contact and initiate only for the initiator.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Hello_TriggersInitiation_OnlyForInitiator()
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
        var bDelivered = new List<IncomingMessage>();
        bChat.MessageDelivered += bDelivered.Add;
        await aChat.StartAsync();
        await bChat.StartAsync();

        var aConv = aChat.EnsureConversation(bobOnAlice);
        var bConv = bChat.EnsureConversation(aliceOnBob);

        // Connect: both send a Hello; only the initiator (Alice) auto-initiates.
        aT.Connect(bT);

        // Alice's last frame is the auto-initiated prekey (sent after her Hello).
        var aliceFrame = WireMessage.Deserialize(aT.LastSentPayload!);
        Assert.Equal(FrameType.Prekey, aliceFrame.FrameType);
        Assert.Equal(alice.DhPublicKey, aliceFrame.SenderIkPublic);

        // Bob sent a Hello, then answered Alice's bare prekey with an explicit ACK
        // (a responder never sends a prekey).
        var bobFrame = WireMessage.Deserialize(bT.LastSentPayload!);
        Assert.Equal(FrameType.Ack, bobFrame.FrameType);
        Assert.True(bobFrame.IsAck);

        // Both sides now have an established session; the bare prekey delivered no message.
        Assert.True(aChat.IsEstablished(aConv.Id));
        Assert.True(bChat.IsEstablished(bConv.Id));
        Assert.Empty(bDelivered);

        // The Hello mapped each Ed25519 key to the correct contact.
        Assert.True(aChat.IsContactConnected(bobOnAlice.Id));
        Assert.True(bChat.IsContactConnected(aliceOnBob.Id));
    }

    // ---------------------------------------------------------------------
    // (regression) An auto-initiated bare prekey consumes seq 1 exactly once:
    // after it is ACKed, the initiator's first real message uses seq 2.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task AutoInitiatedPrekey_DoesNotCollideWithFirstRealMessage()
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
        var bDelivered = new List<IncomingMessage>();
        var aDelivered = new List<IncomingMessage>();
        bChat.MessageDelivered += bDelivered.Add;
        aChat.MessageDelivered += aDelivered.Add;
        await aChat.StartAsync();
        await bChat.StartAsync();

        var aConv = aChat.EnsureConversation(bobOnAlice);
        var bConv = bChat.EnsureConversation(aliceOnBob);

        // Connect: Alice auto-initiates with a bare prekey (seq 1).
        aT.Connect(bT);

        // Bob replies, which implicitly ACKs Alice's bare prekey.
        await bChat.SendText(bConv.Id, "hey");
        Assert.Equal("hey", aDelivered.Single().Plaintext);

        // Alice's first real message must use seq 2 (not re-use the bare prekey's seq 1).
        await aChat.SendText(aConv.Id, "first real");
        Assert.Equal("first real", bDelivered.Single().Plaintext);
    }

    // ---------------------------------------------------------------------
    // (e) Retry re-sends the identical envelope (no key reuse).
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Retry_ResendsIdenticalEnvelope_NoKeyReuse()
    {
        var (alice, bob) = TestData.OrderedIdentities(); // alice = initiator
        using var aDb = Database.InMemory();
        using var bDb = Database.InMemory();

        var aT = new MockTransport("alice");
        var bT = new MockTransport("bob");
        aT.Connect(bT);

        var aContacts = new ContactsService(aDb);
        var bContacts = new ContactsService(bDb);
        var bobOnAlice = aContacts.AddContact("Bob", bob.SigningPublicKey, bob.DhPublicKey, bob.EphemeralDhPublicKey);
        var aliceOnBob = bContacts.AddContact("Alice", alice.SigningPublicKey, alice.DhPublicKey, alice.EphemeralDhPublicKey);

        var aChat = new ChatService(alice, aT, aDb);
        var bChat = new ChatService(bob, bT, bDb);
        var bDelivered = new List<IncomingMessage>();
        var aDelivered = new List<IncomingMessage>();
        bChat.MessageDelivered += bDelivered.Add;
        aChat.MessageDelivered += aDelivered.Add;
        await bChat.StartAsync(); // Bob only; Alice is driven manually

        var aConv = aChat.EnsureConversation(bobOnAlice);
        var bConv = bChat.EnsureConversation(aliceOnBob);

        // Establish (Alice's prekey) and have Bob implicitly ACK it.
        await aChat.SendText(aConv.Id, "m1");
        Assert.Equal("m1", bDelivered.Single().Plaintext);
        await bChat.SendText(bConv.Id, "ack");
        aChat.HandleIncoming(bT.LastSentPayload!);
        Assert.Equal("ack", aDelivered.Single().Plaintext); // m1 is now ACKed

        // Disconnect; Alice's m2 fails and stays sealed in the outbox.
        aT.Disconnect();
        var m2 = await aChat.SendText(aConv.Id, "m2");
        Assert.Equal(MessageStatus.Failed, m2.Status);
        var m2Wire = (byte[])aT.LastSentPayload!.Clone();
        var sendStateAfterSeal = (byte[])new ConversationRepository(aDb.Connection).Get(aConv.Id)!.SendRatchetState.Clone();

        // Reconnect and retry: the IDENTICAL envelope is re-sent and the ratchet is NOT advanced again.
        aT.Connect(bT);
        var retried = await aChat.RetryPending(aConv.Id);
        Assert.Equal(1, retried);
        Assert.Equal(m2Wire, aT.LastSentPayload!);
        Assert.Equal(
            sendStateAfterSeal,
            new ConversationRepository(aDb.Connection).Get(aConv.Id)!.SendRatchetState);

        // Bob decrypts the retried m2 (proving the key was not consumed twice).
        Assert.Equal(2, bDelivered.Count);
        Assert.Equal("m2", bDelivered[1].Plaintext);
    }

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
}
