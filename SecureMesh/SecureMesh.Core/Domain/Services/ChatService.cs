using System.Security.Cryptography;
using System.Text;
using SecureMesh.Core.Domain.Persistence;

namespace SecureMesh.Core.Domain;

/// <summary>A decrypted message delivered to the UI layer.</summary>
public sealed record IncomingMessage(
    int ConversationId,
    int SenderId,
    long Seq,
    string Plaintext,
    DateTime Timestamp);

/// <summary>
/// Sends and receives encrypted text messages over an <see cref="ITransport"/>,
/// persisting plaintext history, the per-conversation ratchet state, and the
/// outbox of sealed-but-undelivered envelopes. The 1:1 session is established
/// lazily: the <b>deterministic initiator</b> (the peer with the smaller sorted
/// X25519 identity key — the same ordering as <see cref="ChannelId.ForOneToOne"/>)
/// is the only party that ever sends a prekey; the responder derives its session
/// from the initiator's prekey. On <see cref="ITransport.PeerConnected"/> each
/// side announces its Ed25519 identity with a <see cref="FrameType.Hello"/> frame,
/// which lets the initiator auto-initiate on connect. The initiator's prekey stays
/// in the outbox (<see cref="MessageStatus.Pending"/>) and is re-sent identically
/// until the responder ACKs it: the responder's first real message is an implicit
/// ACK, and a <b>bare</b> auto-initiated prekey is answered by an explicit
/// <see cref="FrameType.Ack"/> system frame so the initiator's
/// <see cref="SessionEstablished"/> fires even when the responder has nothing to
/// say. A responder message sent before the prekey arrives is queued unsealed and
/// sealed+send once the responder session is established. The root session key is
/// never persisted — only the direction-bound ratchet states and (bounded) skipped
/// keys are.
/// </summary>
public sealed class ChatService : IDisposable
{
    private readonly IdentityKeyPair _localIdentity;
    private readonly ITransport _transport;
    private readonly ContactRepository _contacts;
    private readonly ConversationRepository _conversations;
    private readonly MessageRepository _messages;
    private readonly SyncStateRepository _syncStates;
    private readonly SkippedKeyRepository _skippedKeys;
    private readonly PendingMessageRepository _pending;

    private readonly Dictionary<(int ConversationId, int SenderId), ReceiveChainState> _receive = new();
    private readonly HashSet<int> _connectedContacts = new();
    private bool _started;

    /// <summary>
    /// Serializes every read-modify-write of a conversation's send-ratchet state
    /// (seal + commit). Because the seal step consumes "the NEXT send-ratchet key"
    /// with no other uniqueness guard, and every send path re-reads the fresh
    /// conversation from the repository <i>inside</i> this gate, no two code paths
    /// can seal from the same stale state across an <c>await</c>. A single service
    /// is single-peer (one connected transport), so a service-wide gate is enough;
    /// the gate is held only for the synchronous seal+commit and never across the
    /// transport <c>await</c>, so it cannot deadlock or block unrelated work.
    /// </summary>
    private readonly SemaphoreSlim _sealGate = new(1, 1);

    /// <summary>Raised when an incoming message has been decrypted and stored.</summary>
    public event Action<IncomingMessage>? MessageDelivered;

    /// <summary>
    /// Raised (with the conversation id) when the 1:1 session becomes fully
    /// usable in both directions: for the responder when the initiator's prekey
    /// is received, and for the initiator when the responder's first message
    /// (implicit ACK) or explicit <see cref="FrameType.Ack"/> acknowledges the
    /// prekey. The UI can use this to switch from an "establishing session" state
    /// to "connected".
    /// </summary>
    public event Action<int>? SessionEstablished;

