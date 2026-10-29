using SecureMesh.Core;

namespace SecureMesh.Core.Tests;

public class RatchetTests
{
    [Fact]
    public void Next_ReturnsDistinctKeys()
    {
        var ratchet = new Ratchet(TestData.RandomBytes(32));

        var k1 = ratchet.Next();
        var k2 = ratchet.Next();
        var k3 = ratchet.Next();

        Assert.Equal(32, k1.Length);
        Assert.False(k1.SequenceEqual(k2));
        Assert.False(k2.SequenceEqual(k3));
        Assert.False(k1.SequenceEqual(k3));
    }

    [Fact]
    public void Next_IsDeterministicForSameRoot()
    {
        var root = TestData.RandomBytes(32);

        var a = new Ratchet(root);
        var b = new Ratchet(root);

        Assert.Equal(a.Next(), b.Next());
        Assert.Equal(a.Next(), b.Next());
        Assert.Equal(a.Next(), b.Next());
    }

    [Fact]
    public void Next_IsIrreversible_MessageKeyCannotContinueChain()
    {
        var root = TestData.RandomBytes(32);

        var original = new Ratchet(root);
        var k1 = original.Next();
        var k2 = original.Next();

        // Seeding a fresh ratchet with the leaked message key must NOT
        // reproduce the real chain (one-way property of the KDF).
        var fromMessageKey = new Ratchet(k1);
        var forged = fromMessageKey.Next();

        Assert.False(forged.SequenceEqual(k2));
    }

    [Fact]
    public void Next_DiffersAcrossRoots()
    {
        var a = new Ratchet(TestData.RandomBytes(32));
        var b = new Ratchet(TestData.RandomBytes(32));

        Assert.False(a.Next().SequenceEqual(b.Next()));
    }
}
