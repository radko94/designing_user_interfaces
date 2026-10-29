using SecureMesh.Core;

namespace SecureMesh.Core.Tests;

public class IdentityKeyPairTests
{
    [Fact]
    public void CreateIdentity_ProducesNonNullKeysOfExpectedLengths()
    {
        var id = KeyGeneration.CreateIdentity();

        Assert.NotNull(id);
        Assert.NotNull(id.SigningPublicKey);
        Assert.NotNull(id.SigningPrivateKey);
        Assert.NotNull(id.DhPublicKey);
        Assert.NotNull(id.DhPrivateKey);

        Assert.Equal(32, id.SigningPublicKey.Length);
        Assert.Equal(64, id.SigningPrivateKey.Length);
        Assert.Equal(32, id.DhPublicKey.Length);
        Assert.Equal(32, id.DhPrivateKey.Length);
    }

    [Fact]
    public void CreateIdentity_ProducesDistinctIdentities()
    {
        var a = KeyGeneration.CreateIdentity();
        var b = KeyGeneration.CreateIdentity();

        Assert.False(a.SigningPublicKey.SequenceEqual(b.SigningPublicKey));
        Assert.False(a.SigningPrivateKey.SequenceEqual(b.SigningPrivateKey));
        Assert.False(a.DhPublicKey.SequenceEqual(b.DhPublicKey));
        Assert.False(a.DhPrivateKey.SequenceEqual(b.DhPrivateKey));
    }

    [Fact]
    public void IdentityKeyPair_GetterReturnsDefensiveCopy()
    {
        var id = KeyGeneration.CreateIdentity();

        var copy = id.SigningPublicKey;
        copy[0] ^= 0xFF;

        Assert.False(copy.SequenceEqual(id.SigningPublicKey));
    }
}
