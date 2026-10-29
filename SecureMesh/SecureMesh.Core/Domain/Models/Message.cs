using SQLite;

namespace SecureMesh.Core.Domain;

/// <summary>Delivery status of a locally stored message.</summary>
public enum MessageStatus
{
    /// <summary>Sent by the local user.</summary>
    Sent = 0,

    /// <summary>Received and decrypted from a peer.</summary>
    Received = 1,
}

/// <summary>
/// A message stored encrypted-at-rest. Only the authenticated ciphertext,
/// nonce, tag and signature are persisted; the plaintext is delivered to the
/// caller transiently and can be re-derived with the conversation's session
/// key and ratchet chain when needed.
/// </summary>
[Table("Message")]
public sealed class Message
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ConversationId { get; set; }

    [Indexed]
    public long Seq { get; set; }

    /// <summary>Contact id of the sender; 0 means the local user.</summary>
    [Indexed]
    public int SenderId { get; set; }

    public byte[] Ciphertext { get; set; } = Array.Empty<byte>();

    public byte[] Nonce { get; set; } = Array.Empty<byte>();

    public byte[] Tag { get; set; } = Array.Empty<byte>();

    public byte[] Signature { get; set; } = Array.Empty<byte>();

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public MessageStatus Status { get; set; } = MessageStatus.Sent;
}
