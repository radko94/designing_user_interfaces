using System.Security.Cryptography;
using System.Text;
using SecureMesh.Core;
using SecureMesh.Core.Domain;
using SecureMesh.Core.Domain.Persistence;

namespace SecureMesh.Core.Tests;

public class MessagingDomainIntegrationTests
{
    // ---------------------------------------------------------------------
    // 1. Session key agreement
    // ---------------------------------------------------------------------
    [Fact]
    public void SessionKey_IsDerivedIdentically_ByBothParties()
    {
        var alice = KeyGeneration.CreateIdentity();
        var bob = KeyGeneration.CreateIdentity();

        var aliceKey = CryptoSession.EstablishSessionKey(alice.DhPrivateKey, bob.DhPublicKey);
        var bobKey = CryptoSession.EstablishSessionKey(bob.DhPrivateKey, alice.DhPublicKey);

        Assert.Equal(32, aliceKey.Length);
        Assert.Equal(aliceKey, bobKey);
    }

    // ---------------------------------------------------------------------
    // 2. CryptoSession ratchet round-trip + tamper detection
    // ---------------------------------------------------------------------
    [Fact]
    public void CryptoSession_SealOpen_RoundTrips_And_DetectsTampering()
    {
        var alice = KeyGeneration.CreateIdentity();
        var bob = KeyGeneration.CreateIdentity();
        var sessionKey = CryptoSession.EstablishSessionKey(alice.DhPrivateKey, bob.DhPublicKey);
        var plaintext = Encoding.UTF8.GetBytes("hello bob");

        var aliceSession = new CryptoSession(
            sessionKey, sessionKey, alice.SigningPublicKey, alice.SigningPrivateKey);
        var envelope = aliceSession.Seal(plaintext);

        var bobSession = new CryptoSession(
            sessionKey, sessionKey, bob.SigningPublicKey, bob.SigningPrivateKey);
        var opened = bobSession.Open(envelope);

        Assert.Equal(plaintext, opened);

        var tamperedCiphertext = envelope.Ciphertext;
        tamperedCiphertext[0] ^= 0x01;
        var tampered = new MessageEnvelope(
            envelope.SenderPublic, envelope.Nonce, tamperedCiphertext, envelope.Tag, envelope.Signature);

        Assert.ThrowsAny<CryptographicException>(() => bobSession.Open(tampered));
    }

    [Fact]
    public void CryptoSession_UsesFreshRatchetKey_ForEachMessage()
    {
        var alice = KeyGeneration.CreateIdentity();
        var bob = KeyGeneration.CreateIdentity();
        var sessionKey = CryptoSession.EstablishSessionKey(alice.DhPrivateKey, bob.DhPublicKey);

        var aliceSession = new CryptoSession(
            sessionKey, sessionKey, alice.SigningPublicKey, alice.SigningPrivateKey);
        var bobSession = new CryptoSession(
            sessionKey, sessionKey, bob.SigningPublicKey, bob.SigningPrivateKey);

        var first = aliceSession.Seal(Encoding.UTF8.GetBytes("one"));
        var second = aliceSession.Seal(Encoding.UTF8.GetBytes("two"));

        // Different message keys -> different ciphertext (and nonce).
        Assert.NotEqual(first.Ciphertext, second.Ciphertext);

        // Bob decrypts in order.
        Assert.Equal("one", Encoding.UTF8.GetString(bobSession.Open(first)));
        Assert.Equal("two", Encoding.UTF8.GetString(bobSession.Open(second)));
    }

