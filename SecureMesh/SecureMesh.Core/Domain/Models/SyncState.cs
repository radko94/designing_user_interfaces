using SQLite;

namespace SecureMesh.Core.Domain;

/// <summary>
/// Tracks, per conversation and per peer, the highest message sequence number
/// that has been exchanged, so missing ranges can be computed during sync.
/// </summary>
[Table("SyncState")]
public sealed class SyncState
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ConversationId { get; set; }

    [Indexed]
    public int PeerId { get; set; }

    public long LastSeq { get; set; }
}
