using Sodium;

namespace SecureMesh.Core;

/// <summary>Generates fresh long-term identity key pairs and one-time X25519 keys.</summary>
public static class KeyGeneration
{
    /// <summary>
    /// Creates a fresh identity: an Ed25519 signing key pair (32-byte public,
    /// 64-byte private), an X25519 identity Diffie-Hellman key pair (32-byte
    /// public, 32-byte private), and an X25519 signed-prekey (SPK) pair (32-byte
    /// each), each from libsodium's CSPRNG.
    /// </summary>
    /// <remarks>
    /// The third key pair is still named "ephemeral" across the codebase for
    /// compatibility, but it is reinterpreted as the medium-term <b>signed
    /// prekey (SPK)</b>: it is persisted (Keystore-backed) and published in the
    /// QR payload, and is used in the X3DH-lite session derivation. It is NOT the
    /// per-session one-time key.
    /// </remarks>
    public static IdentityKeyPair CreateIdentity()
    {
        SodiumCore.Init();

        using var signing = PublicKeyAuth.GenerateKeyPair();  // Ed25519 (crypto_sign)
        using var dh = PublicKeyBox.GenerateKeyPair();        // X25519 long-term (crypto_box)
        using var ephemeral = PublicKeyBox.GenerateKeyPair(); // X25519 signed prekey (SPK)

        // IdentityKeyPair defensively clones the arrays before the KeyPair
        // instances are disposed (which zeroes their private keys in memory).
        return new IdentityKeyPair(
            signing.PublicKey,
            signing.PrivateKey,
            dh.PublicKey,
            dh.PrivateKey,
            ephemeral.PublicKey,
            ephemeral.PrivateKey);
    }

    /// <summary>
    /// Generates a fresh one-time X25519 key pair (32-byte public, 32-byte
    /// private) for session establishment. The caller owns the returned private
    /// key and MUST zero it (<see cref="System.Security.Cryptography.CryptographicOperations.ZeroMemory"/>)
    /// once the session key has been derived — it is never persisted.
    /// </summary>
    public static (byte[] PublicKey, byte[] PrivateKey) GenerateX25519KeyPair()
    {
        SodiumCore.Init();
        using var kp = PublicKeyBox.GenerateKeyPair();
        return ((byte[])kp.PublicKey.Clone(), (byte[])kp.PrivateKey.Clone());
    }
}
