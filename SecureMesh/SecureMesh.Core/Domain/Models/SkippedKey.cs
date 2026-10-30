using SQLite;

namespace SecureMesh.Core.Domain;

/// <summary>
/// A persisted skipped message key. When a message arrives out of order (a
/// higher per-sender sequence before a lower one), the receive chain ratchets
/// past the gap and the intermediate message keys are cached here so the
/// late-arriving message can still be decrypted — even after an app restart.
/// Keyed by (<see cref="ChannelId"/>, <see cref="SenderId"/>, <see cref="SenderSeq"/>)
/// and bounded to at most <see cref="CryptoSession.MaxSkippedKeys"/> per sender.
/// </summary>
[Table("SkippedKey")]
public sealed class SkippedKey
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>Stable channel id (4 bytes for 1:1, 32 bytes for groups).</summary>
    public byte[] ChannelId { get; set; } = Array.Empty<byte>();

    /// <summary>Contact id of the sender this key belongs to.</summary>
    public int SenderId { get; set; }

    /// <summary>The sender's per-sender sequence number of the skipped message.</summary>
    public long SenderSeq { get; set; }

    /// <summary>The 32-byte message key that decrypts that message.</summary>
    public byte[] Key { get; set; } = Array.Empty<byte>();
}
