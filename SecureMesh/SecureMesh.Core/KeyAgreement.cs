using System.Security.Cryptography;
using System.Text;
using Sodium;

namespace SecureMesh.Core;

/// <summary>X25519 key agreement and session-key derivation.</summary>
public static class KeyAgreement
{
    /// <summary>
    /// Computes the X25519 Diffie-Hellman shared secret (32 bytes) from this
    /// party's private key and the peer's public key.
    /// </summary>
    public static byte[] Agree(byte[] myDhPrivate, byte[] peerDhPublic)
    {
        ArgumentNullException.ThrowIfNull(myDhPrivate);
        ArgumentNullException.ThrowIfNull(peerDhPublic);

        SodiumCore.Init();
        return ScalarMult.Mult(myDhPrivate, peerDhPublic);
    }

    /// <summary>
    /// Derives a 32-byte AES-256 session key from a shared secret using
    /// HKDF-SHA256. The <paramref name="info"/> string is UTF-8 encoded and
    /// used as the HKDF info/context binding.
    /// </summary>
    public static byte[] DeriveSessionKey(byte[] sharedSecret, byte[] salt, string info)
    {
        ArgumentNullException.ThrowIfNull(sharedSecret);
        ArgumentNullException.ThrowIfNull(info);

        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            sharedSecret,
            outputLength: 32,
            salt,
            Encoding.UTF8.GetBytes(info));
    }
}
