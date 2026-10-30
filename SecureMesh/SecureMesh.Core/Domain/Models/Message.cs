using System.Text.Json.Serialization;
using SQLite;

namespace SecureMesh.Core.Domain;

/// <summary>Delivery status of a locally stored message.</summary>
public enum MessageStatus
{
    /// <summary>Queued for delivery; not yet handed to the transport.</summary>
    Pending = 0,

    /// <summary>Handed to the transport (send attempt returned without throwing).</summary>
    Delivered = 1,

    /// <summary>Received and decrypted from a peer.</summary>
    Received = 2,

    /// <summary>The last send attempt failed and the message awaits retry.</summary>
    Failed = 3,

    /// <summary>
    /// A local system marker (not a user message): consumes a per-sender sequence
    /// number and a ratchet key to establish the session (the initiator's bare
    /// auto-initiated prekey). Excluded from pending/retry and history listings.
    /// </summary>
    System = 4,
}

/// <summary>
/// A message stored as plaintext in the encrypted-at-rest database (the Signal
/// pattern). The wire format remains ciphertext; only the local history stores
/// the decrypted <see cref="Plaintext"/>. The per-message ciphertext/nonce/tag/
/// signature columns are gone — outgoing ciphertext awaiting retry lives in
/// <see cref="PendingMessage"/> instead.
/// </summary>
[Table("Message")]
public sealed class Message
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ConversationId { get; set; }

    /// <summary>
    /// Local monotonic ordering number (shared across both directions, unique
    /// per conversation). Used for storage ordering and the unique index; NOT
    /// the wire sequence number.
    /// </summary>
    [Indexed]
    public long Seq { get; set; }

    /// <summary>
    /// The sender's per-sender sequence number as carried on the wire. This is
    /// the value bound into the message context, used for replay rejection and
    /// skipped-key lookup.
    /// </summary>
    [Indexed]
    public long SenderSeq { get; set; }

    /// <summary>Contact id of the sender; 0 means the local user.</summary>
    [Indexed]
    public int SenderId { get; set; }

    /// <summary>
    /// The decrypted message body. Marked <c>[JsonIgnore]</c> so a sync/relay
    /// payload never transmits plaintext over the wire.
    /// </summary>
    [JsonIgnore]
    public string Plaintext { get; set; } = "";

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public MessageStatus Status { get; set; } = MessageStatus.Pending;
}
