namespace SecureMesh.Core;

/// <summary>
/// A long-term identity made of an Ed25519 signing key pair, an X25519
/// Diffie-Hellman identity key pair, and an X25519 signed-prekey (SPK) pair.
/// The SPK is the medium-term key persisted in Keystore-backed storage and
/// published in the QR payload; it is one input to the X3DH-lite session-key
/// derivation. (The member names still say "ephemeral"/"prekey" for
/// compatibility; they are the SPK, NOT the per-session one-time key.) All key
/// material is stored defensively: the constructors clone the inputs and the
/// getters return fresh copies, so callers can never mutate the internal state.
/// </summary>
public sealed class IdentityKeyPair
{
    public const int Ed25519PublicKeyBytes = 32;
    public const int Ed25519PrivateKeyBytes = 64;
    public const int X25519PublicKeyBytes = 32;
    public const int X25519PrivateKeyBytes = 32;

    private readonly byte[] _signingPublic;
    private readonly byte[] _signingPrivate;
    private readonly byte[] _dhPublic;
    private readonly byte[] _dhPrivate;
    private readonly byte[] _ephemeralDhPublic;
    private readonly byte[] _ephemeralDhPrivate;

    /// <summary>
    /// Constructs an identity with an empty signed-prekey (SPK) X25519 pair.
    /// Kept for backward compatibility (e.g. identities persisted before the
    /// SPK was introduced). Such an identity cannot establish a forward-secret
    /// session (no SPK to use as a 3-DH input).
    /// </summary>
    public IdentityKeyPair(byte[] signingPublic, byte[] signingPrivate, byte[] dhPublic, byte[] dhPrivate)
        : this(signingPublic, signingPrivate, dhPublic, dhPrivate, Array.Empty<byte>(), Array.Empty<byte>())
    {
    }

    /// <summary>
    /// Constructs a full identity including the 32-byte signed-prekey (SPK)
    /// X25519 pair (kept under the legacy "ephemeral" names).
    /// </summary>
    public IdentityKeyPair(
        byte[] signingPublic,
        byte[] signingPrivate,
        byte[] dhPublic,
        byte[] dhPrivate,
        byte[] ephemeralDhPublic,
        byte[] ephemeralDhPrivate)
    {
        ArgumentNullException.ThrowIfNull(signingPublic);
        ArgumentNullException.ThrowIfNull(signingPrivate);
        ArgumentNullException.ThrowIfNull(dhPublic);
        ArgumentNullException.ThrowIfNull(dhPrivate);
        ArgumentNullException.ThrowIfNull(ephemeralDhPublic);
        ArgumentNullException.ThrowIfNull(ephemeralDhPrivate);

        if (signingPublic.Length != Ed25519PublicKeyBytes)
            throw new ArgumentException($"Ed25519 public key must be {Ed25519PublicKeyBytes} bytes.", nameof(signingPublic));
        if (signingPrivate.Length != Ed25519PrivateKeyBytes)
            throw new ArgumentException($"Ed25519 private key must be {Ed25519PrivateKeyBytes} bytes.", nameof(signingPrivate));
        if (dhPublic.Length != X25519PublicKeyBytes)
            throw new ArgumentException($"X25519 public key must be {X25519PublicKeyBytes} bytes.", nameof(dhPublic));
        if (dhPrivate.Length != X25519PrivateKeyBytes)
            throw new ArgumentException($"X25519 private key must be {X25519PrivateKeyBytes} bytes.", nameof(dhPrivate));

        // The SPK pair may be empty (legacy) or exactly 32 bytes.
        if (ephemeralDhPublic.Length != 0 && ephemeralDhPublic.Length != X25519PublicKeyBytes)
            throw new ArgumentException($"Signed-prekey X25519 public key must be empty or {X25519PublicKeyBytes} bytes.", nameof(ephemeralDhPublic));
        if (ephemeralDhPrivate.Length != 0 && ephemeralDhPrivate.Length != X25519PrivateKeyBytes)
            throw new ArgumentException($"Signed-prekey X25519 private key must be empty or {X25519PrivateKeyBytes} bytes.", nameof(ephemeralDhPrivate));

        _signingPublic = (byte[])signingPublic.Clone();
        _signingPrivate = (byte[])signingPrivate.Clone();
        _dhPublic = (byte[])dhPublic.Clone();
        _dhPrivate = (byte[])dhPrivate.Clone();
        _ephemeralDhPublic = (byte[])ephemeralDhPublic.Clone();
        _ephemeralDhPrivate = (byte[])ephemeralDhPrivate.Clone();
    }

    /// <summary>Ed25519 signing public key (32 bytes).</summary>
    public byte[] SigningPublicKey => (byte[])_signingPublic.Clone();

    /// <summary>Ed25519 signing private key (libsodium 64-byte form).</summary>
    public byte[] SigningPrivateKey => (byte[])_signingPrivate.Clone();

    /// <summary>X25519 Diffie-Hellman public key (32 bytes).</summary>
    public byte[] DhPublicKey => (byte[])_dhPublic.Clone();

    /// <summary>X25519 Diffie-Hellman private key (32 bytes).</summary>
    public byte[] DhPrivateKey => (byte[])_dhPrivate.Clone();

    /// <summary>Signed-prekey (SPK) X25519 public key (32 bytes, or empty for legacy identities).</summary>
    public byte[] EphemeralDhPublicKey => (byte[])_ephemeralDhPublic.Clone();

    /// <summary>Signed-prekey (SPK) X25519 private key (32 bytes, or empty for legacy identities).</summary>
    public byte[] EphemeralDhPrivateKey => (byte[])_ephemeralDhPrivate.Clone();

    /// <summary>True when this identity carries a usable signed-prekey (SPK) pair.</summary>
    public bool HasEphemeralKeys => _ephemeralDhPublic.Length == X25519PublicKeyBytes && _ephemeralDhPrivate.Length == X25519PrivateKeyBytes;
}
