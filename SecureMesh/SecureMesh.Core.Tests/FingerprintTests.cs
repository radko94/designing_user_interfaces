using SecureMesh.Core;

namespace SecureMesh.Core.Tests;

public class FingerprintTests
{
    [Fact]
    public void Compute_IsDeterministic()
    {
        var a = TestData.RandomBytes(32);
        var b = TestData.RandomBytes(32);

        Assert.Equal(Fingerprint.Compute(a, b), Fingerprint.Compute(a, b));
    }

    [Fact]
    public void Compute_IsOrderIndependent()
    {
        var a = TestData.RandomBytes(32);
        var b = TestData.RandomBytes(32);

        Assert.Equal(Fingerprint.Compute(a, b), Fingerprint.Compute(b, a));
    }

    [Fact]
    public void Compute_DiffersForDifferentKeys()
    {
        var a = TestData.RandomBytes(32);
        var b = TestData.RandomBytes(32);
        var c = TestData.RandomBytes(32);

        Assert.NotEqual(Fingerprint.Compute(a, b), Fingerprint.Compute(a, c));
    }

    [Fact]
    public void Compute_ProducesGroupedDigitString()
    {
        var fp = Fingerprint.Compute(TestData.RandomBytes(32), TestData.RandomBytes(32));

        // 12 groups of 5 digits separated by single spaces.
        Assert.Matches(@"^\d{5}( \d{5}){11}$", fp);
    }
}
