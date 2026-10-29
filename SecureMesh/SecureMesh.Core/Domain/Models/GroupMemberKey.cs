using SQLite;

namespace SecureMesh.Core.Domain;

/// <summary>
/// Per-member pairwise cryptographic session inside a group. Each member of a
/// group holds a session key + ratchet state for every OTHER member, so a group
/// message is encrypted separately to each member (pairwise E2E, §12) rather
/// than with a single shared group key.
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

    /// <summary>DH-derived root key (32 bytes) for this pairwise channel.</summary>
    public byte[] SessionKey { get; set; } = Array.Empty<byte>();

    /// <summary>Current one-way ratchet chain state for this pairwise channel.</summary>
    public byte[] RatchetState { get; set; } = Array.Empty<byte>();
}