    public ChatService(IdentityKeyPair localIdentity, ITransport transport, Database db)
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
        _skippedKeys = new SkippedKeyRepository(db.Connection);
        _pending = new PendingMessageRepository(db.Connection);
    }

    public Task StartAsync()
    {
        if (_started)
            return Task.CompletedTask;

        _started = true;
        _transport.MessageReceived += HandleIncoming;
        _transport.PeerConnected += OnPeerConnected;
        _transport.PeerDisconnected += OnPeerDisconnected;
        return _transport.StartAsync();
    }

    public Task StopAsync()
    {
        if (!_started)
            return Task.CompletedTask;

        _started = false;
        _transport.MessageReceived -= HandleIncoming;
        _transport.PeerConnected -= OnPeerConnected;
        _transport.PeerDisconnected -= OnPeerDisconnected;
        return _transport.StopAsync();
    }

    /// <summary>
    /// True when this identity is the deterministic initiator for
    /// <paramref name="peer"/>: the peer with the smaller sorted X25519 identity
    /// key initiates, and only that peer ever sends a prekey.
    /// </summary>
    public bool IsInitiator(Contact peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        return ChannelId.CompareKeys(_localIdentity.DhPublicKey, peer.X25519Pub) < 0;
    }

    /// <summary>True when the conversation's session (both ratchet chains) is established.</summary>
    public bool IsEstablished(int conversationId) =>
        _conversations.Get(conversationId)?.IsEstablished ?? false;

    /// <summary>True when a <see cref="FrameType.Hello"/> has been received from the contact.</summary>
    public bool IsContactConnected(int contactId) => _connectedContacts.Contains(contactId);

    /// <summary>
    /// Returns the existing 1:1 conversation with <paramref name="peer"/>, or
    /// creates an <b>unestablished</b> one (stable channel id, empty ratchet
    /// states). No session key is derived here: the session is established
    /// lazily on first send or first prekey receive.
    /// </summary>
    public Conversation EnsureConversation(Contact peer)
    {
        ArgumentNullException.ThrowIfNull(peer);

        var existing = _conversations.FindByPeerId(peer.Id);
        if (existing is not null)
            return existing;

        var conversation = new Conversation
        {
            PeerId = peer.Id,
            IsGroup = false,
            ChannelId = ChannelId.ForOneToOne(_localIdentity.DhPublicKey, peer.X25519Pub),
        };
        _conversations.Insert(conversation);
        return conversation;
    }

    /// <summary>Finds an established 1:1 conversation with a peer, if any.</summary>
    public Conversation? ConversationWithPeer(int peerContactId) =>
        _conversations.FindByPeerId(peerContactId);

    /// <summary>
    /// Encrypts and sends <paramref name="text"/> in the given conversation,
    /// storing the outgoing message (plaintext) with a Pending/Delivered/Failed
    /// status. On the initiator's first send the session is established and the
    /// frame is a prekey message (kept Pending until the responder's implicit or
    /// explicit ACK); on the responder side, a send before the initiator's prekey
    /// arrives is queued unsealed and sealed+send once the responder session is
    /// established. A non-prekey send is queued (not sent) while the initiator's
    /// own prekey is still un-ACKed, so a failed bare-prekey send can never cause a
    /// later non-prekey message to be dropped by an unestablished responder while
    /// marked Delivered. The send ratchet advances exactly once, at seal time; a
    /// retry re-sends the identical envelope.
    /// </summary>
    public async Task<Message> SendText(int conversationId, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var initial = _conversations.Get(conversationId)
            ?? throw new KeyNotFoundException($"Conversation {conversationId} not found.");
        if (initial.IsGroup)
            throw new NotSupportedException("Group messaging is not implemented for this transport.");

        // Phase 1 (under the seal gate): decide the path and, when it is a seal,
        // read the fresh conversation, seal exactly one ratchet key, and commit.
        OutgoingPlan plan;
        await _sealGate.WaitAsync();
        try
        {
            var conversation = _conversations.Get(conversationId)
                ?? throw new KeyNotFoundException($"Conversation {conversationId} not found.");

            if (!conversation.IsEstablished)
            {
                var peer = GetPeer(conversation);
                if (IsInitiator(peer))
                {
                    var (send, recv, ek) = EstablishAsInitiator(conversation);
                    conversation.SendRatchetState = send;
                    conversation.RecvRatchetState = recv;
                    _conversations.Update(conversation);
                    plan = SealLocked(conversation, text, OutgoingKind.Prekey, ek, _localIdentity.DhPublicKey);
                }
                else
                {
                    // Responder: no session yet, so queue unsealed. It is sealed and
                    // sent once the initiator's prekey establishes the session.
                    plan = QueueUnsealedLocked(conversation, text);
                }
            }
            else if (_pending.ForConversation(conversationId).Any(p => p.IsPrekey && p.IsBarePrekey))
            {
                // A bare (auto-initiated) prekey is still un-ACKed, so the peer may
                // have no session yet and would drop a non-prekey frame. Queue until
                // the bare prekey is ACKed. (A real prekey already carries its first
                // message and is sent immediately, so non-prekey sends after it are
                // safe — the responder establishes as soon as the prekey arrives.)
                plan = QueueUnsealedLocked(conversation, text);
            }
            else
            {
                plan = SealLocked(conversation, text, OutgoingKind.Message);
            }
        }
        finally
        {
            _sealGate.Release();
        }

        if (plan.Wire is null)
            return plan.Message; // queued unsealed

        return await SendSealedAsync(plan);
    }

    /// <summary>
    /// Returns the outgoing messages in <paramref name="conversationId"/> that
    /// have not yet been delivered (Pending or Failed), in sequence order.
    /// </summary>
    public IReadOnlyList<Message> ListPending(int conversationId)
    {
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
    /// Returns the readable message history of <paramref name="conversationId"/> in
    /// sequence order. Plaintext is read directly from <see cref="Message.Plaintext"/>
    /// (already decrypted at receive/send time). System markers — the initiator's
    /// bare auto-initiated prekey and explicit ACK rows, both of which carry an
    /// empty plaintext — and any other empty-plaintext row are excluded, so the
    /// internal bare-prekey marker never surfaces to the UI.
    /// </summary>
    public IReadOnlyList<Message> GetHistory(int conversationId)
    {
        var result = new List<Message>();
        foreach (var message in _messages.ForConversation(conversationId))
        {
            if (message.Status == MessageStatus.System || string.IsNullOrEmpty(message.Plaintext))
                continue;
            result.Add(message);
        }
        return result;
    }

    /// <summary>
    /// Re-sends the stored <b>identical</b> envelope of every sealed-but-undelivered
    /// message in <paramref name="conversationId"/> (in sequence order). The ratchet
    /// is NOT advanced again (each key was already consumed once at seal time). A
    /// prekey or explicit ACK stays Pending until acknowledged; non-prekey messages
    /// transition to Delivered. Returns the number of frames handed to the transport.
    /// This is the manual/test retry seam; the automatic retry path is
    /// connect/Hello-driven (see <see cref="HandleHello"/>).
    /// </summary>
    public async Task<int> RetryPending(int conversationId)
    {
        if (_conversations.Get(conversationId) is null)
            throw new KeyNotFoundException($"Conversation {conversationId} not found.");
        return await ResendSealedAsync(conversationId);
    }

    /// <summary>
    /// Invoked by <see cref="ITransport.MessageReceived"/>. Routes
    /// <see cref="FrameType.Hello"/> frames to <see cref="HandleHello"/> and
    /// otherwise authenticates the sender, (on a prekey message) establishes the
    /// session as the responder, decrypts with the receive ratchet, stores the
    /// plaintext, and raises <see cref="MessageDelivered"/>. A bare prekey is
    /// answered with an explicit <see cref="FrameType.Ack"/>; an incoming ACK
    /// clears the initiator's pending prekey and raises
    /// <see cref="SessionEstablished"/>. Replayed/out-of-window frames are
    /// rejected before decryption; malformed or unauthentic payloads are silently
    /// dropped.
    /// </summary>
    public void HandleIncoming(byte[] rawPayload)
    {
        ArgumentNullException.ThrowIfNull(rawPayload);

        WireMessage wire;
        try
        {
            wire = WireMessage.Deserialize(rawPayload);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or ArgumentException)
        {
            return;
        }

        if (wire.IsHello)
        {
            HandleHello(wire);
            return;
        }

        var sender = _contacts.FindByEd25519Pub(wire.SenderPublic);
        if (sender is null)
            return; // unknown sender

        var conversation = _conversations.FindByPeerId(sender.Id);
        if (conversation is null)
        {
            if (!wire.IsPrekey)
                return; // non-prekey frame for a conversation we never created
            if (wire.SenderIkPublic is null || wire.SenderEkPublic is null)
                return;
            if (!sender.X25519Pub.AsSpan().SequenceEqual(wire.SenderIkPublic))
                return; // identity-key mismatch

            conversation = new Conversation
            {
                PeerId = sender.Id,
                IsGroup = false,
                ChannelId = ChannelId.ForOneToOne(_localIdentity.DhPublicKey, wire.SenderIkPublic),
            };
            _conversations.Insert(conversation);
        }

        // Durable duplicate check before any ratchet mutation.
        if (_messages.ExistsBySenderSeq(conversation.Id, sender.Id, wire.Seq))
        {
            // A re-sent bare prekey means our previous ACK may have been lost and the
            // initiator never saw it; re-send the identical ACK (and any other pending
            // frames) without advancing the ratchet again.
            if (wire.IsPrekey && wire.IsBarePrekey)
                _ = ResendSealedAsync(conversation.Id);
            return;
        }

        var wasEstablished = conversation.IsEstablished;

        // Determine the ratchet states to decrypt with. If the session is
        // unestablished, derive them transiently from the prekey fields and only
        // persist once a frame actually decrypts — so a tampered/foreign prekey
        // frame cannot poison the session.
        byte[] sendState;
        byte[] recvState;
        if (conversation.IsEstablished)
        {
            sendState = conversation.SendRatchetState;
            recvState = conversation.RecvRatchetState;
        }
        else
        {
            if (!wire.IsPrekey)
                return; // non-prekey frame for an unestablished conversation
            if (wire.SenderIkPublic is null || wire.SenderEkPublic is null)
                return;
            if (!sender.X25519Pub.AsSpan().SequenceEqual(wire.SenderIkPublic))
                return; // identity-key mismatch

            (sendState, recvState) = CryptoSession.EstablishResponderSession(
                _localIdentity, wire.SenderIkPublic, wire.SenderEkPublic, CryptoSession.DefaultInfo);
        }

        var recv = GetReceiveState(conversation, sender.Id);

        // In-memory replay/ordering fast path (the durable check above is authoritative).
        if (wire.Seq <= recv.LastDeliveredSeq || recv.Delivered.Contains(wire.Seq))
            return;

        var context = MessageContext.Build(conversation.ChannelId, wire.Seq);
        var session = new CryptoSession(
            sendState,
            recvState,
            _localIdentity.SigningPublicKey,
            _localIdentity.SigningPrivateKey);

        byte[] plaintext;
        try
        {
            plaintext = session.Open(wire.ToEnvelope(), context, wire.Seq, recv.SkippedKeys);
        }
        catch (CryptographicException)
        {
            return; // failed authentication/decryption; drop, nothing committed
        }

        // Decryption succeeded: commit the (possibly newly-established) session,
        // persist skipped keys and plaintext.
        conversation.SendRatchetState = sendState;
        conversation.RecvRatchetState = session.RecvRatchetState;
        _conversations.Update(conversation);
        PersistSkippedKeys(conversation.ChannelId, sender.Id, recv.SkippedKeys);

        var isBarePrekey = wire.IsBarePrekey;
        var isAck = wire.IsAck;

        if (isBarePrekey || isAck)
        {
            // A bare prekey establishes the session without a user message; an
            // explicit ACK advances the receive chain without a user message either.
            AdvanceContiguous(recv, wire.Seq);
        }
        else
        {
            var localSeq = _messages.MaxSeq(conversation.Id) + 1;
            var text = Encoding.UTF8.GetString(plaintext);
            _messages.Insert(new Message
            {
                ConversationId = conversation.Id,
                Seq = localSeq,
                SenderSeq = wire.Seq,
                SenderId = sender.Id,
                Plaintext = text,
                Timestamp = DateTime.UtcNow,
                Status = MessageStatus.Received,
            });

            AdvanceContiguous(recv, wire.Seq);

            var lastSeq = _syncStates.Get(conversation.Id, sender.Id)?.LastSeq ?? 0;
            _syncStates.Upsert(conversation.Id, sender.Id, Math.Max(lastSeq, wire.Seq));

            MessageDelivered?.Invoke(new IncomingMessage(
                conversation.Id,
                sender.Id,
                wire.Seq,
                text,
                DateTime.UtcNow));
        }

        if (!wasEstablished)
            SessionEstablished?.Invoke(conversation.Id);

        // Any message from the peer implicitly ACKs our pending prekey; an explicit
        // ACK does too (AcknowledgePrekeys handles both by clearing the outbox).
        AcknowledgePrekeys(conversation.Id);

        // A responder answers a bare prekey with an explicit ACK so the initiator
        // can clear its prekey and fire SessionEstablished even when there is no
        // real message to send.
        if (isBarePrekey)
            _ = SendAckAsync(conversation.Id);

        // Now that the session is established, seal and send any queued (unsealed)
        // messages this side has been holding.
        _ = FlushQueuedAsync(conversation.Id);
    }

    private (byte[] Send, byte[] Recv, byte[] EkPublic) EstablishAsInitiator(Conversation conversation)
    {
        var peer = GetPeer(conversation);
        if (peer.X25519Pub.Length != IdentityKeyPair.X25519PublicKeyBytes)
            throw new InvalidOperationException($"Contact '{peer.Nickname}' has no X25519 identity key.");
        if (peer.EphemeralX25519Pub.Length != IdentityKeyPair.X25519PublicKeyBytes)
            throw new InvalidOperationException($"Contact '{peer.Nickname}' has no signed prekey; the session cannot be established yet.");

        return CryptoSession.EstablishInitiatorSession(
            _localIdentity, peer.X25519Pub, peer.EphemeralX25519Pub, CryptoSession.DefaultInfo);
    }

    private Contact GetPeer(Conversation conversation)
    {
        if (conversation.PeerId is not int peerId)
            throw new InvalidOperationException("A 1:1 conversation must have a peer.");

        return _contacts.Get(peerId)
            ?? throw new InvalidOperationException("The peer contact no longer exists.");
    }

    /// <summary>
    /// The next per-sender sequence number for outgoing messages. This is the max
    /// sequence number already stored (which includes the System marker row of an
    /// auto-initiated bare prekey, so its consumed sequence is never reused), plus
    /// any sealed outbox entries not yet represented by a <see cref="Message"/> row.
    /// Must be called while holding <see cref="_sealGate"/>.
    /// </summary>
    private long NextSenderSeq(int conversationId)
    {
        var max = _messages.MaxSenderSeq(conversationId, senderId: 0);
        foreach (var pending in _pending.ForConversation(conversationId))
        {
            if (pending.SenderSeq > max)
                max = pending.SenderSeq;
        }
        return max + 1;
    }

    /// <summary>The kind of a just-sealed outgoing frame.</summary>
    private enum OutgoingKind
    {
        /// <summary>A normal (non-prekey, non-ACK) encrypted message.</summary>
        Message,
        /// <summary>A first (real) prekey message carrying a user message.</summary>
        Prekey,
        /// <summary>A bare auto-initiated prekey (empty plaintext, no user message).</summary>
        BarePrekey,
        /// <summary>An explicit prekey acknowledgement system frame.</summary>
        Ack,
    }

    /// <summary>The result of sealing (or queueing) an outgoing message.</summary>
    private sealed record OutgoingPlan(Message Message, WireMessage? Wire, long SenderSeq, OutgoingKind Kind);

    /// <summary>
    /// Seals <paramref name="text"/> with the NEXT send-ratchet key and commits the
    /// advanced state plus the message/outbox rows. The caller must hold
    /// <see cref="_sealGate"/> and pass a fresh, established conversation.
    /// </summary>
    private OutgoingPlan SealLocked(
        Conversation conversation,
        string text,
        OutgoingKind kind,
        byte[]? senderEkPublic = null,
        byte[]? senderIkPublic = null)
    {
        var isPrekey = kind is OutgoingKind.Prekey or OutgoingKind.BarePrekey;
        var isBare = kind == OutgoingKind.BarePrekey;
        var isAck = kind == OutgoingKind.Ack;

        var senderSeq = NextSenderSeq(conversation.Id);
        var plaintext = isAck || isBare ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(text);
        var context = MessageContext.Build(conversation.ChannelId, senderSeq);

        var session = new CryptoSession(
            conversation.SendRatchetState,
            conversation.RecvRatchetState,
            _localIdentity.SigningPublicKey,
            _localIdentity.SigningPrivateKey);

        var envelope = session.Seal(plaintext, context);

        // Commit the send ratchet at seal time: exactly one key consumed, once.
        conversation.SendRatchetState = session.SendRatchetState;
        _conversations.Update(conversation);

        var message = new Message
        {
            ConversationId = conversation.Id,
            Seq = _messages.MaxSeq(conversation.Id) + 1,
            SenderSeq = senderSeq,
            SenderId = 0, // 0 == self
            Plaintext = isAck || isBare ? "" : text,
            Timestamp = DateTime.UtcNow,
            Status = isAck || isBare ? MessageStatus.System : MessageStatus.Pending,
        };
        _messages.Insert(message);

        _pending.Insert(new PendingMessage
        {
            ConversationId = conversation.Id,
            SenderSeq = senderSeq,
            SenderPublic = envelope.SenderPublic,
            Nonce = envelope.Nonce,
            Ciphertext = envelope.Ciphertext,
            Tag = envelope.Tag,
            Signature = envelope.Signature,
            IsPrekey = isPrekey,
            IsBarePrekey = isBare,
            IsAck = isAck,
            SenderEkPublic = isPrekey ? (senderEkPublic ?? Array.Empty<byte>()) : Array.Empty<byte>(),
            SenderIkPublic = isPrekey ? (senderIkPublic ?? Array.Empty<byte>()) : Array.Empty<byte>(),
        });

        var wire = kind switch
        {
            OutgoingKind.Ack => WireMessage.FromAck(conversation.Id, senderSeq, envelope),
            OutgoingKind.Prekey or OutgoingKind.BarePrekey =>
                WireMessage.FromPrekeyEnvelope(conversation.Id, senderSeq, envelope, senderEkPublic!, senderIkPublic!, isBare),
            _ => WireMessage.FromEnvelope(conversation.Id, senderSeq, envelope),
        };

        return new OutgoingPlan(message, wire, senderSeq, kind);
    }

    /// <summary>
    /// Stores the responder's (or a gated initiator's) outgoing message unsealed —
    /// no ratchet key consumed yet. The caller must hold <see cref="_sealGate"/>.
    /// </summary>
    private OutgoingPlan QueueUnsealedLocked(Conversation conversation, string text)
    {
        var senderSeq = NextSenderSeq(conversation.Id);

        var message = new Message
        {
            ConversationId = conversation.Id,
            Seq = _messages.MaxSeq(conversation.Id) + 1,
            SenderSeq = senderSeq,
            SenderId = 0, // 0 == self
            Plaintext = text,
            Timestamp = DateTime.UtcNow,
            Status = MessageStatus.Pending,
        };
        _messages.Insert(message);

        // No PendingMessage row: this message is unsealed.
        return new OutgoingPlan(message, Wire: null, senderSeq, OutgoingKind.Message);
    }

    /// <summary>
    /// Hands an already-sealed frame to the transport and updates its status. A
    /// transport failure marks the message Failed (never Delivered); a successful
    /// non-prekey/non-ACK send marks it Delivered and removes it from the outbox,
    /// while a prekey or ACK stays Pending until acknowledged.
    /// </summary>
    private async Task<Message> SendSealedAsync(OutgoingPlan plan)
    {
        try
        {
            await _transport.SendAsync(plan.Wire!.Serialize());
        }
        catch
        {
            plan.Message.Status = MessageStatus.Failed;
            _messages.Update(plan.Message);
            return plan.Message;
        }

        if (plan.Kind == OutgoingKind.Message)
        {
            // Non-prekey messages follow the "handed to transport" delivery contract.
            plan.Message.Status = MessageStatus.Delivered;
            _messages.Update(plan.Message);
            _pending.Delete(plan.Message.ConversationId, plan.SenderSeq);
        }
        // A prekey or ACK stays Pending until the peer acknowledges it.

        return plan.Message;
    }

    /// <summary>
    /// Re-sends the stored <b>identical</b> envelopes of the conversation's outbox
    /// (in sequence order) without advancing the ratchet. Prekey and ACK frames are
    /// kept for future re-sends; non-prekey frames are marked Delivered on success.
    /// </summary>
    private async Task<int> ResendSealedAsync(int conversationId)
    {
        var sent = 0;
        foreach (var pending in _pending.ForConversation(conversationId).OrderBy(p => p.SenderSeq).ToList())
        {
            var envelope = new MessageEnvelope(
                pending.SenderPublic,
                pending.Nonce,
                pending.Ciphertext,
                pending.Tag,
                pending.Signature);

            WireMessage wire;
            if (pending.IsAck)
                wire = WireMessage.FromAck(conversationId, pending.SenderSeq, envelope);
            else if (pending.IsPrekey)
                wire = WireMessage.FromPrekeyEnvelope(conversationId, pending.SenderSeq, envelope, pending.SenderEkPublic, pending.SenderIkPublic, pending.IsBarePrekey);
            else
                wire = WireMessage.FromEnvelope(conversationId, pending.SenderSeq, envelope);

            try
            {
                await _transport.SendAsync(wire.Serialize());
            }
            catch
            {
                if (!pending.IsPrekey && !pending.IsAck)
                    SetOutgoingStatus(conversationId, pending.SenderSeq, MessageStatus.Failed);
                continue;
            }

            if (pending.IsPrekey)
            {
                // Still awaiting the responder's ACK. A real prekey (that failed
                // earlier) returns to Pending; a bare prekey keeps its System status.
                if (!pending.IsBarePrekey)
                    SetOutgoingStatus(conversationId, pending.SenderSeq, MessageStatus.Pending);
            }
            else if (pending.IsAck)
            {
                // Kept in the outbox so a lost ACK is re-sent identically if the
                // initiator re-sends its bare prekey.
            }
            else
            {
                _pending.Delete(conversationId, pending.SenderSeq);
                SetOutgoingStatus(conversationId, pending.SenderSeq, MessageStatus.Delivered);
            }
            sent++;
        }

        return sent;
    }

    /// <summary>
    /// Seals and sends any queued (unsealed) outgoing messages once the responder
    /// session is established. Each message consumes one fresh ratchet key at seal
    /// time; a send failure stores the sealed envelope for a later identical retry.
    /// The conversation is re-read from the repository inside the seal gate for
    /// every message, so a concurrent <see cref="SendText"/> can never cause the
    /// send ratchet to be read stale and a key reused.
    /// </summary>
    private async Task<int> FlushQueuedAsync(int conversationId)
    {
        try
        {
            var sent = 0;
            while (true)
            {
                OutgoingPlan? plan = null;
                Message? queued = null;

                await _sealGate.WaitAsync();
                try
                {
                    var conversation = _conversations.Get(conversationId);
                    if (conversation is null || !conversation.IsEstablished)
                        return sent;

                    foreach (var message in ListPending(conversationId))
                    {
                        if (_pending.Find(conversationId, message.SenderSeq) is null)
                        {
                            queued = message;
                            break;
                        }
                    }
                    if (queued is null)
                        return sent;

                    var senderSeq = queued.SenderSeq;
                    var context = MessageContext.Build(conversation.ChannelId, senderSeq);
                    var session = new CryptoSession(
                        conversation.SendRatchetState,
                        conversation.RecvRatchetState,
                        _localIdentity.SigningPublicKey,
                        _localIdentity.SigningPrivateKey);
                    var envelope = session.Seal(Encoding.UTF8.GetBytes(queued.Plaintext), context);
                    conversation.SendRatchetState = session.SendRatchetState;
                    _conversations.Update(conversation);

                    _pending.Insert(new PendingMessage
                    {
                        ConversationId = conversationId,
                        SenderSeq = senderSeq,
                        SenderPublic = envelope.SenderPublic,
                        Nonce = envelope.Nonce,
                        Ciphertext = envelope.Ciphertext,
                        Tag = envelope.Tag,
                        Signature = envelope.Signature,
                        IsPrekey = false,
                        IsBarePrekey = false,
                        IsAck = false,
                        SenderEkPublic = Array.Empty<byte>(),
                        SenderIkPublic = Array.Empty<byte>(),
                    });

                    var wire = WireMessage.FromEnvelope(conversationId, senderSeq, envelope);
                    plan = new OutgoingPlan(queued, wire, senderSeq, OutgoingKind.Message);
                }
                finally
                {
                    _sealGate.Release();
                }

                if (plan is null)
                    return sent;

                try
                {
                    await _transport.SendAsync(plan.Wire!.Serialize());
                }
                catch
                {
                    queued!.Status = MessageStatus.Failed;
                    _messages.Update(queued);
                    continue;
                }

                queued!.Status = MessageStatus.Delivered;
                _messages.Update(queued);
                _pending.Delete(conversationId, plan.SenderSeq);
                sent++;
            }
        }
        catch
        {
            // Flushing is best-effort and retried on the next connection.
            return 0;
        }
    }

    /// <summary>
    /// Sends an explicit prekey acknowledgement (a system <see cref="FrameType.Ack"/>
    /// frame with an empty plaintext) on the established session. The ACK stays in
    /// the outbox (marked <see cref="PendingMessage.IsAck"/>) so a lost ACK can be
    /// re-sent identically if the initiator re-sends its bare prekey.
    /// </summary>
    private async Task SendAckAsync(int conversationId)
    {
        OutgoingPlan plan;
        await _sealGate.WaitAsync();
        try
        {
            var conversation = _conversations.Get(conversationId);
            if (conversation is null || !conversation.IsEstablished)
                return;
            plan = SealLocked(conversation, "", OutgoingKind.Ack);
        }
        finally
        {
            _sealGate.Release();
        }

        try
        {
            await _transport.SendAsync(plan.Wire!.Serialize());
        }
        catch
        {
            // Stays in the outbox (IsAck); re-sent identically on reconnect or when
            // the initiator re-sends its bare prekey.
        }
    }

    /// <summary>
    /// Marks our pending prekey(s) Delivered now that the peer has replied on the
    /// established session (implicit ACK) or sent an explicit ACK, and raises
    /// <see cref="SessionEstablished"/> on the initiator side.
    /// </summary>
    private void AcknowledgePrekeys(int conversationId)
    {
        var acknowledged = false;
        foreach (var pending in _pending.ForConversation(conversationId).Where(p => p.IsPrekey).ToList())
        {
            if (!pending.IsBarePrekey)
                SetOutgoingStatus(conversationId, pending.SenderSeq, MessageStatus.Delivered);
            _pending.Delete(conversationId, pending.SenderSeq);
            acknowledged = true;
        }

        if (acknowledged)
            SessionEstablished?.Invoke(conversationId);
    }

    private void SetOutgoingStatus(int conversationId, long senderSeq, MessageStatus status)
    {
        var message = _messages.ForConversation(conversationId)
            .FirstOrDefault(m => m.SenderId == 0 && m.SenderSeq == senderSeq);
        if (message is not null && message.Status != status)
        {
            message.Status = status;
            _messages.Update(message);
        }
    }

    private void OnPeerConnected(string peerId)
    {
        // Announce our Ed25519 identity so the peer can map us to a contact and, if
        // it is the initiator, auto-initiate. Retries are driven by the peer's Hello
        // (which tells us WHICH contact is connected).
        _ = SendHelloAsync();
    }

    private void OnPeerDisconnected(string peerId)
    {
        // Single-peer transport: the connected peer is gone, so no contact is connected.
        _connectedContacts.Clear();
    }

    private async Task SendHelloAsync()
    {
        try
        {
            await _transport.SendAsync(WireMessage.FromHello(_localIdentity.SigningPublicKey).Serialize());
        }
        catch
        {
            // The peer may not be fully up yet; the Hello is re-sent on the next connect.
        }
    }

    private void HandleHello(WireMessage wire)
    {
        var contact = _contacts.FindByEd25519Pub(wire.SenderPublic);
        if (contact is null)
            return;

        _connectedContacts.Add(contact.Id);

        var conversation = _conversations.FindByPeerId(contact.Id);
        if (conversation is null)
            return;

        if (!conversation.IsEstablished)
        {
            if (IsInitiator(contact))
                _ = InitiateAsync(conversation.Id);
            // else: the responder waits for the initiator's prekey.
        }
        else
        {
            // Re-send any pending prekey/ACK/messages (lost-prekey recovery on reconnect).
            _ = ResendSealedAsync(conversation.Id);
        }
    }

    /// <summary>
    /// Initiator-side proactive establishment: seeds the ratchet states and sends a
    /// <b>bare prekey</b> — a prekey frame whose envelope seals an empty plaintext —
    /// so the responder can establish its session (and flush its queued messages)
    /// before either side has any real text to send. Establish + seal are atomic
    /// under the seal gate so a concurrent <see cref="SendText"/> cannot seal a
    /// non-prekey frame in the instant between establishment and the prekey send.
    /// </summary>
    private async Task InitiateAsync(int conversationId)
    {
        try
        {
            OutgoingPlan? plan = null;
            await _sealGate.WaitAsync();
            try
            {
                var conversation = _conversations.Get(conversationId);
                if (conversation is null || conversation.IsEstablished)
                    return;

                var peer = GetPeer(conversation);
                if (!IsInitiator(peer))
                    return;

                if (_pending.ForConversation(conversationId).Any(p => p.IsPrekey && p.IsBarePrekey))
                    return; // a bare prekey is already queued/sent

                var (send, recv, ek) = EstablishAsInitiator(conversation);
                conversation.SendRatchetState = send;
                conversation.RecvRatchetState = recv;
                _conversations.Update(conversation);

                plan = SealLocked(conversation, "", OutgoingKind.BarePrekey, ek, _localIdentity.DhPublicKey);
            }
            finally
            {
                _sealGate.Release();
            }

            if (plan is not null)
            {
                try
                {
                    await _transport.SendAsync(plan.Wire!.Serialize());
                }
                catch
                {
                    // Stays Pending in the outbox; re-sent on the next connect until ACKed.
                }
            }
        }
        catch
        {
            // Initiation can be retried on the next connect/hello.
        }
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
        // Full replace of the bounded cache for this channel + sender.
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

    public void Dispose()
    {
        if (_started)
        {
            _started = false;
            _transport.MessageReceived -= HandleIncoming;
            _transport.PeerConnected -= OnPeerConnected;
            _transport.PeerDisconnected -= OnPeerDisconnected;
        }
    }

    /// <summary>
    /// Per-sender receive-side ordering and skipped-key state. Tracks the highest
    /// contiguous sequence delivered and the message keys of not-yet-received
    /// (skipped) messages so out-of-order frames within the window still decrypt.
    /// Skipped keys are loaded from (and persisted to) the <see cref="SkippedKey"/>
    /// table so they survive an app restart.
    /// </summary>
    private sealed class ReceiveChainState
    {
        public long LastDeliveredSeq;
        public HashSet<long> Delivered { get; } = new();
        public Dictionary<long, byte[]> SkippedKeys { get; } = new();
    }
}
