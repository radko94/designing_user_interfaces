using System.Text;
using System.Text.Json;
using SecureMesh.Core;
using SecureMesh.Core.Domain;
using SecureMesh.Core.Domain.Persistence;

namespace SecureMesh.Core.Tests;

public class GroupChatTests
{
    private const string GroupKey = "trip-xyz";

    // ---------------------------------------------------------------------
    // 1. Pairwise sessions are established identically by both sides (lazily)
    // ---------------------------------------------------------------------
    [Fact]
    public async Task GroupMemberKeys_AreEstablishedIdentically_ByBothSides()
    {
        using var aliceDb = Database.InMemory();
        using var bobDb = Database.InMemory();

        var alice = KeyGeneration.CreateIdentity();
        var bob = KeyGeneration.CreateIdentity();

        var aliceTransport = new MockTransport("alice");
        var bobTransport = new MockTransport("bob");
        aliceTransport.Connect(bobTransport);

        var aliceChat = new GroupChatService(alice, aliceTransport, aliceDb);
        var bobChat = new GroupChatService(bob, bobTransport, bobDb);
        var bobDelivered = new List<IncomingMessage>();
        bobChat.MessageDelivered += bobDelivered.Add;

        var bobContactOnAlice = new ContactsService(aliceDb).AddContact("Bob", bob.SigningPublicKey, bob.DhPublicKey, bob.EphemeralDhPublicKey);
        var aliceContactOnBob = new ContactsService(bobDb).AddContact("Alice", alice.SigningPublicKey, alice.DhPublicKey, alice.EphemeralDhPublicKey);

        var aliceGroupId = aliceChat.CreateGroup("Pair", new[] { bobContactOnAlice }, GroupKey).Id;
        var bobGroupId = bobChat.CreateGroup("Pair", new[] { aliceContactOnBob }, GroupKey).Id;

        // Lazy establishment: placeholder keys start unestablished.
        Assert.False(aliceChat.GetMemberKey(aliceGroupId, bobContactOnAlice.Id)!.IsEstablished);

        // Alice's first message is a prekey frame that establishes both sides.
        await aliceChat.SendGroupMessage(aliceGroupId, "hello");
        bobChat.HandleIncomingGroupMessage(aliceTransport.LastSentPayload!);
        Assert.Single(bobDelivered);

        var aliceKey = aliceChat.GetMemberKey(aliceGroupId, bobContactOnAlice.Id)!;
        var bobKey = bobChat.GetMemberKey(bobGroupId, aliceContactOnBob.Id)!;

        Assert.True(aliceKey.IsEstablished);
        Assert.True(bobKey.IsEstablished);
        Assert.Equal(aliceKey.SendRatchetState, bobKey.RecvRatchetState);
        Assert.Equal(aliceKey.RecvRatchetState, bobKey.SendRatchetState);
    }

    // ---------------------------------------------------------------------
    // 2. A 3-identity group: encrypt-to-each-member round-trips, and each
    //    member decrypts ONLY their own copy.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task GroupSend_EncryptsToEachMember_EachDecryptsOnlyTheirOwn()
    {
        using var s = SetupThree();

        await s.Alice.Chat.SendGroupMessage(s.AliceGroupId, "hello group");

        var wire = s.Alice.Transport.LastSentPayload;
        Assert.NotNull(wire);
        Assert.DoesNotContain("hello group", Encoding.UTF8.GetString(wire));

        s.Bob.Chat.HandleIncomingGroupMessage(wire);
        s.Carol.Chat.HandleIncomingGroupMessage(wire);

        Assert.Single(s.Bob.Delivered);
        Assert.Equal("hello group", s.Bob.Delivered[0].Plaintext);
        Assert.Single(s.Carol.Delivered);
        Assert.Equal("hello group", s.Carol.Delivered[0].Plaintext);

        // Swap Bob's copy for Carol's copy: Bob's pairwise key must NOT decrypt it.
        var parsed = JsonSerializer.Deserialize<GroupWireMessage>(wire)!;
        var carolRecipient = parsed.Recipients.Single(r => r.RecipientContactId == s.CarolOnAlice.Id);
        var swappedRecipients = parsed.Recipients.Select(r =>
            r.RecipientContactId == s.BobOnAlice.Id
                ? new GroupWireRecipient
                {
                    RecipientContactId = s.BobOnAlice.Id,
                    Nonce = carolRecipient.Nonce,
                    Ciphertext = carolRecipient.Ciphertext,
                    Tag = carolRecipient.Tag,
                    Signature = carolRecipient.Signature,
                }
                : r).ToList();
        var swappedWire = new GroupWireMessage
        {
            GroupKey = parsed.GroupKey,
            Seq = parsed.Seq,
            SenderPublic = parsed.SenderPublic,
            Recipients = swappedRecipients,
        }.Serialize();

        var bobBefore = s.Bob.Delivered.Count;
        s.Bob.Chat.HandleIncomingGroupMessage(swappedWire);
        Assert.Equal(bobBefore, s.Bob.Delivered.Count); // dropped: not Bob's copy (and already delivered)
    }

