using System.Security.Cryptography;
using System.Text;
using SecureMesh.Core;
using SecureMesh.Core.Domain;
using SecureMesh.Core.Domain.Persistence;

namespace SecureMesh.Core.Tests;

/// <summary>
/// Tests for the foundational cryptography fixes: X3DH-lite session keys, the
/// direction-split ratchet with a persisted skipped-key cache, context binding,
/// replay/ordering rejection, and the outbox/retry lifecycle.
/// </summary>
public class CryptoFixTests
{
    // ---------------------------------------------------------------------
    // (a) Bidirectional chat A→B and B→A both deliver
    // ---------------------------------------------------------------------
    [Fact]
    public async Task BidirectionalChat_BothDirections_Deliver()
    {
        using var aDb = Database.InMemory();
        using var bDb = Database.InMemory();
        var (a, b) = TestData.OrderedIdentities(); // a = initiator

        var aT = new MockTransport("a");
        var bT = new MockTransport("b");
        aT.Connect(bT);

        var aContacts = new ContactsService(aDb);
        var bContacts = new ContactsService(bDb);
        var bOnA = aContacts.AddContact("Bob", b.SigningPublicKey, b.DhPublicKey, b.EphemeralDhPublicKey);
        var aOnB = bContacts.AddContact("Alice", a.SigningPublicKey, a.DhPublicKey, a.EphemeralDhPublicKey);

        var aChat = new ChatService(a, aT, aDb);
        var bChat = new ChatService(b, bT, bDb);
        var aDelivered = new List<IncomingMessage>();
        var bDelivered = new List<IncomingMessage>();
        aChat.MessageDelivered += aDelivered.Add;
        bChat.MessageDelivered += bDelivered.Add;

        await aChat.StartAsync();
        await bChat.StartAsync();

        var aConv = aChat.EnsureConversation(bOnA);
        var bConv = bChat.EnsureConversation(aOnB);

        await aChat.SendText(aConv.Id, "hi bob");
        Assert.Equal("hi bob", bDelivered.Single().Plaintext);

        await bChat.SendText(bConv.Id, "hi alice");
        Assert.Equal("hi alice", aDelivered.Single().Plaintext);

        // Both messages persisted on both sides (no seq collision).
        Assert.Equal(2, new MessageRepository(aDb.Connection).ForConversation(aConv.Id).Count);
        Assert.Equal(2, new MessageRepository(bDb.Connection).ForConversation(bConv.Id).Count);
    }

    // ---------------------------------------------------------------------
    // (b) Two crossing/concurrent sends both deliver
    // ---------------------------------------------------------------------
    [Fact]
    public async Task CrossingSends_BothDeliver()
    {
        using var pair = SetupPeers();
        var aDelivered = new List<IncomingMessage>();
        var bDelivered = new List<IncomingMessage>();
        pair.AliceChat.MessageDelivered += aDelivered.Add;
        pair.BobChat.MessageDelivered += bDelivered.Add;

        var aConv = pair.AliceChat.EnsureConversation(pair.BobOnAlice);
        var bConv = pair.BobChat.EnsureConversation(pair.AliceOnBob);

        // Establish the session first (Alice initiates, Bob responds) so the
        // crossing messages below are non-prekey frames.
        await pair.AliceChat.SendText(aConv.Id, "establish");
        pair.BobChat.HandleIncoming(pair.AliceTransport.Outbox[0]);
        Assert.Equal("establish", bDelivered.Single().Plaintext);

        // Two crossing (simultaneous) sends on the established session.
        await pair.AliceChat.SendText(aConv.Id, "hi");
        await pair.BobChat.SendText(bConv.Id, "hey");

        Assert.Equal(2, pair.AliceTransport.Outbox.Count); // "establish" + "hi"
        Assert.Single(pair.BobTransport.Outbox);           // "hey"

        // Cross-deliver the two new messages.
        pair.BobChat.HandleIncoming(pair.AliceTransport.Outbox[1]);
        pair.AliceChat.HandleIncoming(pair.BobTransport.Outbox[0]);

        Assert.Equal("hi", bDelivered.Last().Plaintext);
        Assert.Equal("hey", aDelivered.Single().Plaintext);
    }

