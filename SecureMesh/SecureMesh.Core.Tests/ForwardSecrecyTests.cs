using System.Text;
using SecureMesh.Core;
using SecureMesh.Core.Domain;
using SecureMesh.Core.Domain.Persistence;

namespace SecureMesh.Core.Tests;

/// <summary>
/// Tests that prove the spec's "Test requirements" (a)–(g) for the forward-secrecy
/// (prekey-message) redesign: prekey session establishment, one-time-EK deletion,
/// plaintext-history restart, out-of-order/replay with persisted skipped keys,
/// identical-envelope retry, per-sender group sequence numbers, and stable
/// channel-id context binding.
/// </summary>
public class ForwardSecrecyTests
{
    // ---------------------------------------------------------------------
    // (a) A prekey message establishes the session; both peers then chat.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task PrekeyMessage_EstablishesSession_AndEnablesBidirectionalChat()
    {
        using var aDb = Database.InMemory();
        using var bDb = Database.InMemory();
        var (alice, bob) = TestData.OrderedIdentities(); // alice = initiator

        var aT = new MockTransport("alice");
        var bT = new MockTransport("bob");
        aT.Connect(bT);

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
        await aChat.StartAsync();
        await bChat.StartAsync();

        var aConv = aChat.EnsureConversation(bobOnAlice);
        var bConv = bChat.EnsureConversation(aliceOnBob);

        // Alice's first message is a prekey frame carrying her one-time EK + IK.
        await aChat.SendText(aConv.Id, "hello");
        var first = WireMessage.Deserialize(aT.LastSentPayload!);
        Assert.Equal(FrameType.Prekey, first.FrameType);
        Assert.True(first.IsPrekey);
        Assert.NotNull(first.SenderEkPublic);
        Assert.NotNull(first.SenderIkPublic);
        Assert.Equal(32, first.SenderEkPublic!.Length);
        Assert.Equal(alice.DhPublicKey, first.SenderIkPublic);
        Assert.Equal("hello", bDelivered.Single().Plaintext);

        // Bob replies on the now-established session (a non-prekey frame).
        await bChat.SendText(bConv.Id, "hi back");
        var reply = WireMessage.Deserialize(bT.LastSentPayload!);
        Assert.Equal(FrameType.Message, reply.FrameType);
        Assert.False(reply.IsPrekey);
        Assert.Null(reply.SenderEkPublic);
        Assert.Equal("hi back", aDelivered.Single().Plaintext);
    }

    // ---------------------------------------------------------------------
    // (b) After establishment the one-time EK private key is gone.
    // ---------------------------------------------------------------------
    [Fact]
    public void EkPrivateKey_IsGone_AfterEstablishment()
    {
        var alice = KeyGeneration.CreateIdentity();
        var bob = KeyGeneration.CreateIdentity();

        // EstablishInitiatorSession returns only the two ratchet states and the EK
        // PUBLIC key; the root session key and EK private key are zeroed inside and
        // never leave the method, so they cannot be persisted.
        var (send, recv, ekPublic) = CryptoSession.EstablishInitiatorSession(
            alice, bob.DhPublicKey, bob.EphemeralDhPublicKey, CryptoSession.DefaultInfo);

        Assert.Equal(32, send.Length);
        Assert.Equal(32, recv.Length);
        Assert.Equal(32, ekPublic.Length);

        // The responder reaches the same session from only the public EK + its own
        // SPK private key — no EK private key is required or derivable from persisted data.
        var (bobSend, bobRecv) = CryptoSession.EstablishResponderSession(
            bob, alice.DhPublicKey, ekPublic, CryptoSession.DefaultInfo);
        Assert.Equal(send, bobRecv);
        Assert.Equal(recv, bobSend);
    }