    // ---------------------------------------------------------------------
    // 3. Tamper detection on a per-member ciphertext.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task GroupSend_TamperedCiphertext_IsDropped()
    {
        using var s = SetupThree();

        await s.Alice.Chat.SendGroupMessage(s.AliceGroupId, "secret");
        var wire = s.Alice.Transport.LastSentPayload!;

        // Tamper Bob's copy in a fresh wire; Carol's copy stays intact.
        var parsed = JsonSerializer.Deserialize<GroupWireMessage>(wire)!;
        foreach (var r in parsed.Recipients)
        {
            if (r.RecipientContactId == s.BobOnAlice.Id)
                r.Ciphertext[0] ^= 0x01;
        }
        var tampered = parsed.Serialize();

        s.Bob.Chat.HandleIncomingGroupMessage(tampered);
        Assert.Empty(s.Bob.Delivered); // tampered copy is dropped

        // Carol's copy in the ORIGINAL wire still decrypts.
        s.Carol.Chat.HandleIncomingGroupMessage(wire);
        Assert.Single(s.Carol.Delivered);
        Assert.Equal("secret", s.Carol.Delivered[0].Plaintext);
    }

    // ---------------------------------------------------------------------
    // 4. A member who missed messages catches up via sync.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task MemberWhoMissedMessage_CatchesUpViaSync()
    {
        using var s = SetupThree();

        await s.Alice.Chat.SendGroupMessage(s.AliceGroupId, "first");
        var w1 = s.Alice.Transport.LastSentPayload!;
        await s.Alice.Chat.SendGroupMessage(s.AliceGroupId, "second");
        var w2 = s.Alice.Transport.LastSentPayload!;

        // Bob receives both live.
        s.Bob.Chat.HandleIncomingGroupMessage(w1);
        s.Bob.Chat.HandleIncomingGroupMessage(w2);
        Assert.Equal(2, s.Bob.Delivered.Count);

        // Carol was offline and missed both.
        Assert.Empty(s.Carol.Delivered);

        var aliceConversationId = s.Alice.Chat.GetGroup(s.AliceGroupId)!.ConversationId!.Value;
        var carolConversationId = s.Carol.Chat.GetGroup(s.CarolGroupId)!.ConversationId!.Value;

        var aliceLast = new MessageRepository(s.Alice.Db.Connection).MaxSeq(aliceConversationId);
        var carolLast = new MessageRepository(s.Carol.Db.Connection).MaxSeq(carolConversationId);

        var missing = SyncService.ComputeMissingRanges(carolLast, aliceLast);
        Assert.Single(missing);
        Assert.Equal(new SeqRange(1, 2), missing[0]);

        var payload = s.Alice.Chat.ProduceSyncPayload(s.AliceGroupId, afterSeq: carolLast);
        Assert.Equal(2, payload.Messages.Count);
        Assert.Equal(4, payload.GroupEnvelopes.Count);

        var applied = s.Carol.Chat.ApplySyncPayload(s.CarolGroupId, payload);
        Assert.Equal(2, applied);

        Assert.Equal(2, s.Carol.Delivered.Count);
        Assert.Equal("first", s.Carol.Delivered[0].Plaintext);
        Assert.Equal("second", s.Carol.Delivered[1].Plaintext);
    }

