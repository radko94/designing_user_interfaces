using SQLite;

namespace SecureMesh.Core.Domain;

/// <summary>
/// A known peer. <see cref="Ed25519Pub"/> is used to authenticate signed
/// messages; <see cref="X25519Pub"/> is used to derive the session key.
/// </summary>
[Table("Contact")]
public sealed class Contact
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Nickname { get; set; } = "";

    public byte[] Ed25519Pub { get; set; } = Array.Empty<byte>();

    public byte[] X25519Pub { get; set; } = Array.Empty<byte>();

    public bool Verified { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
