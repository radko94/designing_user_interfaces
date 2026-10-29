namespace SecureMesh.Core;

/// <summary>
/// A long-term identity made of an Ed25519 signing key pair and an X25519
/// Diffie-Hellman key pair. All key material is stored defensively: the
/// constructor clones the inputs and the getters return fresh copies, so
/// callers can never mutate the internal state of an identity.
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

    public IdentityKeyPair(byte[] signingPublic, byte[] signingPrivate, byte[] dhPublic, byte[] dhPrivate)
    {
        ArgumentNullException.ThrowIfNull(signingPublic);
        ArgumentNullException.ThrowIfNull(signingPrivate);
        ArgumentNullException.ThrowIfNull(dhPublic);
        ArgumentNullException.ThrowIfNull(dhPrivate);

        if (signingPublic.Length != Ed25519PublicKeyBytes)
            throw new ArgumentException($"Ed25519 public key must be {Ed25519PublicKeyBytes} bytes.", nameof(signingPublic));
        if (signingPrivate.Length != Ed25519PrivateKeyBytes)
            throw new ArgumentException($"Ed25519 private key must be {Ed25519PrivateKeyBytes} bytes.", nameof(signingPrivate));
        if (dhPublic.Length != X25519PublicKeyBytes)
            throw new ArgumentException($"X25519 public key must be {X25519PublicKeyBytes} bytes.", nameof(dhPublic));
        if (dhPrivate.Length != X25519PrivateKeyBytes)
            throw new ArgumentException($"X25519 private key must be {X25519PrivateKeyBytes} bytes.", nameof(dhPrivate));

        _signingPublic = (byte[])signingPublic.Clone();
        _signingPrivate = (byte[])signingPrivate.Clone();
        _dhPublic = (byte[])dhPublic.Clone();
        _dhPrivate = (byte[])dhPrivate.Clone();
    }

    /// <summary>Ed25519 signing public key (32 bytes).</summary>
    public byte[] SigningPublicKey => (byte[])_signingPublic.Clone();

    /// <summary>Ed25519 signing private key (libsodium 64-byte form).</summary>
    public byte[] SigningPrivateKey => (byte[])_signingPrivate.Clone();

    /// <summary>X25519 Diffie-Hellman public key (32 bytes).</summary>
    public byte[] DhPublicKey => (byte[])_dhPublic.Clone();

    /// <summary>X25519 Diffie-Hellman private key (32 bytes).</summary>
    public byte[] DhPrivateKey => (byte[])_dhPrivate.Clone();
}