    // ---------------------------------------------------------------------
    // 5. A member who joins later catches up with post-join messages only.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task MemberWhoJoinsLater_CatchesUpWithPostJoinMessagesOnly()
    {
        using var aliceDb = Database.InMemory();
        using var bobDb = Database.InMemory();
        using var carolDb = Database.InMemory();

        var alice = KeyGeneration.CreateIdentity();
        var bob = KeyGeneration.CreateIdentity();
        var carol = KeyGeneration.CreateIdentity();

        var aliceTransport = new MockTransport("alice");
        var bobTransport = new MockTransport("bob");
        aliceTransport.Connect(bobTransport);

        var aliceContacts = new ContactsService(aliceDb);
        var bobContacts = new ContactsService(bobDb);
        var carolContacts = new ContactsService(carolDb);

        var aliceChat = new GroupChatService(alice, aliceTransport, aliceDb);
        var bobChat = new GroupChatService(bob, bobTransport, bobDb);
        var carolChat = new GroupChatService(carol, new MockTransport("carol"), carolDb);

        var bobDelivered = new List<IncomingMessage>();
        var carolDelivered = new List<IncomingMessage>();
        bobChat.MessageDelivered += bobDelivered.Add;
        carolChat.MessageDelivered += carolDelivered.Add;

        // Alice + Bob only, at first.
        var bobOnAlice = aliceContacts.AddContact("Bob", bob.SigningPublicKey, bob.DhPublicKey, bob.EphemeralDhPublicKey);
        var aliceOnBob = bobContacts.AddContact("Alice", alice.SigningPublicKey, alice.DhPublicKey, alice.EphemeralDhPublicKey);

        var aliceGroupId = aliceChat.CreateGroup("Trip", new[] { bobOnAlice }, GroupKey).Id;
        var bobGroupId = bobChat.CreateGroup("Trip", new[] { aliceOnBob }, GroupKey).Id;

        // Alice sends before Carol joins.
        await aliceChat.SendGroupMessage(aliceGroupId, "before-join");
        bobChat.HandleIncomingGroupMessage(aliceTransport.LastSentPayload!);
        Assert.Single(bobDelivered);

        // Carol joins: Alice adds her; Carol creates the same group.
        var carolOnAlice = aliceContacts.AddContact("Carol", carol.SigningPublicKey, carol.DhPublicKey, carol.EphemeralDhPublicKey);
        var aliceOnCarol = carolContacts.AddContact("Alice", alice.SigningPublicKey, alice.DhPublicKey, alice.EphemeralDhPublicKey);
        var bobOnCarol = carolContacts.AddContact("Bob", bob.SigningPublicKey, bob.DhPublicKey, bob.EphemeralDhPublicKey);

        aliceChat.AddMember(aliceGroupId, carolOnAlice);
        var carolGroupId = carolChat.CreateGroup("Trip", new[] { aliceOnCarol, bobOnCarol }, GroupKey).Id;

        // Alice sends after Carol joins; Carol is offline (does not receive live).
        await aliceChat.SendGroupMessage(aliceGroupId, "after-join");
        var w2 = aliceTransport.LastSentPayload!;
        bobChat.HandleIncomingGroupMessage(w2);

        // Carol syncs from Alice and can only decrypt the post-join message.
        var aliceConversationId = aliceChat.GetGroup(aliceGroupId)!.ConversationId!.Value;
        var carolConversationId = carolChat.GetGroup(carolGroupId)!.ConversationId!.Value;
        var aliceLast = new MessageRepository(aliceDb.Connection).MaxSeq(aliceConversationId);
        var carolLast = new MessageRepository(carolDb.Connection).MaxSeq(carolConversationId);

        var payload = aliceChat.ProduceSyncPayload(aliceGroupId, afterSeq: carolLast);
        var applied = carolChat.ApplySyncPayload(carolGroupId, payload);

        Assert.Equal(2, applied);
        Assert.Single(carolDelivered);
        Assert.Equal("after-join", carolDelivered[0].Plaintext);
    }

