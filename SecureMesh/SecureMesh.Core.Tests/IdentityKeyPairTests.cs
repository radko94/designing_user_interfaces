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
        Assert.NotNull(id.EphemeralDhPublicKey);
        Assert.NotNull(id.EphemeralDhPrivateKey);

        Assert.Equal(32, id.SigningPublicKey.Length);
        Assert.Equal(64, id.SigningPrivateKey.Length);
        Assert.Equal(32, id.DhPublicKey.Length);
        Assert.Equal(32, id.DhPrivateKey.Length);
        Assert.Equal(32, id.EphemeralDhPublicKey.Length);
        Assert.Equal(32, id.EphemeralDhPrivateKey.Length);
        Assert.True(id.HasEphemeralKeys);
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
        Assert.False(a.EphemeralDhPublicKey.SequenceEqual(b.EphemeralDhPublicKey));
        Assert.False(a.EphemeralDhPrivateKey.SequenceEqual(b.EphemeralDhPrivateKey));
    }

    [Fact]
    public void IdentityKeyPair_GetterReturnsDefensiveCopy()
    {
        var id = KeyGeneration.CreateIdentity();

        var copy = id.SigningPublicKey;
        copy[0] ^= 0xFF;

        Assert.False(copy.SequenceEqual(id.SigningPublicKey));

        var ephemeralCopy = id.EphemeralDhPublicKey;
        ephemeralCopy[0] ^= 0xFF;
        Assert.False(ephemeralCopy.SequenceEqual(id.EphemeralDhPublicKey));
    }

    [Fact]
    public void IdentityKeyPair_FourArgConstructor_HasEmptyEphemeralKeys()
    {
        var id = KeyGeneration.CreateIdentity();
        var legacy = new IdentityKeyPair(id.SigningPublicKey, id.SigningPrivateKey, id.DhPublicKey, id.DhPrivateKey);

        Assert.Empty(legacy.EphemeralDhPublicKey);
        Assert.Empty(legacy.EphemeralDhPrivateKey);
        Assert.False(legacy.HasEphemeralKeys);
    }
}