    // ---------------------------------------------------------------------
    // 3. End-to-end through real services and two wired transports
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ChatService_SendText_DeliversEncryptedMessage_EndToEnd()
    {
        using var aliceDb = Database.InMemory();
        using var bobDb = Database.InMemory();

        var alice = KeyGeneration.CreateIdentity();
        var bob = KeyGeneration.CreateIdentity();

        var aliceTransport = new MockTransport("alice");
        var bobTransport = new MockTransport("bob");
        aliceTransport.Connect(bobTransport);

        var aliceContacts = new ContactsService(aliceDb);
        var bobContacts = new ContactsService(bobDb);

        var bobContactForAlice = aliceContacts.AddContact("Bob", bob.SigningPublicKey, bob.DhPublicKey);
        var aliceContactForBob = bobContacts.AddContact("Alice", alice.SigningPublicKey, alice.DhPublicKey);

        var aliceChat = new ChatService(alice, aliceTransport, aliceDb);
        var bobChat = new ChatService(bob, bobTransport, bobDb);

        var delivered = new List<IncomingMessage>();
        bobChat.MessageDelivered += delivered.Add;

        await aliceChat.StartAsync();
        await bobChat.StartAsync();

        var aliceConversation = aliceChat.EnsureConversation(bobContactForAlice);
        var bobConversation = bobChat.EnsureConversation(aliceContactForBob);

        // Sanity: both peers derived the same session key.
        Assert.Equal(aliceConversation.SessionKey, bobConversation.SessionKey);

        var message = await aliceChat.SendText(aliceConversation.Id, "hello bob");

        Assert.Equal(1, message.Seq);
        Assert.Equal(MessageStatus.Sent, message.Status);

        // The framed bytes on the wire must NOT be the plaintext.
        var wire = aliceTransport.LastSentPayload;
        Assert.NotNull(wire);
        Assert.DoesNotContain("hello bob", Encoding.UTF8.GetString(wire));

        // Bob received and decrypted exactly one message.
        Assert.Single(delivered);
        Assert.Equal("hello bob", delivered[0].Plaintext);
        Assert.Equal(bobConversation.Id, delivered[0].ConversationId);

        // Both sides persisted an encrypted message.
        var aliceMessages = new MessageRepository(aliceDb.Connection).ForConversation(aliceConversation.Id);
        var bobMessages = new MessageRepository(bobDb.Connection).ForConversation(bobConversation.Id);
        Assert.Single(aliceMessages);
        Assert.Single(bobMessages);
        Assert.Equal(MessageStatus.Received, bobMessages[0].Status);
        Assert.DoesNotContain("hello bob", Encoding.UTF8.GetString(bobMessages[0].Ciphertext));
    }

    // ---------------------------------------------------------------------
    // 4. Sync: missing-range computation and payload application
    // ---------------------------------------------------------------------
    [Fact]
    public void SyncService_ComputesMissingRange_And_AppliesPayload()
    {
        using var aliceDb = Database.InMemory();
        using var bobDb = Database.InMemory();

        const int conversationId = 1;
        const int alicePeerIdOnBob = 42; // arbitrary peer id for SyncState

        var aliceMessages = new MessageRepository(aliceDb.Connection);
        var bobMessages = new MessageRepository(bobDb.Connection);

        for (long seq = 1; seq <= 5; seq++)
        {
            aliceMessages.Insert(new Message
            {
                ConversationId = conversationId,
                Seq = seq,
                SenderId = 0,
                Timestamp = DateTime.UtcNow,
            });
        }

        for (long seq = 1; seq <= 3; seq++)
        {
            bobMessages.Insert(new Message
            {
                ConversationId = conversationId,
                Seq = seq,
                SenderId = 1,
                Timestamp = DateTime.UtcNow,
            });
        }

        var aliceSync = new SyncService(aliceDb);
        var bobSync = new SyncService(bobDb);

        var aliceLast = aliceMessages.MaxSeq(conversationId); // 5
        var bobLast = bobMessages.MaxSeq(conversationId);     // 3

        // Bob (known=3) is missing Alice's range up to 5.
        var missing = SyncService.ComputeMissingRanges(bobLast, aliceLast);
        Assert.Single(missing);
        Assert.Equal(new SeqRange(4, 5), missing[0]);

        // Alice produces what Bob is missing (seq > 3).
        var payload = aliceSync.ProducePayload(conversationId, afterSeq: bobLast);
        Assert.Equal(2, payload.Messages.Count);

        // Bob applies it and now has all five messages.
        var applied = bobSync.ApplyPayload(conversationId, alicePeerIdOnBob, payload);
        Assert.Equal(5, applied);

        var bobAll = bobMessages.ForConversation(conversationId);
        Assert.Equal(5, bobAll.Count);
        Assert.Equal(5, bobMessages.MaxSeq(conversationId));
    }

    // ---------------------------------------------------------------------
    // Supplementary domain coverage
    // ---------------------------------------------------------------------
    [Fact]
    public void ContactsService_AddListVerifyRemove()
    {
        using var db = Database.InMemory();
        var service = new ContactsService(db);
        var identity = KeyGeneration.CreateIdentity();

        var contact = service.AddContact("Alice", identity.SigningPublicKey, identity.DhPublicKey);
        Assert.False(contact.Verified);
        Assert.Single(service.ListContacts());

        service.VerifyContact(contact.Id);
        Assert.True(service.GetById(contact.Id)!.Verified);

        service.RemoveContact(contact.Id);
        Assert.Empty(service.ListContacts());
    }

    [Fact]
    public void GroupRepository_PersistsGroupsAndMembers()
    {
        using var db = Database.InMemory();
        var groups = new GroupRepository(db.Connection);
        var members = new GroupMemberRepository(db.Connection);

        var group = new Group { Name = "Team" };
        groups.Insert(group);
        members.Insert(new GroupMember { GroupId = group.Id, ContactId = 7 });

        var loaded = groups.All();
        Assert.Single(loaded);
        Assert.Equal("Team", loaded[0].Name);
        Assert.Single(members.ForGroup(group.Id));
    }
}
