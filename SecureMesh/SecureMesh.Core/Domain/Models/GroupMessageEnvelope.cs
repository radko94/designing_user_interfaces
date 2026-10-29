using SQLite;

namespace SecureMesh.Core.Domain;

/// <summary>
/// A single recipient's authenticated ciphertext for a group message. A group
/// message is encrypted once per member, so a message of N recipients produces
/// N rows (same <see cref="Seq"/>, one per <see cref="RecipientContactId"/>).
/// The sender's Ed25519 public key is denormalized onto each row so a
/// store-and-forward relay (and sync catch-up) can authenticate and decrypt a
/// member's own copy without the original wire frame.
/// </summary>
[Table("GroupMessageEnvelope")]
public sealed class GroupMessageEnvelope
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ConversationId { get; set; }

    [Indexed]
    public long Seq { get; set; }

    /// <summary>Ed25519 public key of the sender (identical across all recipients).</summary>
    public byte[] SenderPublic { get; set; } = Array.Empty<byte>();

    /// <summary>Contact id of the member this copy is encrypted to (sender-local id).</summary>
    [Indexed]
    public int RecipientContactId { get; set; }

    public byte[] Nonce { get; set; } = Array.Empty<byte>();

    public byte[] Ciphertext { get; set; } = Array.Empty<byte>();

    public byte[] Tag { get; set; } = Array.Empty<byte>();

    public byte[] Signature { get; set; } = Array.Empty<byte>();
}
