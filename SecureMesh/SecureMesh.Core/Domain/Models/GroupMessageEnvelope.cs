using SQLite;

namespace SecureMesh.Core.Domain;

/// <summary>
/// A single recipient's authenticated ciphertext for a group message. A group
/// message is encrypted once per member, so a message of N recipients produces
/// N rows (same <see cref="Seq"/>, one per <see cref="RecipientContactId"/>).
/// The sender's Ed25519 public key is denormalized onto each row so a
/// store-and-forward relay (and sync catch-up) can authenticate and decrypt a
/// member's own copy without the original wire frame. These rows keep the
/// ciphertext (unlike <see cref="Message"/>, which stores plaintext) because
/// they are relayed verbatim to other members over the wire.
/// </summary>
[Table("GroupMessageEnvelope")]
public sealed class GroupMessageEnvelope
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ConversationId { get; set; }

    /// <summary>Local/origin ordering sequence shared by all recipients of one message.</summary>
    [Indexed]
    public long Seq { get; set; }

    /// <summary>The sender's per-sender sequence number bound into the context.</summary>
    [Indexed]
    public long SenderSeq { get; set; }

    /// <summary>Ed25519 public key of the sender (identical across all recipients).</summary>
    public byte[] SenderPublic { get; set; } = Array.Empty<byte>();

    /// <summary>True when this copy belongs to a prekey (first) message.</summary>
    public bool IsPrekey { get; set; }

    /// <summary>The sender's one-time X25519 EK public key (prekey messages only).</summary>
    public byte[] SenderEkPublic { get; set; } = Array.Empty<byte>();

    /// <summary>The sender's long-term X25519 identity public key (prekey messages only).</summary>
    public byte[] SenderIkPublic { get; set; } = Array.Empty<byte>();

    /// <summary>Contact id of the member this copy is encrypted to (sender-local id).</summary>
    [Indexed]
    public int RecipientContactId { get; set; }

    public byte[] Nonce { get; set; } = Array.Empty<byte>();

    public byte[] Ciphertext { get; set; } = Array.Empty<byte>();

    public byte[] Tag { get; set; } = Array.Empty<byte>();

    public byte[] Signature { get; set; } = Array.Empty<byte>();
}
