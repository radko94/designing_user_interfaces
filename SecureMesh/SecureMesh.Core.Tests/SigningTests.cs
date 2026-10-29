using System.Text;
using SecureMesh.Core;

namespace SecureMesh.Core.Tests;

public class SigningTests
{
    [Fact]
    public void SignVerify_ValidSignatureVerifies()
    {
        var id = KeyGeneration.CreateIdentity();
        var message = Encoding.UTF8.GetBytes("authenticate this");

        var signature = Signing.Sign(message, id.SigningPrivateKey);

        Assert.Equal(64, signature.Length);
        Assert.True(Signing.Verify(message, signature, id.SigningPublicKey));
    }

    [Fact]
    public void Verify_FailsOnTamperedMessage()
    {
        var id = KeyGeneration.CreateIdentity();
        var message = Encoding.UTF8.GetBytes("original");
        var signature = Signing.Sign(message, id.SigningPrivateKey);

        var tampered = Encoding.UTF8.GetBytes("tampered");

        Assert.False(Signing.Verify(tampered, signature, id.SigningPublicKey));
    }

    [Fact]
    public void Verify_FailsOnTamperedSignature()
    {
        var id = KeyGeneration.CreateIdentity();
        var message = Encoding.UTF8.GetBytes("original");
        var signature = Signing.Sign(message, id.SigningPrivateKey);

        signature[0] ^= 0x01;

        Assert.False(Signing.Verify(message, signature, id.SigningPublicKey));
    }

    [Fact]
    public void Verify_FailsWithWrongKey()
    {
        var id = KeyGeneration.CreateIdentity();
        var other = KeyGeneration.CreateIdentity();
        var message = Encoding.UTF8.GetBytes("original");
        var signature = Signing.Sign(message, id.SigningPrivateKey);

        Assert.False(Signing.Verify(message, signature, other.SigningPublicKey));
    }
}
