using SQLite;

namespace SecureMesh.Core.Domain;

/// <summary>
/// Per-member pairwise cryptographic session inside a group. Each member of a
/// group holds two direction-bound ratchet chains for every OTHER member, so a
/// group message is encrypted separately to each member (pairwise E2E) rather
/// than with a single shared group key. The root session key is never persisted;
/// it is discarded after seeding the ratchet states. An empty pair of ratchet
/// states marks an unestablished pairwise session (established lazily on first
/// send or first prekey receive).
/// </summary>
[Table("GroupMemberKey")]
public sealed class GroupMemberKey
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int GroupId { get; set; }

    /// <summary>Contact id of the member this pairwise session is shared with.</summary>
    [Indexed]
    public int MemberContactId { get; set; }

    /// <summary>Send-direction ratchet chain state for this pairwise channel.</summary>
    public byte[] SendRatchetState { get; set; } = Array.Empty<byte>();

    /// <summary>Receive-direction ratchet chain state for this pairwise channel.</summary>
    public byte[] RecvRatchetState { get; set; } = Array.Empty<byte>();

    /// <summary>True once this pairwise session has been established.</summary>
    public bool IsEstablished => SendRatchetState.Length > 0 && RecvRatchetState.Length > 0;
}
