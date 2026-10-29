using Sodium;

namespace SecureMesh.Core;

/// <summary>Generates fresh long-term identity key pairs.</summary>
public static class KeyGeneration
{
    /// <summary>
    /// Creates a fresh identity: an Ed25519 signing key pair (32-byte public,
    /// 64-byte private) and an X25519 Diffie-Hellman key pair (32-byte public,
    /// 32-byte private), each from libsodium's CSPRNG.
    /// </summary>
    public static IdentityKeyPair CreateIdentity()
    {
        SodiumCore.Init();

        using var signing = PublicKeyAuth.GenerateKeyPair(); // Ed25519 (crypto_sign)
        using var dh = PublicKeyBox.GenerateKeyPair();       // X25519 (crypto_box)

        // IdentityKeyPair defensively clones the arrays before the KeyPair
        // instances are disposed (which zeroes their private keys in memory).
        return new IdentityKeyPair(signing.PublicKey, signing.PrivateKey, dh.PublicKey, dh.PrivateKey);
    }
}
