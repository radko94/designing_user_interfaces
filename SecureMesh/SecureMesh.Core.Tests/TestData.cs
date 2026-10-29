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
}
