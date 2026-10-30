using System.Security.Cryptography;
using System.Text;
using SecureMesh.Core.Domain.Persistence;

namespace SecureMesh.Core.Domain;

/// <summary>
/// Group chat following the pairwise-E2E model: every member holds a separate
/// <see cref="GroupMemberKey"/> (two direction-bound ratchet chains, no root
/// key) for every OTHER member, and a group message is encrypted once per
/// member. A recipient decrypts only the copy encrypted with their own pairwise
/// key. Pairwise sessions are established lazily via the same X3DH-lite prekey
/// mechanism as 1:1; a single one-time EK is reused across all members
/// established by one broadcast. Store-and-forward catch-up is provided by
/// <see cref="SyncService"/>.
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
    private readonly SkippedKeyRepository _skippedKeys;
    private readonly PendingMessageRepository _pending;
    private readonly SyncService _sync;

    private readonly Dictionary<(int ConversationId, int SenderId), ReceiveChainState> _receive = new();
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
        _skippedKeys = new SkippedKeyRepository(db.Connection);
        _pending = new PendingMessageRepository(db.Connection);
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
    /// Creates a group (and its local conversation) with the given OTHER members.
    /// The conversation's channel id is <c>SHA256(GroupKey)</c>. Pairwise member
    /// sessions are created unestablished and established lazily on first message.
    /// The stable <paramref name="groupKey"/> is shared out-of-band; when omitted
    /// a fresh one is generated.
    /// </summary>
    public Group CreateGroup(string name, IReadOnlyList<Contact> members, string? groupKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(members);

        var key = groupKey ?? NewGroupKey();

        var conversation = new Conversation
        {
            PeerId = null,
            IsGroup = true,
            ChannelId = ChannelId.ForGroup(key),
        };
        _conversations.Insert(conversation);

        var group = new Group
        {
            Name = name,
            GroupKey = key,
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

    /// <summary>Adds a member and creates an unestablished pairwise key placeholder.</summary>
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

        EnsureMemberKey(groupId, contact.Id);
        return member;
    }

    public void RemoveMember(int groupId, int contactId)
    {
        // TODO(forward-secrecy): removing a member only deletes their local
        // pairwise key. A departed member retains valid pairwise keys with the
        // other members and can still decrypt future copies encrypted to them.
        // Group key rotation on member leave is OUT OF SCOPE for this pass.
        _members.Remove(groupId, contactId);
        _memberKeys.DeleteForGroupAndContact(groupId, contactId);
    }

    /// <summary>
    /// Returns (or creates) the pairwise key placeholder shared with
    /// <paramref name="contact"/> inside <paramref name="groupId"/>. The returned
    /// key is <b>unestablished</b> (empty ratchet states) until the first send or
    /// prekey receive; no session key is derived or persisted.
    /// </summary>
    public GroupMemberKey EstablishMemberKey(int groupId, Contact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);

        var existing = _memberKeys.FindByGroupAndContact(groupId, contact.Id);
        if (existing is not null)
            return existing;

        var key = new GroupMemberKey
        {
            GroupId = groupId,
            MemberContactId = contact.Id,
        };
        _memberKeys.Insert(key);
        return key;
    }

    public GroupMemberKey? GetMemberKey(int groupId, int contactId) =>
        _memberKeys.FindByGroupAndContact(groupId, contactId);

    /// <summary>
    /// True when every member's pairwise session is established (each member
    /// holds an established <see cref="GroupMemberKey"/>). A group with no
    /// members is not established. Mirrors <see cref="ChatService.IsEstablished"/>
    /// so the UI can honestly switch from an "establishing session" state to
    /// "connected" for a group.
    /// </summary>
    public bool IsEstablished(int groupId)
    {
        var members = _members.ForGroup(groupId);
        if (members.Count == 0)
            return false;

        foreach (var member in members)
        {
            if (_memberKeys.FindByGroupAndContact(groupId, member.ContactId)?.IsEstablished != true)
                return false;
        }

        return true;
    }

    // ------------------------------------------------------------------
    // Sending
    // ------------------------------------------------------------------

    /// <summary>
    /// Encrypts <paramref name="text"/> separately to every member of
    /// <paramref name="groupId"/> (each under that member's own pairwise key),
    /// stores the outgoing message (plaintext) + per-member ciphertexts, and
    /// sends a single <see cref="GroupWireMessage"/>. The wire sequence number is
    /// the sender's per-sender sequence; unestablished members are established
    /// with one shared one-time EK and the frame is marked prekey.
    /// </summary>
    public async Task<Message> SendGroupMessage(int groupId, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var group = _groups.Get(groupId) ?? throw new KeyNotFoundException($"Group {groupId} not found.");
        var conversationId = ConversationIdOf(group);
        var conversation = _conversations.Get(conversationId)
            ?? throw new InvalidOperationException($"Group {groupId} has no conversation.");
        var channelId = conversation.ChannelId;

        var members = _members.ForGroup(groupId);
        if (members.Count == 0)
            throw new InvalidOperationException("The group has no members.");

        var senderSeq = _messages.MaxSenderSeq(conversationId, senderId: 0) + 1;
        var localSeq = _messages.MaxSeq(conversationId) + 1;
        var context = MessageContext.Build(channelId, senderSeq);
        var plaintextBytes = Encoding.UTF8.GetBytes(text);

        // Determine up-front whether any member's pairwise session is unestablished
        // (=> this frame is a prekey message). One one-time EK is generated and
        // reused across every member established by this broadcast.
        var memberKeys = members.ToDictionary(m => m.ContactId, m => EnsureMemberKey(groupId, m.ContactId));
        var isPrekey = memberKeys.Values.Any(k => !k.IsEstablished);
        byte[]? ekPrivate = null;
        byte[]? ekPublic = null;
        if (isPrekey)
        {
            var kp = KeyGeneration.GenerateX25519KeyPair();
            ekPrivate = kp.PrivateKey;
            ekPublic = kp.PublicKey;
        }

        var recipients = new List<GroupWireRecipient>();

        foreach (var member in members)
        {
            var key = memberKeys[member.ContactId];

            if (!key.IsEstablished)
            {
                var contact = _contacts.Get(member.ContactId)
                    ?? throw new KeyNotFoundException($"Contact {member.ContactId} not found.");
                if (contact.X25519Pub.Length != IdentityKeyPair.X25519PublicKeyBytes)
                    throw new InvalidOperationException($"Contact '{contact.Nickname}' has no X25519 identity key.");
                if (contact.EphemeralX25519Pub.Length != IdentityKeyPair.X25519PublicKeyBytes)
                    throw new InvalidOperationException($"Contact '{contact.Nickname}' has no signed prekey; the pairwise session cannot be established.");

                var (send, recv) = CryptoSession.EstablishInitiatorSessionWithEk(
                    _localIdentity, ekPrivate!, contact.X25519Pub, contact.EphemeralX25519Pub, CryptoSession.GroupInfo);
                key.SendRatchetState = send;
                key.RecvRatchetState = recv;
                _memberKeys.Update(key);
            }

            var session = new CryptoSession(
                key.SendRatchetState,
                key.RecvRatchetState,
                _localIdentity.SigningPublicKey,
                _localIdentity.SigningPrivateKey);

            var envelope = session.Seal(plaintextBytes, context);

            // Commit the send ratchet at seal time (exactly one key consumed, once).
            key.SendRatchetState = session.SendRatchetState;
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
                Seq = localSeq,
                SenderSeq = senderSeq,
                SenderPublic = _localIdentity.SigningPublicKey,
                IsPrekey = isPrekey,
                SenderEkPublic = ekPublic ?? Array.Empty<byte>(),
                SenderIkPublic = isPrekey ? _localIdentity.DhPublicKey : Array.Empty<byte>(),
                RecipientContactId = member.ContactId,
                Nonce = envelope.Nonce,
                Ciphertext = envelope.Ciphertext,
                Tag = envelope.Tag,
                Signature = envelope.Signature,
            });
        }

        // The one-time EK private key is never persisted; zero it now that every
        // unestablished member has been established.
        if (ekPrivate is not null)
            CryptographicOperations.ZeroMemory(ekPrivate);

        var message = new Message
        {
            ConversationId = conversationId,
            Seq = localSeq,
            SenderSeq = senderSeq,
            SenderId = 0, // 0 == self
            Plaintext = text,
            Timestamp = DateTime.UtcNow,
            Status = MessageStatus.Pending,
        };
        _messages.Insert(message);

        _pending.Insert(new PendingMessage
        {
            ConversationId = conversationId,
            SenderSeq = senderSeq,
            IsPrekey = isPrekey,
            SenderEkPublic = ekPublic ?? Array.Empty<byte>(),
            SenderIkPublic = isPrekey ? _localIdentity.DhPublicKey : Array.Empty<byte>(),
        });

        var wire = new GroupWireMessage
        {
            GroupKey = group.GroupKey,
            Seq = senderSeq,
            SenderPublic = _localIdentity.SigningPublicKey,
            Recipients = recipients,
            FrameType = isPrekey ? FrameType.Prekey : FrameType.Message,
            SenderEkPublic = isPrekey ? ekPublic : null,
            SenderIkPublic = isPrekey ? _localIdentity.DhPublicKey : null,
        };

        try
        {
            await _transport.SendAsync(wire.Serialize());
        }
        catch
        {
            message.Status = MessageStatus.Failed;
            _messages.Update(message);
            return message;
        }

        message.Status = MessageStatus.Delivered;
        _messages.Update(message);
        _pending.Delete(conversationId, senderSeq);
        return message;
    }

    /// <summary>Returns outgoing group messages that are Pending or Failed.</summary>
    public IReadOnlyList<Message> ListPending(int groupId)
    {
        var group = _groups.Get(groupId) ?? throw new KeyNotFoundException($"Group {groupId} not found.");
        var conversationId = ConversationIdOf(group);
        var result = new List<Message>();
        foreach (var m in _messages.ForConversation(conversationId))
        {
            if (m.SenderId == 0 && m.Status is MessageStatus.Pending or MessageStatus.Failed)
                result.Add(m);
        }
        result.Sort((a, b) => a.SenderSeq.CompareTo(b.SenderSeq));
        return result;
    }

    /// <summary>
    /// Re-sends the stored per-member ciphertexts of every pending/failed group
    /// message (in sequence order) as the <b>identical</b> frame. No member's
    /// send ratchet advances again (each key was consumed once at seal time).
    /// Returns the number of messages delivered.
    /// </summary>
    public async Task<int> RetryPending(int groupId)
    {
        var group = _groups.Get(groupId) ?? throw new KeyNotFoundException($"Group {groupId} not found.");
        var conversationId = ConversationIdOf(group);

        var delivered = 0;
        foreach (var message in ListPending(groupId))
        {
            var envelopes = _groupEnvelopes.ForSeq(conversationId, message.Seq);
            if (envelopes.Count == 0)
                continue;

            var pending = _pending.Find(conversationId, message.SenderSeq);
            var wire = new GroupWireMessage
            {
                GroupKey = group.GroupKey,
                Seq = message.SenderSeq,
                SenderPublic = _localIdentity.SigningPublicKey,
                Recipients = envelopes.Select(e => new GroupWireRecipient
                {
                    RecipientContactId = e.RecipientContactId,
                    Nonce = e.Nonce,
                    Ciphertext = e.Ciphertext,
                    Tag = e.Tag,
                    Signature = e.Signature,
                }).ToList(),
            };

            if (pending is not null && pending.IsPrekey)
            {
                wire.FrameType = FrameType.Prekey;
                wire.SenderEkPublic = pending.SenderEkPublic;
                wire.SenderIkPublic = pending.SenderIkPublic;
            }

            try
            {
                await _transport.SendAsync(wire.Serialize());
            }
            catch
            {
                message.Status = MessageStatus.Failed;
                _messages.Update(message);
                continue;
            }

            message.Status = MessageStatus.Delivered;
            _messages.Update(message);
            _pending.Delete(conversationId, message.SenderSeq);
            delivered++;
        }

        return delivered;
    }

    // ------------------------------------------------------------------
    // Receiving
    // ------------------------------------------------------------------

    /// <summary>
    /// Invoked by <see cref="ITransport.MessageReceived"/>. Authenticates the
    /// sender, (on a prekey frame) establishes the pairwise session as the
    /// responder, decrypts ONLY this member's own copy, stores the plaintext and
    /// all per-member ciphertexts (for store-and-forward relay), and raises
    /// <see cref="MessageDelivered"/>. Tampered, replayed, or foreign copies are
    /// dropped.
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
        var conversation = _conversations.Get(conversationId);
        if (conversation is null)
            return;
        var channelId = conversation.ChannelId;

        var senderSeq = wire.Seq;

        // Durable duplicate check before any ratchet mutation.
        if (_messages.ExistsBySenderSeq(conversationId, sender.Id, senderSeq))
            return;

        var recv = GetReceiveState(conversation, sender.Id);
        if (senderSeq <= recv.LastDeliveredSeq || recv.Delivered.Contains(senderSeq))
            return;

        var context = MessageContext.Build(channelId, senderSeq);
        var candidates = wire.Recipients.Select(r => r.ToEnvelope(wire.SenderPublic)).ToList();
        var plaintext = TryDeliver(
            group, conversationId, senderSeq, sender, candidates, context, recv.SkippedKeys,
            wire.IsPrekey, wire.SenderEkPublic, wire.SenderIkPublic);
        if (plaintext is null)
            return; // no copy for us, or tampered

        var localSeq = _messages.MaxSeq(conversationId) + 1;
        _messages.Insert(new Message
        {
            ConversationId = conversationId,
            Seq = localSeq,
            SenderSeq = senderSeq,
            SenderId = sender.Id,
            Plaintext = plaintext,
            Timestamp = DateTime.UtcNow,
            Status = MessageStatus.Received,
        });

        foreach (var recipient in wire.Recipients)
        {
            if (_groupEnvelopes.Exists(conversationId, localSeq, recipient.RecipientContactId))
                continue;

            _groupEnvelopes.Insert(new GroupMessageEnvelope
            {
                ConversationId = conversationId,
                Seq = localSeq,
                SenderSeq = senderSeq,
                SenderPublic = wire.SenderPublic,
                IsPrekey = wire.IsPrekey,
                SenderEkPublic = wire.SenderEkPublic ?? Array.Empty<byte>(),
                SenderIkPublic = wire.SenderIkPublic ?? Array.Empty<byte>(),
                RecipientContactId = recipient.RecipientContactId,
                Nonce = recipient.Nonce,
                Ciphertext = recipient.Ciphertext,
                Tag = recipient.Tag,
                Signature = recipient.Signature,
            });
        }

        PersistSkippedKeys(channelId, sender.Id, recv.SkippedKeys);
        AdvanceContiguous(recv, senderSeq);

        MessageDelivered?.Invoke(new IncomingMessage(
            conversationId,
            sender.Id,
            senderSeq,
            plaintext,
            DateTime.UtcNow));
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
    /// of each newly-applied message (in sequence order), storing its plaintext.
    /// Returns the highest local sequence number that was newly applied.
    /// </summary>
    public long ApplySyncPayload(int groupId, SyncPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var group = _groups.Get(groupId) ?? throw new KeyNotFoundException($"Group {groupId} not found.");
        var conversationId = ConversationIdOf(group);
        var conversation = _conversations.Get(conversationId);
        if (conversation is null)
            return 0;
        var channelId = conversation.ChannelId;

        var newSeqs = new List<long>();
        foreach (var src in payload.Messages)
        {
            if (src.Seq <= 0 || _messages.Exists(conversationId, src.Seq))
                continue;

            _messages.Insert(new Message
            {
                ConversationId = conversationId,
                Seq = src.Seq,
                SenderSeq = src.SenderSeq,
                SenderId = src.SenderId,
                Plaintext = "",
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
                SenderSeq = e.SenderSeq,
                SenderPublic = e.SenderPublic,
                IsPrekey = e.IsPrekey,
                SenderEkPublic = e.SenderEkPublic,
                SenderIkPublic = e.SenderIkPublic,
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

            var first = envelopes[0];
            var senderSeq = first.SenderSeq;
            var candidates = envelopes
                .Select(e => new MessageEnvelope(e.SenderPublic, e.Nonce, e.Ciphertext, e.Tag, e.Signature))
                .ToList();

            var recv = GetReceiveState(conversation, sender.Id);
            var context = MessageContext.Build(channelId, senderSeq);
            var plaintext = TryDeliver(
                group, conversationId, senderSeq, sender, candidates, context, recv.SkippedKeys,
                first.IsPrekey, first.SenderEkPublic, first.SenderIkPublic);
            if (plaintext is null)
                continue;

            var stored = _messages.ForConversation(conversationId).FirstOrDefault(m => m.Seq == seq);
            if (stored is not null)
            {
                stored.Plaintext = plaintext;
                _messages.Update(stored);
            }

            PersistSkippedKeys(channelId, sender.Id, recv.SkippedKeys);
            AdvanceContiguous(recv, senderSeq);
            maxApplied = Math.Max(maxApplied, seq);

            MessageDelivered?.Invoke(new IncomingMessage(
                conversationId,
                sender.Id,
                senderSeq,
                plaintext,
                DateTime.UtcNow));
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

    private ReceiveChainState GetReceiveState(Conversation conversation, int senderId)
    {
        if (!_receive.TryGetValue((conversation.Id, senderId), out var state))
        {
            state = new ReceiveChainState();
            foreach (var skipped in _skippedKeys.ForChannelAndSender(conversation.ChannelId, senderId))
                state.SkippedKeys[skipped.SenderSeq] = (byte[])skipped.Key.Clone();
            _receive[(conversation.Id, senderId)] = state;
        }
        return state;
    }

    private void PersistSkippedKeys(byte[] channelId, int senderId, IDictionary<long, byte[]> skippedKeys)
    {
        _skippedKeys.DeleteForChannelAndSender(channelId, senderId);
        foreach (var kv in skippedKeys)
        {
            _skippedKeys.Insert(new SkippedKey
            {
                ChannelId = (byte[])channelId.Clone(),
                SenderId = senderId,
                SenderSeq = kv.Key,
                Key = (byte[])kv.Value.Clone(),
            });
        }
    }

    private static void AdvanceContiguous(ReceiveChainState state, long seq)
    {
        if (seq == state.LastDeliveredSeq + 1)
        {
            state.LastDeliveredSeq = seq;
            while (state.Delivered.Remove(state.LastDeliveredSeq + 1))
                state.LastDeliveredSeq++;
        }
        else
        {
            state.Delivered.Add(seq);
        }
    }

    /// <summary>
    /// Decrypts this member's own copy from <paramref name="candidates"/> using
    /// the pairwise receive chain shared with <paramref name="sender"/>, commits
    /// the advanced receive state, and updates sync state. Returns the decrypted
    /// plaintext, or <c>null</c> when no candidate decrypts. If the pairwise
    /// session is unestablished, it is established (transiently) from the prekey
    /// fields and only persisted once a candidate actually decrypts, so a foreign
    /// prekey frame with no copy for us cannot poison the session.
    /// </summary>
    private string? TryDeliver(
        Group group,
        int conversationId,
        long senderSeq,
        Contact sender,
        IReadOnlyList<MessageEnvelope> candidates,
        byte[] context,
        Dictionary<long, byte[]> skippedKeys,
        bool isPrekey,
        byte[]? senderEkPublic,
        byte[]? senderIkPublic)
    {
        var memberKey = _memberKeys.FindByGroupAndContact(group.Id, sender.Id);
        var hasEstablished = memberKey is not null && memberKey.IsEstablished;

        byte[] sendState;
        byte[] recvState;
        if (hasEstablished)
        {
            // Already established: decrypt normally. A wire-level IsPrekey only
            // means *some* member needed a prekey; our own copy is sealed with our
            // established session, so the prekey fields are ignored.
            sendState = memberKey!.SendRatchetState;
            recvState = memberKey.RecvRatchetState;
        }
        else
        {
            if (!isPrekey || senderEkPublic is null || senderIkPublic is null)
                return null; // no session and not a prekey → cannot decrypt
            if (!sender.X25519Pub.AsSpan().SequenceEqual(senderIkPublic))
                return null; // identity-key mismatch

            (sendState, recvState) = CryptoSession.EstablishResponderSession(
                _localIdentity, senderIkPublic, senderEkPublic, CryptoSession.GroupInfo);
        }

        foreach (var candidate in candidates)
        {
            var session = new CryptoSession(
                sendState,
                recvState,
                _localIdentity.SigningPublicKey,
                _localIdentity.SigningPrivateKey);

            byte[] plaintext;
            try
            {
                plaintext = session.Open(candidate, context, senderSeq, skippedKeys);
            }
            catch (CryptographicException)
            {
                continue; // not our copy (or tampered): try the next candidate
            }

            // A candidate decrypted: persist the (possibly newly-established)
            // pairwise session and its advanced receive chain.
            if (memberKey is null)
            {
                memberKey = new GroupMemberKey
                {
                    GroupId = group.Id,
                    MemberContactId = sender.Id,
                    SendRatchetState = sendState,
                    RecvRatchetState = session.RecvRatchetState,
                };
                _memberKeys.Insert(memberKey);
            }
            else
            {
                memberKey.SendRatchetState = sendState;
                memberKey.RecvRatchetState = session.RecvRatchetState;
                _memberKeys.Update(memberKey);
            }

            var lastSeq = _syncStates.Get(conversationId, sender.Id)?.LastSeq ?? 0;
            _syncStates.Upsert(conversationId, sender.Id, Math.Max(lastSeq, senderSeq));

            return Encoding.UTF8.GetString(plaintext);
        }

        return null;
    }

    public void Dispose()
    {
        if (_started)
        {
            _started = false;
            _transport.MessageReceived -= HandleIncomingGroupMessage;
        }
    }

    private sealed class ReceiveChainState
    {
        public long LastDeliveredSeq;
        public HashSet<long> Delivered { get; } = new();
        public Dictionary<long, byte[]> SkippedKeys { get; } = new();
    }
}