    [Fact]
    public async Task PersistedConversation_HasNoSessionKeyOrEkPrivate()
    {
        using var aDb = Database.InMemory();
        using var bDb = Database.InMemory();
        var (alice, bob) = TestData.OrderedIdentities(); // alice = initiator

        var aT = new MockTransport("alice");
        var bT = new MockTransport("bob");
        aT.Connect(bT);

        var aContacts = new ContactsService(aDb);
        var bContacts = new ContactsService(bDb);
        var bobOnAlice = aContacts.AddContact("Bob", bob.SigningPublicKey, bob.DhPublicKey, bob.EphemeralDhPublicKey);
        var aliceOnBob = bContacts.AddContact("Alice", alice.SigningPublicKey, alice.DhPublicKey, alice.EphemeralDhPublicKey);

        var aChat = new ChatService(alice, aT, aDb);
        var bChat = new ChatService(bob, bT, bDb);
        await aChat.StartAsync();
        await bChat.StartAsync();

        var aConv = aChat.EnsureConversation(bobOnAlice);
        bChat.EnsureConversation(aliceOnBob);

        await aChat.SendText(aConv.Id, "secret");

        // The persisted conversation stores only the stable channel id + ratchet
        // states — there is no session key and no EK private key.
        var persisted = new ConversationRepository(aDb.Connection).Get(aConv.Id)!;
        Assert.Equal(4, persisted.ChannelId.Length);
        Assert.Equal(32, persisted.SendRatchetState.Length);
        Assert.Equal(32, persisted.RecvRatchetState.Length);

        // The prekey stays in the outbox until the responder's implicit ACK.
        var outbox = new PendingMessageRepository(aDb.Connection).ForConversation(aConv.Id);
        Assert.Single(outbox);
        Assert.True(outbox[0].IsPrekey);
    }

    // ---------------------------------------------------------------------
    // (c) History is readable from persisted plaintext after "restart".
    // ---------------------------------------------------------------------
    [Fact]
    public async Task History_IsReadableFromPlaintext_AfterRestart()
    {
        using var aDb = Database.InMemory();
        using var bDb = Database.InMemory();
        var (alice, bob) = TestData.OrderedIdentities(); // alice = initiator

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
        await aChat.StartAsync();
        await bChat.StartAsync();

        var aConv = aChat.EnsureConversation(bobOnAlice);
        var bConv = bChat.EnsureConversation(aliceOnBob);

        await aChat.SendText(aConv.Id, "m1");
        await aChat.SendText(aConv.Id, "m2");
        Assert.Equal(2, bDelivered.Count);

        // History is stored as plaintext — readable directly from the DB.
        var bobMessages = new MessageRepository(bDb.Connection).ForConversation(bConv.Id);
        Assert.Equal(new[] { "m1", "m2" }, bobMessages.Select(m => m.Plaintext).ToArray());

        // "Restart" Bob: a fresh ChatService over the same DB (no session key to load).
        bChat.Dispose();
        var bChat2 = new ChatService(bob, new MockTransport("bob2"), bDb);
        var bDelivered2 = new List<IncomingMessage>();
        bChat2.MessageDelivered += bDelivered2.Add;
        await bChat2.StartAsync();

        // Alice continues the session; Bob's restarted service decrypts from the
        // persisted receive ratchet state (the root session key was never stored).
        await aChat.SendText(aConv.Id, "m3");
        var wire = aT.LastSentPayload!;
        bChat2.HandleIncoming(wire);
        Assert.Equal("m3", bDelivered2.Single().Plaintext);
    }

