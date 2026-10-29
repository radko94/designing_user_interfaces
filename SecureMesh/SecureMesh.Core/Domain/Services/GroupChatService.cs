using System.Security.Cryptography;
using System.Text;
using SecureMesh.Core.Domain.Persistence;

namespace SecureMesh.Core.Domain;

/// <summary>
/// Group chat following the pairwise-E2E model: every member holds a separate
/// <see cref="GroupMemberKey"/> (session key + ratchet) for every OTHER member,
/// and a group message is encrypted once per member. A recipient decrypts only
/// the copy encrypted with their own pairwise key. Store-and-forward catch-up
/// is provided by <see cref="SyncService"/> (wired through the group's local
/// conversation).
/// </summary>
public sealed class GroupChatService : IDisposable
{
    private readonly IdentityKeyPair _localIdentity;
    private readonly ITransport _transport;
    private readonly ContactRepository _contacts;
    private readonly ConversationRepository _conversations;
    private readonly MessageRepository _messages;
    private readonly SyncStateRepository _syncStates;
    private readonly GroupRepository _groups;
    private readonly GroupMemberRepository _members;
    private readonly GroupMemberKeyRepository _memberKeys;
    private readonly GroupMessageEnvelopeRepository _groupEnvelopes;
    private readonly SyncService _sync;

    private bool _started;

    /// <summary>Raised when an incoming group message has been decrypted and stored.</summary>
    public event Action<IncomingMessage>? MessageDelivered;

    public GroupChatService(IdentityKeyPair localIdentity, ITransport transport, Database db)
    {
        ArgumentNullException.ThrowIfNull(localIdentity);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(db);

        _localIdentity = localIdentity;
        _transport = transport;
        _contacts = new ContactRepository(db.Connection);
        _conversations = new ConversationRepository(db.Connection);
        _messages = new MessageRepository(db.Connection);
        _syncStates = new SyncStateRepository(db.Connection);
        _groups = new GroupRepository(db.Connection);
        _members = new GroupMemberRepository(db.Connection);
        _memberKeys = new GroupMemberKeyRepository(db.Connection);
        _groupEnvelopes = new GroupMessageEnvelopeRepository(db.Connection);
        _sync = new SyncService(_messages, _syncStates, _groupEnvelopes);
    }

    public Task StartAsync()
    {
        if (_started)
            return Task.CompletedTask;

        _started = true;
        _transport.MessageReceived += HandleIncomingGroupMessage;
        return _transport.StartAsync();
    }

    public Task StopAsync()
    {
        if (!_started)
            return Task.CompletedTask;

        _started = false;
        _transport.MessageReceived -= HandleIncomingGroupMessage;
        return _transport.StopAsync();
    }

    // ------------------------------------------------------------------
    // Group management
    // ------------------------------------------------------------------

    /// <summary>
    /// Creates a group (and its local conversation) with the given OTHER members,
    /// deriving a pairwise <see cref="GroupMemberKey"/> for each member. The
    /// stable <paramref name="groupKey"/> is shared out-of-band so every member
    /// creates the same logical group; when omitted a fresh one is generated.
    /// </summary>
    public Group CreateGroup(string name, IReadOnlyList<Contact> members, string? groupKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(members);

        var conversation = new Conversation
        {
            PeerId = null,
            IsGroup = true,
            SessionKey = Array.Empty<byte>(),
            RatchetState = Array.Empty<byte>(),
        };
        _conversations.Insert(conversation);

        var group = new Group
        {
            Name = name,
            GroupKey = groupKey ?? NewGroupKey(),
            ConversationId = conversation.Id,
        };
        _groups.Insert(group);

        foreach (var member in members)
            AddMember(group.Id, member);

        return group;
    }

    public Group? GetGroup(int groupId) => _groups.Get(groupId);

    public IReadOnlyList<Group> ListGroups() => _groups.All();

    public Group? FindGroupByKey(string groupKey) => _groups.FindByKey(groupKey);