    // ---------------------------------------------------------------------
    // 6. Add/remove member keeps the pairwise key table consistent.
    // ---------------------------------------------------------------------
    [Fact]
    public void AddAndRemoveMember_MaintainsMemberKeys()
    {
        using var aliceDb = Database.InMemory();
        var alice = KeyGeneration.CreateIdentity();
        var bob = KeyGeneration.CreateIdentity();
        var carol = KeyGeneration.CreateIdentity();

        var aliceChat = new GroupChatService(alice, new MockTransport("alice"), aliceDb);
        var contacts = new ContactsService(aliceDb);

        var bobOnAlice = contacts.AddContact("Bob", bob.SigningPublicKey, bob.DhPublicKey, bob.EphemeralDhPublicKey);
        var carolOnAlice = contacts.AddContact("Carol", carol.SigningPublicKey, carol.DhPublicKey, carol.EphemeralDhPublicKey);

        var groupId = aliceChat.CreateGroup("Team", new[] { bobOnAlice }, GroupKey).Id;

        Assert.Single(aliceChat.ListMembers(groupId));
        Assert.NotNull(aliceChat.GetMemberKey(groupId, bobOnAlice.Id));

        aliceChat.AddMember(groupId, carolOnAlice);
        Assert.Equal(2, aliceChat.ListMembers(groupId).Count);
        Assert.NotNull(aliceChat.GetMemberKey(groupId, carolOnAlice.Id));

        aliceChat.RemoveMember(groupId, bobOnAlice.Id);
        Assert.Single(aliceChat.ListMembers(groupId));
        Assert.Null(aliceChat.GetMemberKey(groupId, bobOnAlice.Id));
        Assert.NotNull(aliceChat.GetMemberKey(groupId, carolOnAlice.Id));
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------
    private sealed class Peer : IDisposable
    {
        public required string Name { get; init; }
        public IdentityKeyPair Identity { get; init; } = null!;
        public Database Db { get; init; } = null!;
        public MockTransport Transport { get; init; } = null!;
        public ContactsService Contacts { get; init; } = null!;
        public GroupChatService Chat { get; init; } = null!;
        public List<IncomingMessage> Delivered { get; } = new();

        public static Peer Create(string name)
        {
            var identity = KeyGeneration.CreateIdentity();
            var db = Database.InMemory();
            var transport = new MockTransport(name);
            var contacts = new ContactsService(db);
            var chat = new GroupChatService(identity, transport, db);
            var peer = new Peer
            {
                Name = name,
                Identity = identity,
                Db = db,
                Transport = transport,
                Contacts = contacts,
                Chat = chat,
            };
            chat.MessageDelivered += peer.Delivered.Add;
            return peer;
        }

        public Contact AddContact(Peer other) =>
            Contacts.AddContact(other.Name, other.Identity.SigningPublicKey, other.Identity.DhPublicKey, other.Identity.EphemeralDhPublicKey);

        public void Dispose()
        {
            Chat.Dispose();
            Transport.Dispose();
            Db.Dispose();
        }
    }

    private sealed class GroupSetup : IDisposable
    {
        public required Peer Alice { get; init; }
        public required Peer Bob { get; init; }
        public required Peer Carol { get; init; }
        public required int AliceGroupId { get; init; }
        public required int BobGroupId { get; init; }
        public required int CarolGroupId { get; init; }
        public required Contact BobOnAlice { get; init; }
        public required Contact CarolOnAlice { get; init; }
        public required Contact AliceOnBob { get; init; }
        public required Contact CarolOnBob { get; init; }
        public required Contact AliceOnCarol { get; init; }
        public required Contact BobOnCarol { get; init; }

        public void Dispose()
        {
            Alice.Dispose();
            Bob.Dispose();
            Carol.Dispose();
        }
    }

    private static GroupSetup SetupThree()
    {
        var alice = Peer.Create("Alice");
        var bob = Peer.Create("Bob");
        var carol = Peer.Create("Carol");
        alice.Transport.Connect(bob.Transport);

        var bobOnAlice = alice.AddContact(bob);
        var carolOnAlice = alice.AddContact(carol);
        var aliceOnBob = bob.AddContact(alice);
        var carolOnBob = bob.AddContact(carol);
        var aliceOnCarol = carol.AddContact(alice);
        var bobOnCarol = carol.AddContact(bob);

        var aliceGroupId = alice.Chat.CreateGroup("Trip", new[] { bobOnAlice, carolOnAlice }, GroupKey).Id;
        var bobGroupId = bob.Chat.CreateGroup("Trip", new[] { aliceOnBob, carolOnBob }, GroupKey).Id;
        var carolGroupId = carol.Chat.CreateGroup("Trip", new[] { aliceOnCarol, bobOnCarol }, GroupKey).Id;

        return new GroupSetup
        {
            Alice = alice,
            Bob = bob,
            Carol = carol,
            AliceGroupId = aliceGroupId,
            BobGroupId = bobGroupId,
            CarolGroupId = carolGroupId,
            BobOnAlice = bobOnAlice,
            CarolOnAlice = carolOnAlice,
            AliceOnBob = aliceOnBob,
            CarolOnBob = carolOnBob,
            AliceOnCarol = aliceOnCarol,
            BobOnCarol = bobOnCarol,
        };
    }
}
