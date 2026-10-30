using System.Security.Cryptography;
using SecureMesh.Core;

namespace SecureMesh.Core.Tests;

internal static class TestData
{
    public static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    public static byte[] Key() => RandomBytes(SymmetricCrypto.KeyBytes);

    public static IdentityKeyPair Identity() => KeyGeneration.CreateIdentity();

    /// <summary>
    /// Creates two identities with a known initiator role: the first is the
    /// deterministic initiator (smaller X25519 identity key) and the second is the
    /// responder.
    /// </summary>
    public static (IdentityKeyPair Initiator, IdentityKeyPair Responder) OrderedIdentities()
    {
        var a = KeyGeneration.CreateIdentity();
        var b = KeyGeneration.CreateIdentity();
        return ChannelId.CompareKeys(a.DhPublicKey, b.DhPublicKey) < 0 ? (a, b) : (b, a);
    }
}