    // ---------------------------------------------------------------------
    // (c) Out-of-order delivery within the window still decrypts
    // ---------------------------------------------------------------------
    [Fact]
    public async Task OutOfOrderDelivery_WithinWindow_StillDecrypts()
    {
        using var pair = SetupPeers();
        var bDelivered = new List<IncomingMessage>();
        pair.BobChat.MessageDelivered += bDelivered.Add;

        var aConv = pair.AliceChat.EnsureConversation(pair.BobOnAlice);
        pair.BobChat.EnsureConversation(pair.AliceOnBob);

        await pair.AliceChat.SendText(aConv.Id, "m1");
        await pair.AliceChat.SendText(aConv.Id, "m2");
        await pair.AliceChat.SendText(aConv.Id, "m3");

        // Deliver 1, 3, 2: the skipped key for seq 2 is cached and recovered.
        pair.BobChat.HandleIncoming(pair.AliceTransport.Outbox[0]);
        pair.BobChat.HandleIncoming(pair.AliceTransport.Outbox[2]);
        pair.BobChat.HandleIncoming(pair.AliceTransport.Outbox[1]);

        Assert.Equal(3, bDelivered.Count);
        Assert.Equal(new[] { "m1", "m3", "m2" }, bDelivered.Select(d => d.Plaintext).ToArray());
    }

    // ---------------------------------------------------------------------
    // (d) A too-old/beyond-window message is rejected without corrupting state
    // ---------------------------------------------------------------------
    [Fact]
    public async Task BeyondWindowMessage_IsRejected_WithoutCorruptingState()
    {
        using var pair = SetupPeers();
        var bDelivered = new List<IncomingMessage>();
        pair.BobChat.MessageDelivered += bDelivered.Add;

        var aConv = pair.AliceChat.EnsureConversation(pair.BobOnAlice);
        pair.BobChat.EnsureConversation(pair.AliceOnBob);

        for (var i = 0; i < 40; i++)
            await pair.AliceChat.SendText(aConv.Id, $"m{i + 1}");

        pair.BobChat.HandleIncoming(pair.AliceTransport.Outbox[0]); // seq 1
        Assert.Single(bDelivered);

        // seq 40 is 38 ratchet steps ahead → beyond MaxSkip → dropped.
        pair.BobChat.HandleIncoming(pair.AliceTransport.Outbox[39]);
        Assert.Single(bDelivered);

        // State not corrupted: seq 2 (next expected) still decrypts.
        pair.BobChat.HandleIncoming(pair.AliceTransport.Outbox[1]);
        Assert.Equal(2, bDelivered.Count);
        Assert.Equal("m2", bDelivered[1].Plaintext);
    }

    // ---------------------------------------------------------------------
    // (e) A replayed frame (seq <= last) is rejected
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ReplayedFrame_IsRejected()
    {
        using var pair = SetupPeers();
        var bDelivered = new List<IncomingMessage>();
        pair.BobChat.MessageDelivered += bDelivered.Add;

        var aConv = pair.AliceChat.EnsureConversation(pair.BobOnAlice);
        pair.BobChat.EnsureConversation(pair.AliceOnBob);

        await pair.AliceChat.SendText(aConv.Id, "m1");
        await pair.AliceChat.SendText(aConv.Id, "m2");
        await pair.AliceChat.SendText(aConv.Id, "m3");

        pair.BobChat.HandleIncoming(pair.AliceTransport.Outbox[0]);
        pair.BobChat.HandleIncoming(pair.AliceTransport.Outbox[1]);
        pair.BobChat.HandleIncoming(pair.AliceTransport.Outbox[2]);
        Assert.Equal(3, bDelivered.Count);

        // Replay seq 2.
        pair.BobChat.HandleIncoming(pair.AliceTransport.Outbox[1]);
        Assert.Equal(3, bDelivered.Count);

        // State intact: seq 4 still decrypts.
        await pair.AliceChat.SendText(aConv.Id, "m4");
        pair.BobChat.HandleIncoming(pair.AliceTransport.Outbox[3]);
        Assert.Equal(4, bDelivered.Count);
        Assert.Equal("m4", bDelivered[3].Plaintext);
    }

