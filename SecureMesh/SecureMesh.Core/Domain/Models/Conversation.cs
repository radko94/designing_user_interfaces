using SQLite;

namespace SecureMesh.Core.Domain;

/// <summary>
/// A conversation's cryptographic state. <see cref="SessionKey"/> is the
/// DH-derived root key; <see cref="RatchetState"/> is the current one-way
/// chain state used to derive per-message AES keys.
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

    public byte[] SessionKey { get; set; } = Array.Empty<byte>();

    public byte[] RatchetState { get; set; } = Array.Empty<byte>();
}
