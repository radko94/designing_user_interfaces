using SecureMesh.Core;

namespace SecureMesh.Core.Tests;

public class KeyAgreementTests
{
    [Fact]
    public void Agree_IsSymmetric()
    {
        var a = KeyGeneration.CreateIdentity();
        var b = KeyGeneration.CreateIdentity();

        var ab = KeyAgreement.Agree(a.DhPrivateKey, b.DhPublicKey);
        var ba = KeyAgreement.Agree(b.DhPrivateKey, a.DhPublicKey);

        Assert.Equal(32, ab.Length);
        Assert.Equal(ab, ba);
    }

    [Fact]
    public void Agree_ProducesDifferentSecretsForDifferentPeers()
    {
        var a = KeyGeneration.CreateIdentity();
        var b = KeyGeneration.CreateIdentity();
        var c = KeyGeneration.CreateIdentity();

        var ab = KeyAgreement.Agree(a.DhPrivateKey, b.DhPublicKey);
        var ac = KeyAgreement.Agree(a.DhPrivateKey, c.DhPublicKey);

        Assert.False(ab.SequenceEqual(ac));
    }

    [Fact]
    public void DeriveSessionKey_IsDeterministicAnd32Bytes()
    {
        var shared = TestData.RandomBytes(32);
        var salt = TestData.RandomBytes(16);

        var k1 = KeyAgreement.DeriveSessionKey(shared, salt, "chat");
        var k2 = KeyAgreement.DeriveSessionKey(shared, salt, "chat");

        Assert.Equal(32, k1.Length);
        Assert.Equal(k1, k2);
    }

    [Fact]
    public void DeriveSessionKey_DiffersByInfo()
    {
        var shared = TestData.RandomBytes(32);
        var salt = TestData.RandomBytes(16);

        var k1 = KeyAgreement.DeriveSessionKey(shared, salt, "chat");
        var k2 = KeyAgreement.DeriveSessionKey(shared, salt, "other");

        Assert.False(k1.SequenceEqual(k2));
    }
}