    // ---------------------------------------------------------------------
    // (f) seq tampering is rejected (context binding)
    // ---------------------------------------------------------------------
    [Fact]
    public async Task SeqTampering_IsRejected()
    {
        using var pair = SetupPeers();
        var bDelivered = new List<IncomingMessage>();
        pair.BobChat.MessageDelivered += bDelivered.Add;

        var aConv = pair.AliceChat.EnsureConversation(pair.BobOnAlice);
        pair.BobChat.EnsureConversation(pair.AliceOnBob);

        await pair.AliceChat.SendText(aConv.Id, "secret");
        var original = pair.AliceTransport.Outbox[0];

        var tamperedSeq = WireMessage.Deserialize(original);
        tamperedSeq.Seq = 999;
        pair.BobChat.HandleIncoming(tamperedSeq.Serialize());
        Assert.Empty(bDelivered);

        // The untampered frame still delivers.
        pair.BobChat.HandleIncoming(original);
        Assert.Single(bDelivered);
        Assert.Equal("secret", bDelivered[0].Plaintext);
    }

    // ---------------------------------------------------------------------
    // (g) Offline send marks Failed; RetryPending later succeeds
    // ---------------------------------------------------------------------
    [Fact]
    public async Task OfflineSend_MarksFailed_AndRetrySucceeds()
    {
        using var aDb = Database.InMemory();
        using var bDb = Database.InMemory();
        var (a, b) = TestData.OrderedIdentities(); // a = initiator

        var aT = new MockTransport("a");
        var bT = new MockTransport("b"); // not connected yet

        var aContacts = new ContactsService(aDb);
        var bContacts = new ContactsService(bDb);
        var bOnA = aContacts.AddContact("Bob", b.SigningPublicKey, b.DhPublicKey, b.EphemeralDhPublicKey);
        var aOnB = bContacts.AddContact("Alice", a.SigningPublicKey, a.DhPublicKey, a.EphemeralDhPublicKey);

        var aChat = new ChatService(a, aT, aDb);
        var bChat = new ChatService(b, bT, bDb);
        var bDelivered = new List<IncomingMessage>();
        bChat.MessageDelivered += bDelivered.Add;
        await bChat.StartAsync();

        var aConv = aChat.EnsureConversation(bOnA);
        var bConv = bChat.EnsureConversation(aOnB);

        // No connected peer → send fails.
        var message = await aChat.SendText(aConv.Id, "hello offline");
        Assert.Equal(MessageStatus.Failed, message.Status);
        Assert.Single(aChat.ListPending(aConv.Id));

        // Connect and retry: the prekey is handed to the transport (now Pending,
        // awaiting the responder's implicit ACK).
        aT.Connect(bT);
        var retried = await aChat.RetryPending(aConv.Id);
        Assert.Equal(1, retried);
        Assert.Equal("hello offline", bDelivered.Single().Plaintext);

        var stored = new MessageRepository(aDb.Connection).ForConversation(aConv.Id);
        Assert.Single(stored);
        Assert.Equal(MessageStatus.Pending, stored[0].Status); // prekey awaiting ACK
        Assert.Single(aChat.ListPending(aConv.Id));

        // Bob replies → Alice's prekey is implicitly ACKed → Delivered.
        await bChat.SendText(bConv.Id, "got it");
        aChat.HandleIncoming(bT.LastSentPayload!);
        Assert.Equal(MessageStatus.Delivered, new MessageRepository(aDb.Connection).ForConversation(aConv.Id)[0].Status);
        Assert.Empty(aChat.ListPending(aConv.Id));
    }

