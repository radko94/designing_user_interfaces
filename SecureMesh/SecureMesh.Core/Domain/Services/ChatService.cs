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
/// persisting encrypted-at-rest messages and the per-conversation ratchet state.
/// </summary>
public sealed class ChatService : IDisposable
{
    private readonly IdentityKeyPair _localIdentity;
    private readonly ITransport _transport;
    private readonly ContactRepository _contacts;
    private readonly ConversationRepository _conversations;
    private readonly MessageRepository _messages;
    private readonly SyncStateRepository _syncStates;

    private bool _started;

    /// <summary>Raised when an incoming message has been decrypted and stored.</summary>
    public event Action<IncomingMessage>? MessageDelivered;

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
    }

    public Task StartAsync()
    {
        if (_started)
            return Task.CompletedTask;

        _started = true;
        _transport.MessageReceived += HandleIncoming;
        return _transport.StartAsync();
    }

    public Task StopAsync()
    {
        if (!_started)
            return Task.CompletedTask;

        _started = false;
        _transport.MessageReceived -= HandleIncoming;
        return _transport.StopAsync();
    }

    /// <summary>
    /// Returns the existing 1:1 conversation with <paramref name="peer"/>, or
    /// establishes one by deriving the shared session key and seeding the
    /// ratchet chain.
    /// </summary>
    public Conversation EnsureConversation(Contact peer)
    {
        ArgumentNullException.ThrowIfNull(peer);

        var existing = _conversations.FindByPeerId(peer.Id);
        if (existing is not null)
            return existing;

        var sessionKey = CryptoSession.EstablishSessionKey(_localIdentity.DhPrivateKey, peer.X25519Pub);
        var conversation = new Conversation
        {
            PeerId = peer.Id,
            IsGroup = false,
            SessionKey = (byte[])sessionKey.Clone(),
            RatchetState = (byte[])sessionKey.Clone(),
        };

        _conversations.Insert(conversation);
        return conversation;
    }

    /// <summary>Finds an established 1:1 conversation with a peer, if any.</summary>
    public Conversation? ConversationWithPeer(int peerContactId) =>
        _conversations.FindByPeerId(peerContactId);

    /// <summary>
    /// Encrypts and sends <paramref name="text"/> in the given conversation,
    /// then stores the outgoing (encrypted) message.
    /// </summary>
    public async Task<Message> SendText(int conversationId, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var conversation = _conversations.Get(conversationId)
            ?? throw new KeyNotFoundException($"Conversation {conversationId} not found.");

        if (conversation.IsGroup)
            throw new NotSupportedException("Group messaging is not implemented for this transport.");
        if (conversation.SessionKey.Length == 0)
            throw new InvalidOperationException("The conversation has no session key.");

        var seq = _messages.MaxSeq(conversationId) + 1;

        var session = new CryptoSession(
            conversation.SessionKey,
            conversation.RatchetState,
            _localIdentity.SigningPublicKey,
            _localIdentity.SigningPrivateKey);

        var envelope = session.Seal(Encoding.UTF8.GetBytes(text));

        conversation.RatchetState = session.RatchetState;
        _conversations.Update(conversation);

        var message = new Message
        {
            ConversationId = conversationId,
            Seq = seq,
            SenderId = 0, // 0 == self
            Ciphertext = envelope.Ciphertext,
            Nonce = envelope.Nonce,
            Tag = envelope.Tag,
            Signature = envelope.Signature,
            Timestamp = DateTime.UtcNow,
            Status = MessageStatus.Sent,
        };
        _messages.Insert(message);

        var wire = WireMessage.FromEnvelope(conversationId, seq, envelope);
        await _transport.SendAsync(wire.Serialize());

        return message;
    }

    /// <summary>
    /// Invoked by <see cref="ITransport.MessageReceived"/>. Deserializes the
    /// frame, authenticates the sender, decrypts with the conversation's
    /// ratchet, stores the message, and raises <see cref="MessageDelivered"/>.
    /// Malformed or unauthentic payloads are silently dropped.
    /// </summary>
    public void HandleIncoming(byte[] rawPayload)
    {
        ArgumentNullException.ThrowIfNull(rawPayload);

        WireMessage wire;
        MessageEnvelope envelope;
        try
        {
            wire = WireMessage.Deserialize(rawPayload);
            envelope = wire.ToEnvelope();
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or ArgumentException)
        {
            return;
        }

        var sender = _contacts.FindByEd25519Pub(envelope.SenderPublic);
        if (sender is null)
            return; // unknown sender

        var conversation = _conversations.FindByPeerId(sender.Id);
        if (conversation is null || conversation.SessionKey.Length == 0)
            return; // no established session with this peer

        byte[] plaintext;
        try
        {
            var session = new CryptoSession(
                conversation.SessionKey,
                conversation.RatchetState,
                _localIdentity.SigningPublicKey,
                _localIdentity.SigningPrivateKey);

            plaintext = session.Open(envelope);

            conversation.RatchetState = session.RatchetState;
            _conversations.Update(conversation);
        }
        catch (CryptographicException)
        {
            return; // failed authentication/decryption; drop
        }

        if (!_messages.Exists(conversation.Id, wire.Seq))
        {
            var message = new Message
            {
                ConversationId = conversation.Id,
                Seq = wire.Seq,
                SenderId = sender.Id,
                Ciphertext = envelope.Ciphertext,
                Nonce = envelope.Nonce,
                Tag = envelope.Tag,
                Signature = envelope.Signature,
                Timestamp = DateTime.UtcNow,
                Status = MessageStatus.Received,
            };
            _messages.Insert(message);
        }

        var lastSeq = _syncStates.Get(conversation.Id, sender.Id)?.LastSeq ?? 0;
        _syncStates.Upsert(conversation.Id, sender.Id, Math.Max(lastSeq, wire.Seq));

        MessageDelivered?.Invoke(new IncomingMessage(
            conversation.Id,
            sender.Id,
            wire.Seq,
            Encoding.UTF8.GetString(plaintext),
            DateTime.UtcNow));
    }

    public void Dispose()
    {
        if (_started)
        {
            _started = false;
            _transport.MessageReceived -= HandleIncoming;
        }
    }
}
