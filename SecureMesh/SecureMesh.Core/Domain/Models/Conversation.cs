using SQLite;

namespace SecureMesh.Core.Domain;

/// <summary>
/// A conversation's cryptographic state. <see cref="ChannelId"/> is the stable,
/// device-independent channel identifier used for context binding and skipped-key
/// lookup; <see cref="SendRatchetState"/> and <see cref="RecvRatchetState"/> are
/// the two direction-bound chain states used to derive per-message AES keys.
/// The root session key is intentionally NOT persisted — it is discarded as soon
/// as the ratchet states are seeded.
/// </summary>
[Table("Conversation")]
public sealed class Conversation
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>Contact id of the 1:1 peer; null for groups.</summary>
    [Indexed]
    public int? PeerId { get; set; }

    public bool IsGroup { get; set; }

    /// <summary>
    /// Stable channel id: 4 bytes (first 4 of SHA256(sorted(IK_a, IK_b))) for 1:1,
    /// or 32 bytes (SHA256(GroupKey)) for groups. Identical on both peers.
    /// </summary>
    public byte[] ChannelId { get; set; } = Array.Empty<byte>();

    public byte[] SendRatchetState { get; set; } = Array.Empty<byte>();

    public byte[] RecvRatchetState { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// True once the session has been established (both ratchet chains seeded).
    /// A not-yet-established conversation cannot be messaged until the peer
    /// connects (first prekey send or first prekey receive).
    /// </summary>
    public bool IsEstablished => !IsGroup && SendRatchetState.Length > 0 && RecvRatchetState.Length > 0;
}