    // ---------------------------------------------------------------------
    // (d) Out-of-order/replay with skipped keys survives restart.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task OutOfOrder_WithSkippedKeys_SurvivesRestart()
    {
        using var aDb = Database.InMemory();
        using var bDb = Database.InMemory();
        var (alice, bob) = TestData.OrderedIdentities(); // alice = initiator

        var aT = new BufferingTransport();
        var bT = new BufferingTransport();

        var aContacts = new ContactsService(aDb);
        var bContacts = new ContactsService(bDb);
        var bobOnAlice = aContacts.AddContact("Bob", bob.SigningPublicKey, bob.DhPublicKey, bob.EphemeralDhPublicKey);
        var aliceOnBob = bContacts.AddContact("Alice", alice.SigningPublicKey, alice.DhPublicKey, alice.EphemeralDhPublicKey);

        var aChat = new ChatService(alice, aT, aDb);
        var bChat = new ChatService(bob, bT, bDb);
        var bDelivered = new List<IncomingMessage>();
        bChat.MessageDelivered += bDelivered.Add;

        var aConv = aChat.EnsureConversation(bobOnAlice);
        var bConv = bChat.EnsureConversation(aliceOnBob);

        await aChat.SendText(aConv.Id, "m1");
        await aChat.SendText(aConv.Id, "m2");
        await aChat.SendText(aConv.Id, "m3");

        // Deliver 1, 3 (skips 2): the skipped key for seq 2 is persisted.
        bChat.HandleIncoming(aT.Outbox[0]);
        bChat.HandleIncoming(aT.Outbox[2]);
        Assert.Equal(new[] { "m1", "m3" }, bDelivered.Select(d => d.Plaintext).ToArray());

        // The skipped key for seq 2 is durable.
        var channelId = new ConversationRepository(bDb.Connection).Get(bConv.Id)!.ChannelId;
        Assert.Single(new SkippedKeyRepository(bDb.Connection).ForChannelAndSender(channelId, aliceOnBob.Id));

        // "Restart" Bob, then deliver the late m2 — decrypts via the persisted skipped key.
        bChat.Dispose();
        var bChat2 = new ChatService(bob, new BufferingTransport(), bDb);
        var bDelivered2 = new List<IncomingMessage>();
        bChat2.MessageDelivered += bDelivered2.Add;

        bChat2.HandleIncoming(aT.Outbox[1]);
        Assert.Equal("m2", bDelivered2.Single().Plaintext);
    }

    // ---------------------------------------------------------------------
    // (e) Retry re-sends the identical envelope (no key reuse).
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Retry_ResendsIdenticalEnvelope_NoKeyReuse()
    {
        using var aDb = Database.InMemory();
        using var bDb = Database.InMemory();
        var (alice, bob) = TestData.OrderedIdentities(); // alice = initiator

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
        await aChat.StartAsync();
        await bChat.StartAsync();

        var aConv = aChat.EnsureConversation(bobOnAlice);
        var bConv = bChat.EnsureConversation(aliceOnBob);

        // Establish the session (Alice's prekey) and have Bob implicitly ACK it.
        await aChat.SendText(aConv.Id, "m1");
        Assert.Equal("m1", bDelivered.Single().Plaintext);
        await bChat.SendText(bConv.Id, "ack");
        Assert.Equal("ack", aDelivered.Single().Plaintext); // ACKs Alice's prekey

        // Disconnect; Alice's m2 fails and stays sealed in the outbox.
        aT.Disconnect();
        var m2 = await aChat.SendText(aConv.Id, "m2");
        Assert.Equal(MessageStatus.Failed, m2.Status);
        var m2Wire = (byte[])aT.LastSentPayload!.Clone();

        // Reconnect: the outbox is re-sent with the IDENTICAL envelope (no re-seal).
        aT.Connect(bT);
        Assert.Equal(m2Wire, aT.LastSentPayload!);
        Assert.Empty(aChat.ListPending(aConv.Id));

        // Bob decrypts the retried m2.
        Assert.Equal(2, bDelivered.Count);
        Assert.Equal("m2", bDelivered[1].Plaintext);
    }

