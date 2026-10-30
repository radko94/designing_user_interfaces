using SQLite;

namespace SecureMesh.Core.Domain;

/// <summary>
/// The sealed, already-encrypted outgoing message that awaits delivery (the
/// outbox). Because a message's ratchet key is consumed exactly once at seal
/// time, a retry must re-send the <b>identical</b> envelope rather than re-seal
/// with a fresh key; the sealed bytes (and, for the first message, the prekey
/// fields) are therefore persisted here until the transport accepts them. For
/// group messages the per-recipient ciphertext lives in
/// <see cref="GroupMessageEnvelope"/> and only the prekey fields are stored here.
/// </summary>
[Table("PendingMessage")]
public sealed class PendingMessage
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ConversationId { get; set; }

    /// <summary>The per-sender wire sequence number of this message.</summary>
    [Indexed]
    public long SenderSeq { get; set; }

    /// <summary>Ed25519 public key of the sender (signature verification).</summary>
    public byte[] SenderPublic { get; set; } = Array.Empty<byte>();

    public byte[] Nonce { get; set; } = Array.Empty<byte>();

    public byte[] Ciphertext { get; set; } = Array.Empty<byte>();

    public byte[] Tag { get; set; } = Array.Empty<byte>();

    public byte[] Signature { get; set; } = Array.Empty<byte>();

    /// <summary>True when this is a (first) prekey message carrying the fields below.</summary>
    public bool IsPrekey { get; set; }

    /// <summary>
    /// True when this prekey is <b>bare</b>: a session-establishment artifact whose
    /// envelope seals an empty plaintext (no user message). An explicit flag rather
    /// than an inference from <see cref="Ciphertext"/> length.
    /// </summary>
    public bool IsBarePrekey { get; set; }

    /// <summary>True when this is an explicit prekey acknowledgement system frame.</summary>
    public bool IsAck { get; set; }

    /// <summary>The sender's one-time X25519 EK public key (prekey messages only).</summary>
    public byte[] SenderEkPublic { get; set; } = Array.Empty<byte>();

    /// <summary>The sender's long-term X25519 identity public key (prekey messages only).</summary>
    public byte[] SenderIkPublic { get; set; } = Array.Empty<byte>();
}
