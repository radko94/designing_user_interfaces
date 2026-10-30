using SQLite;

namespace SecureMesh.Core.Domain;

/// <summary>
/// A known peer. <see cref="Ed25519Pub"/> is used to authenticate signed
/// messages; <see cref="X25519Pub"/> is the peer's long-term X25519 identity
/// key and <see cref="EphemeralX25519Pub"/> is the peer's signed prekey (SPK).
/// The SPK is used (with the sender's one-time key) in the X3DH-lite session
/// derivation; the member name is kept as "Ephemeral" for compatibility.
/// </summary>
[Table("Contact")]
public sealed class Contact
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Nickname { get; set; } = "";

    public byte[] Ed25519Pub { get; set; } = Array.Empty<byte>();

    public byte[] X25519Pub { get; set; } = Array.Empty<byte>();

    /// <summary>Signed prekey (SPK) X25519 public key (32 bytes; empty for legacy contacts).</summary>
    public byte[] EphemeralX25519Pub { get; set; } = Array.Empty<byte>();

    public bool Verified { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