    // ---------------------------------------------------------------------
    // (f) Group messages use per-sender sequence numbers.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task GroupMessages_UsePerSenderSequenceNumbers()
    {
        using var aDb = Database.InMemory();
        using var bDb = Database.InMemory();
        var alice = KeyGeneration.CreateIdentity();
        var bob = KeyGeneration.CreateIdentity();

        var aT = new MockTransport("alice");
        var bT = new MockTransport("bob");
        aT.Connect(bT);

        var aContacts = new ContactsService(aDb);
        var bContacts = new ContactsService(bDb);
        var bobOnAlice = aContacts.AddContact("Bob", bob.SigningPublicKey, bob.DhPublicKey, bob.EphemeralDhPublicKey);
        var aliceOnBob = bContacts.AddContact("Alice", alice.SigningPublicKey, alice.DhPublicKey, alice.EphemeralDhPublicKey);

        var aChat = new GroupChatService(alice, aT, aDb);
        var bChat = new GroupChatService(bob, bT, bDb);
        var bDelivered = new List<IncomingMessage>();
        var aDelivered = new List<IncomingMessage>();
        bChat.MessageDelivered += bDelivered.Add;
        aChat.MessageDelivered += aDelivered.Add;

        var aliceGroupId = aChat.CreateGroup("Trip", new[] { bobOnAlice }, "per-sender").Id;
        var bobGroupId = bChat.CreateGroup("Trip", new[] { aliceOnBob }, "per-sender").Id;

        await aChat.SendGroupMessage(aliceGroupId, "a1");
        bChat.HandleIncomingGroupMessage(aT.LastSentPayload!);

        await aChat.SendGroupMessage(aliceGroupId, "a2");
        bChat.HandleIncomingGroupMessage(aT.LastSentPayload!);

        await bChat.SendGroupMessage(bobGroupId, "b1");
        aChat.HandleIncomingGroupMessage(bT.LastSentPayload!);

        Assert.Equal(2, bDelivered.Count);
        Assert.Single(aDelivered);

        // Per-sender sequence numbers: Alice's own messages are 1 and 2, but Bob's
        // message starts its OWN sequence at 1 (not a shared counter of 3).
        var aConvId = aChat.GetGroup(aliceGroupId)!.ConversationId!.Value;
        var bConvId = bChat.GetGroup(bobGroupId)!.ConversationId!.Value;

        var aliceOut = new MessageRepository(aDb.Connection).ForConversation(aConvId)
            .Where(m => m.SenderId == 0).OrderBy(m => m.SenderSeq).Select(m => m.SenderSeq).ToArray();
        Assert.Equal(new long[] { 1, 2 }, aliceOut);

        var bobOut = new MessageRepository(bDb.Connection).ForConversation(bConvId)
            .Where(m => m.SenderId == 0).Select(m => m.SenderSeq).Single();
        Assert.Equal(1, bobOut);
    }

    // ---------------------------------------------------------------------
    // (g) Context tamper (wrong channel id) is rejected.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task WrongChannelId_IsRejected()
    {
        using var aDb = Database.InMemory();
        using var bDb = Database.InMemory();
        var (alice, bob) = TestData.OrderedIdentities(); // alice = initiator

        var aT = new BufferingTransport();
        var bT = new BufferingTransport();

        var aContacts = new ContactsService(aDb);
        var bContacts = new ContactsService(bDb);
        var bobOnAlice = aContacts.AddContact("Bob", bob.SigningPublicKey, bob.DhPublicKey, bob.EphemeralDhPublicKey);
        var aliceOnBob = bContacts.AddContact("Alice", alice.SigningPublicKey, alice.DhPublicKey, alice.EphemeralDhPublicKey);

        var aChat = new ChatService(alice, aT, aDb);
        var bChat = new ChatService(bob, bT, bDb);
        var bDelivered = new List<IncomingMessage>();
        bChat.MessageDelivered += bDelivered.Add;

        var aConv = aChat.EnsureConversation(bobOnAlice);
        var bConv = bChat.EnsureConversation(aliceOnBob);

        await aChat.SendText(aConv.Id, "secret");

        // Corrupt Bob's channel id so the context binding mismatches.
        var repo = new ConversationRepository(bDb.Connection);
        var bConvFresh = repo.Get(bConv.Id)!;
        var correctChannelId = bConvFresh.ChannelId;
        bConvFresh.ChannelId = new byte[] { 0xde, 0xad, 0xbe, 0xef };
        repo.Update(bConvFresh);

        bChat.HandleIncoming(aT.Outbox[0]);
        Assert.Empty(bDelivered); // wrong channel id → dropped

        // Restore the correct channel id → the same frame now decrypts.
        bConvFresh.ChannelId = correctChannelId;
        repo.Update(bConvFresh);
        bChat.HandleIncoming(aT.Outbox[0]);
        Assert.Equal("secret", bDelivered.Single().Plaintext);
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