    public IReadOnlyList<GroupMember> ListMembers(int groupId) => _members.ForGroup(groupId);

    /// <summary>Adds a member and derives the pairwise session key with them.</summary>
    public GroupMember AddMember(int groupId, Contact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);
        _ = _groups.Get(groupId) ?? throw new KeyNotFoundException($"Group {groupId} not found.");

        foreach (var existing in _members.ForGroup(groupId))
        {
            if (existing.ContactId == contact.Id)
                return existing;
        }

        var member = new GroupMember { GroupId = groupId, ContactId = contact.Id };
        _members.Insert(member);

        EstablishMemberKey(groupId, contact);
        return member;
    }

    public void RemoveMember(int groupId, int contactId)
    {
        _members.Remove(groupId, contactId);
        _memberKeys.DeleteForGroupAndContact(groupId, contactId);
    }

    /// <summary>
    /// Returns (or derives) the pairwise session key + ratchet state shared with
    /// <paramref name="contact"/> inside <paramref name="groupId"/>. Both sides
    /// derive the identical key from their own DH private key and the peer's
    /// public key.
    /// </summary>
    public GroupMemberKey EstablishMemberKey(int groupId, Contact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);

        var existing = _memberKeys.FindByGroupAndContact(groupId, contact.Id);
        if (existing is not null)
            return existing;

        var sessionKey = CryptoSession.EstablishSessionKey(_localIdentity.DhPrivateKey, contact.X25519Pub);
        var key = new GroupMemberKey
        {
            GroupId = groupId,
            MemberContactId = contact.Id,
            SessionKey = (byte[])sessionKey.Clone(),
            RatchetState = (byte[])sessionKey.Clone(),
        };
        _memberKeys.Insert(key);
        return key;
    }

    public GroupMemberKey? GetMemberKey(int groupId, int contactId) =>
        _memberKeys.FindByGroupAndContact(groupId, contactId);

    // ------------------------------------------------------------------
    // Sending
    // ------------------------------------------------------------------

    /// <summary>
    /// Encrypts <paramref name="text"/> separately to every member of
    /// <paramref name="groupId"/> (each under that member's own pairwise key),
    /// stores the outgoing message + per-member ciphertexts, and sends a single
    /// <see cref="GroupWireMessage"/> over the transport.
    /// </summary>
    public async Task<Message> SendGroupMessage(int groupId, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var group = _groups.Get(groupId) ?? throw new KeyNotFoundException($"Group {groupId} not found.");
        var conversationId = ConversationIdOf(group);

        var members = _members.ForGroup(groupId);
        if (members.Count == 0)
            throw new InvalidOperationException("The group has no members.");

        var seq = _messages.MaxSeq(conversationId) + 1;
        var recipients = new List<GroupWireRecipient>();

        foreach (var member in members)
        {
            var key = EnsureMemberKey(groupId, member.ContactId);
            var session = new CryptoSession(
                key.SessionKey,
                key.RatchetState,
                _localIdentity.SigningPublicKey,
                _localIdentity.SigningPrivateKey);

            var envelope = session.Seal(Encoding.UTF8.GetBytes(text));

            key.RatchetState = session.RatchetState;
            _memberKeys.Update(key);

            recipients.Add(new GroupWireRecipient
            {
                RecipientContactId = member.ContactId,
                Nonce = envelope.Nonce,
                Ciphertext = envelope.Ciphertext,
                Tag = envelope.Tag,
                Signature = envelope.Signature,
            });

            _groupEnvelopes.Insert(new GroupMessageEnvelope
            {
                ConversationId = conversationId,
                Seq = seq,
                SenderPublic = _localIdentity.SigningPublicKey,
                RecipientContactId = member.ContactId,
                Nonce = envelope.Nonce,
                Ciphertext = envelope.Ciphertext,
                Tag = envelope.Tag,
                Signature = envelope.Signature,
            });
        }

        _messages.Insert(new Message
        {
            ConversationId = conversationId,
            Seq = seq,
            SenderId = 0, // 0 == self
            Timestamp = DateTime.UtcNow,
            Status = MessageStatus.Sent,
        });

        var wire = new GroupWireMessage
        {
            GroupKey = group.GroupKey,
            Seq = seq,
            SenderPublic = _localIdentity.SigningPublicKey,
            Recipients = recipients,
        };
        await _transport.SendAsync(wire.Serialize());

        return _messages.ForConversation(conversationId).Single(m => m.Seq == seq);
    }

    // ------------------------------------------------------------------
    // Receiving
    // ------------------------------------------------------------------

    /// <summary>
    /// Invoked by <see cref="ITransport.MessageReceived"/>. Authenticates the
    /// sender, decrypts ONLY this member's own copy, stores the message and all
    /// per-member ciphertexts (for store-and-forward relay), and raises
    /// <see cref="MessageDelivered"/>. Tampered or foreign copies are dropped.
    /// </summary>
    public void HandleIncomingGroupMessage(byte[] rawPayload)
    {
        ArgumentNullException.ThrowIfNull(rawPayload);

        GroupWireMessage wire;
        try
        {
            wire = GroupWireMessage.Deserialize(rawPayload);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or ArgumentException)
        {
            return;
        }

        var group = _groups.FindByKey(wire.GroupKey);
        if (group is null)
            return; // unknown group

        var sender = _contacts.FindByEd25519Pub(wire.SenderPublic);
        if (sender is null)
            return; // unknown sender

        var conversationId = ConversationIdOf(group);
        var candidates = wire.Recipients.Select(r => r.ToEnvelope(wire.SenderPublic)).ToList();
        if (!TryDeliver(group, conversationId, wire.Seq, sender, candidates))
            return; // no copy for us, or tampered

        if (!_messages.Exists(conversationId, wire.Seq))
        {
            _messages.Insert(new Message
            {
                ConversationId = conversationId,
                Seq = wire.Seq,
                SenderId = sender.Id,
                Timestamp = DateTime.UtcNow,
                Status = MessageStatus.Received,
            });
        }

        foreach (var recipient in wire.Recipients)
        {
            if (_groupEnvelopes.Exists(conversationId, wire.Seq, recipient.RecipientContactId))
                continue;

            _groupEnvelopes.Insert(new GroupMessageEnvelope
            {
                ConversationId = conversationId,
                Seq = wire.Seq,
                SenderPublic = wire.SenderPublic,
                RecipientContactId = recipient.RecipientContactId,
                Nonce = recipient.Nonce,
                Ciphertext = recipient.Ciphertext,
                Tag = recipient.Tag,
                Signature = recipient.Signature,
            });
        }
    }

    // ------------------------------------------------------------------
    // Store-and-forward sync
    // ------------------------------------------------------------------

    /// <summary>Produces a catch-up payload for the group's conversation.</summary>
    public SyncPayload ProduceSyncPayload(int groupId, long afterSeq)
    {
        var group = _groups.Get(groupId) ?? throw new KeyNotFoundException($"Group {groupId} not found.");
        return _sync.ProducePayload(ConversationIdOf(group), afterSeq);
    }

    /// <summary>
    /// Applies a catch-up payload for a group: stores the missed messages and
    /// per-member ciphertexts, then decrypts and delivers this member's own copy
    /// of each newly-applied message (in sequence order). Returns the highest
    /// sequence number that was newly applied (0 if nothing new).
    /// </summary>
    public long ApplySyncPayload(int groupId, SyncPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var group = _groups.Get(groupId) ?? throw new KeyNotFoundException($"Group {groupId} not found.");
        var conversationId = ConversationIdOf(group);

        var newSeqs = new List<long>();
        foreach (var src in payload.Messages)
        {
            if (src.Seq <= 0 || _messages.Exists(conversationId, src.Seq))
                continue;

            _messages.Insert(new Message
            {
                ConversationId = conversationId,
                Seq = src.Seq,
                SenderId = src.SenderId,
                Timestamp = src.Timestamp,
                Status = MessageStatus.Received,
            });
            newSeqs.Add(src.Seq);
        }

        foreach (var e in payload.GroupEnvelopes ?? new List<GroupMessageEnvelope>())
        {
            if (e.Seq <= 0 || _groupEnvelopes.Exists(conversationId, e.Seq, e.RecipientContactId))
                continue;

            _groupEnvelopes.Insert(new GroupMessageEnvelope
            {
                ConversationId = conversationId,
                Seq = e.Seq,
                SenderPublic = e.SenderPublic,
                RecipientContactId = e.RecipientContactId,
                Nonce = e.Nonce,
                Ciphertext = e.Ciphertext,
                Tag = e.Tag,
                Signature = e.Signature,
            });
        }

        long maxApplied = 0;
        foreach (var seq in newSeqs.OrderBy(s => s))
        {
            var envelopes = _groupEnvelopes.ForSeq(conversationId, seq);
            if (envelopes.Count == 0)
                continue;

            var sender = _contacts.FindByEd25519Pub(envelopes[0].SenderPublic);
            if (sender is null)
                continue;

            var candidates = envelopes
                .Select(e => new MessageEnvelope(e.SenderPublic, e.Nonce, e.Ciphertext, e.Tag, e.Signature))
                .ToList();

            if (TryDeliver(group, conversationId, seq, sender, candidates))
                maxApplied = Math.Max(maxApplied, seq);
        }

        return maxApplied;
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private static int ConversationIdOf(Group group) =>
        group.ConversationId
        ?? throw new InvalidOperationException($"Group {group.Id} has no conversation.");

    private static string NewGroupKey()
    {
        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private GroupMemberKey EnsureMemberKey(int groupId, int contactId)
    {
        var existing = _memberKeys.FindByGroupAndContact(groupId, contactId);
        if (existing is not null)
            return existing;

        var contact = _contacts.Get(contactId)
            ?? throw new KeyNotFoundException($"Contact {contactId} not found.");
        return EstablishMemberKey(groupId, contact);
    }

    /// <summary>
    /// Decrypts this member's own copy from <paramref name="candidates"/> using
    /// the pairwise key shared with <paramref name="sender"/>, commits the
    /// advanced ratchet state, updates sync state, and raises
    /// <see cref="MessageDelivered"/>. Returns false when no candidate decrypts.
    /// </summary>
    private bool TryDeliver(
        Group group,
        int conversationId,
        long seq,
        Contact sender,
        IReadOnlyList<MessageEnvelope> candidates)
    {
        var memberKey = _memberKeys.FindByGroupAndContact(group.Id, sender.Id);
        if (memberKey is null)
            return false; // we have no pairwise session with this sender in this group

        foreach (var candidate in candidates)
        {
            var session = new CryptoSession(
                memberKey.SessionKey,
                memberKey.RatchetState,
                _localIdentity.SigningPublicKey,
                _localIdentity.SigningPrivateKey);

            byte[] plaintext;
            try
            {
                plaintext = session.Open(candidate);
            }
            catch (CryptographicException)
            {
                continue; // not our copy (or tampered): try the next candidate
            }

            memberKey.RatchetState = session.RatchetState;
            _memberKeys.Update(memberKey);

            var lastSeq = _syncStates.Get(conversationId, sender.Id)?.LastSeq ?? 0;
            _syncStates.Upsert(conversationId, sender.Id, Math.Max(lastSeq, seq));

            MessageDelivered?.Invoke(new IncomingMessage(
                conversationId,
                sender.Id,
                seq,
                Encoding.UTF8.GetString(plaintext),
                DateTime.UtcNow));
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_started)
        {
            _started = false;
            _transport.MessageReceived -= HandleIncomingGroupMessage;
        }
    }
}