    // ---------------------------------------------------------------------
    // (h) Session key cannot be derived without the signed prekey (SPK)
    // ---------------------------------------------------------------------
    [Fact]
    public void SessionKey_CannotBeDerived_WithoutSpk()
    {
        var alice = KeyGeneration.CreateIdentity();
        var bob = KeyGeneration.CreateIdentity();
        var ek = KeyGeneration.GenerateX25519KeyPair();
        try
        {
            // Peer with no SPK public key (empty).
            var legacyBob = new IdentityKeyPair(bob.SigningPublicKey, bob.SigningPrivateKey, bob.DhPublicKey, bob.DhPrivateKey);
            Assert.ThrowsAny<Exception>(() =>
                CryptoSession.DeriveSessionKeyAsInitiator(alice, ek.PrivateKey, legacyBob.DhPublicKey, legacyBob.EphemeralDhPublicKey, CryptoSession.DefaultInfo));

            // Local identity with no SPK private key.
            var legacyAlice = new IdentityKeyPair(alice.SigningPublicKey, alice.SigningPrivateKey, alice.DhPublicKey, alice.DhPrivateKey);
            Assert.ThrowsAny<Exception>(() =>
                CryptoSession.DeriveSessionKeyAsResponder(legacyAlice, bob.DhPublicKey, ek.PublicKey, CryptoSession.DefaultInfo));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ek.PrivateKey);
        }
    }

    [Fact]
    public void SessionKey_DependsOnOneTimeEk_NotJustStaticDh()
    {
        var alice = KeyGeneration.CreateIdentity();
        var bob = KeyGeneration.CreateIdentity();
        var ek = KeyGeneration.GenerateX25519KeyPair();
        try
        {
            var session = CryptoSession.DeriveSessionKeyAsInitiator(
                alice, ek.PrivateKey, bob.DhPublicKey, bob.EphemeralDhPublicKey, CryptoSession.DefaultInfo);

            // The old static-static construction must NOT reproduce the session key.
            var staticShared = KeyAgreement.Agree(alice.DhPrivateKey, bob.DhPublicKey);
            var staticKey = KeyAgreement.DeriveSessionKey(staticShared, new byte[32], CryptoSession.DefaultInfo);
            Assert.False(session.SequenceEqual(staticKey));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ek.PrivateKey);
        }
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

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

        public void Deliver(byte[] payload) => MessageReceived?.Invoke(payload);

        public void Dispose() { }
    }

    private sealed class PeerPair : IDisposable
    {
        public required ChatService AliceChat { get; init; }
        public required ChatService BobChat { get; init; }
        public required Contact BobOnAlice { get; init; }
        public required Contact AliceOnBob { get; init; }
        public required Database AliceDb { get; init; }
        public required Database BobDb { get; init; }
        public required BufferingTransport AliceTransport { get; init; }
        public required BufferingTransport BobTransport { get; init; }

        public void Dispose()
        {
            AliceChat.Dispose();
            BobChat.Dispose();
            AliceTransport.Dispose();
            BobTransport.Dispose();
            AliceDb.Dispose();
            BobDb.Dispose();
        }
    }

    private static PeerPair SetupPeers()
    {
        var aliceDb = Database.InMemory();
        var bobDb = Database.InMemory();
        var (alice, bob) = TestData.OrderedIdentities(); // alice = initiator

        var aliceTransport = new BufferingTransport();
        var bobTransport = new BufferingTransport();

        var aliceContacts = new ContactsService(aliceDb);
        var bobContacts = new ContactsService(bobDb);
        var bobOnAlice = aliceContacts.AddContact("Bob", bob.SigningPublicKey, bob.DhPublicKey, bob.EphemeralDhPublicKey);
        var aliceOnBob = bobContacts.AddContact("Alice", alice.SigningPublicKey, alice.DhPublicKey, alice.EphemeralDhPublicKey);

        return new PeerPair
        {
            AliceChat = new ChatService(alice, aliceTransport, aliceDb),
            BobChat = new ChatService(bob, bobTransport, bobDb),
            BobOnAlice = bobOnAlice,
            AliceOnBob = aliceOnBob,
            AliceDb = aliceDb,
            BobDb = bobDb,
            AliceTransport = aliceTransport,
            BobTransport = bobTransport,
        };
    }
}
