using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecureMesh.Core.Domain;

/// <summary>Wire frame discriminator carried on every frame.</summary>
public enum FrameType
{
    /// <summary>A normal (non-prekey) encrypted message.</summary>
    Message = 0,

    /// <summary>A first message establishing the session (carries the sender's IK + one-time EK).</summary>
    Prekey = 1,

    /// <summary>An identity hello carrying only the sender's Ed25519 public key.</summary>
    Hello = 2,

    /// <summary>
    /// An explicit prekey acknowledgement: a session-bound system frame (empty
    /// plaintext, no user message) that a responder emits after establishing a
    /// session from a <b>bare</b> prekey, so the initiator can clear its pending
    /// prekey and raise <see cref="Services.ChatService.SessionEstablished"/>.
    /// </summary>
    Ack = 3,
}

/// <summary>
/// The framed wire payload sent over <see cref="ITransport"/>. It carries the
/// routing metadata (sender's local conversation id and the per-sender sequence
/// number) in the clear together with the signed, authenticated
/// <see cref="MessageEnvelope"/> fields. The plaintext is never present.
/// A first (prekey) message additionally carries the sender's one-time EK and
/// identity public keys so the recipient can establish the session; a
/// <see cref="FrameType.Hello"/> frame carries only the sender's Ed25519 public
/// key (in <see cref="SenderPublic"/>) so the recipient can map the connected
/// transport peer to a contact.
/// </summary>
public sealed class WireMessage
{
    /// <summary>
    /// The sender's local conversation id. Retained for compatibility; it is
    /// NOT cryptographically bound (the context uses the stable channel id).
    /// </summary>
    public int ConversationId { get; set; }

    /// <summary>The sender's per-sender sequence number (bound into the context).</summary>
    public long Seq { get; set; }

    public byte[] SenderPublic { get; set; } = Array.Empty<byte>();

    public byte[] Nonce { get; set; } = Array.Empty<byte>();

    public byte[] Ciphertext { get; set; } = Array.Empty<byte>();

    public byte[] Tag { get; set; } = Array.Empty<byte>();

    public byte[] Signature { get; set; } = Array.Empty<byte>();

    /// <summary>The frame kind. Omitted on the wire when it is <see cref="FrameType.Message"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public FrameType FrameType { get; set; } = FrameType.Message;

    /// <summary>True when this is a prekey (first) message establishing the session.</summary>
    [JsonIgnore]
    public bool IsPrekey => FrameType == FrameType.Prekey;

    /// <summary>True when this is an identity hello (carries only the Ed25519 public key).</summary>
    [JsonIgnore]
    public bool IsHello => FrameType == FrameType.Hello;

    /// <summary>True when this is an explicit prekey acknowledgement frame.</summary>
    [JsonIgnore]
    public bool IsAck => FrameType == FrameType.Ack;

    /// <summary>
    /// True when this is a <b>bare</b> prekey: a session-establishment artifact
    /// whose envelope seals an empty plaintext (no user message). This is an
    /// explicit wire field, not an inference from <see cref="Ciphertext"/> length,
    /// so a genuine empty-plaintext message can never be misclassified as a bare
    /// prekey. Omitted on the wire when false.
    /// </summary>
    [JsonPropertyName("IsBarePrekey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsBarePrekey { get; set; }

    /// <summary>The sender's one-time X25519 EK public key (prekey messages only).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? SenderEkPublic { get; set; }

    /// <summary>The sender's long-term X25519 identity public key (prekey messages only).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? SenderIkPublic { get; set; }

    public static WireMessage FromEnvelope(int conversationId, long seq, MessageEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return new WireMessage
        {
            ConversationId = conversationId,
            Seq = seq,
            SenderPublic = envelope.SenderPublic,
            Nonce = envelope.Nonce,
            Ciphertext = envelope.Ciphertext,
            Tag = envelope.Tag,
            Signature = envelope.Signature,
            FrameType = FrameType.Message,
        };
    }

    public static WireMessage FromPrekeyEnvelope(
        int conversationId,
        long seq,
        MessageEnvelope envelope,
        byte[] senderEkPublic,
        byte[] senderIkPublic,
        bool isBarePrekey = false)
    {
        var wire = FromEnvelope(conversationId, seq, envelope);
        wire.FrameType = FrameType.Prekey;
        wire.SenderEkPublic = senderEkPublic;
        wire.SenderIkPublic = senderIkPublic;
        wire.IsBarePrekey = isBarePrekey;
        return wire;
    }

    /// <summary>
    /// Builds an explicit prekey acknowledgement: a session-bound system frame
    /// (empty plaintext, no user message) sent by the responder after establishing
    /// a session from a bare prekey.
    /// </summary>
    public static WireMessage FromAck(int conversationId, long seq, MessageEnvelope envelope)
    {
        var wire = FromEnvelope(conversationId, seq, envelope);
        wire.FrameType = FrameType.Ack;
        return wire;
    }

    /// <summary>
    /// Builds an identity hello carrying only the sender's Ed25519 public key.
    /// </summary>
    public static WireMessage FromHello(byte[] ed25519PublicKey)
    {
        ArgumentNullException.ThrowIfNull(ed25519PublicKey);
        return new WireMessage
        {
            FrameType = FrameType.Hello,
            SenderPublic = ed25519PublicKey,
        };
    }

    public MessageEnvelope ToEnvelope() =>
        new MessageEnvelope(SenderPublic, Nonce, Ciphertext, Tag, Signature);

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this);

    public static WireMessage Deserialize(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return JsonSerializer.Deserialize<WireMessage>(bytes)
            ?? throw new InvalidDataException("Could not deserialize a WireMessage.");
    }
}
