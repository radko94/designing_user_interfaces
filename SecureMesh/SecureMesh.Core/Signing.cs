using Sodium;

namespace SecureMesh.Core;

/// <summary>Ed25519 digital signatures.</summary>
public static class Signing
{
    /// <summary>
    /// Produces a detached Ed25519 signature (64 bytes) over
    /// <paramref name="message"/> using the 64-byte libsodium private key.
    /// </summary>
    public static byte[] Sign(byte[] message, byte[] ed25519Private)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(ed25519Private);

        SodiumCore.Init();
        return PublicKeyAuth.SignDetached(message, ed25519Private);
    }

    /// <summary>
    /// Verifies a detached Ed25519 signature. Returns <c>false</c> (rather than
    /// throwing) when the signature is invalid for the given message/key.
    /// </summary>
    public static bool Verify(byte[] message, byte[] signature, byte[] ed25519Public)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(ed25519Public);

        SodiumCore.Init();
        return PublicKeyAuth.VerifyDetached(signature, message, ed25519Public);
    }
}
