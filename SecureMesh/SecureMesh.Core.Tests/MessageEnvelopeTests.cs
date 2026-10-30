using System.Security.Cryptography;
using System.Text;
using SecureMesh.Core;

namespace SecureMesh.Core.Tests;

public class MessageEnvelopeTests
{
    private static byte[] Context() => MessageContext.Build(1, 1);

    [Fact]
    public void SealOpen_RoundTrips()
    {
        var id = KeyGeneration.CreateIdentity();
        var key = TestData.Key();
        var plaintext = Encoding.UTF8.GetBytes("secret message");
        var context = Context();

        var env = MessageEnvelope.Seal(key, plaintext, id.SigningPublicKey, id.SigningPrivateKey, context);

        Assert.NotNull(env);
        Assert.Equal(id.SigningPublicKey, env.SenderPublic);
        Assert.Equal(12, env.Nonce.Length);
        Assert.Equal(16, env.Tag.Length);
        Assert.Equal(64, env.Signature.Length);

        var opened = MessageEnvelope.Open(env, key, context);
        Assert.Equal(plaintext, opened);
    }

    [Fact]
    public void Open_DetectsTamperedCiphertext()
    {
        var id = KeyGeneration.CreateIdentity();
        var key = TestData.Key();
        var context = Context();
        var env = MessageEnvelope.Seal(
            key, Encoding.UTF8.GetBytes("secret message"), id.SigningPublicKey, id.SigningPrivateKey, context);

        var ciphertext = env.Ciphertext;
        ciphertext[0] ^= 0x01;
        var tampered = new MessageEnvelope(env.SenderPublic, env.Nonce, ciphertext, env.Tag, env.Signature);

        Assert.ThrowsAny<CryptographicException>(() => MessageEnvelope.Open(tampered, key, context));
    }

    [Fact]
    public void Open_DetectsTamperedTag()
    {
        var id = KeyGeneration.CreateIdentity();
        var key = TestData.Key();
        var context = Context();
        var env = MessageEnvelope.Seal(
            key, Encoding.UTF8.GetBytes("secret"), id.SigningPublicKey, id.SigningPrivateKey, context);

        var tag = env.Tag;
        tag[0] ^= 0x01;
        var tampered = new MessageEnvelope(env.SenderPublic, env.Nonce, env.Ciphertext, tag, env.Signature);

        Assert.ThrowsAny<CryptographicException>(() => MessageEnvelope.Open(tampered, key, context));
    }

    [Fact]
    public void Open_DetectsTamperedSignature()
    {
        var id = KeyGeneration.CreateIdentity();
        var key = TestData.Key();
        var context = Context();
        var env = MessageEnvelope.Seal(
            key, Encoding.UTF8.GetBytes("secret"), id.SigningPublicKey, id.SigningPrivateKey, context);

        var signature = env.Signature;
        signature[0] ^= 0x01;
        var tampered = new MessageEnvelope(env.SenderPublic, env.Nonce, env.Ciphertext, env.Tag, signature);

        Assert.ThrowsAny<CryptographicException>(() => MessageEnvelope.Open(tampered, key, context));
    }

    [Fact]
    public void Open_DetectsWrongSenderKey()
    {
        var id = KeyGeneration.CreateIdentity();
        var attacker = KeyGeneration.CreateIdentity();
        var key = TestData.Key();
        var context = Context();
        var env = MessageEnvelope.Seal(
            key, Encoding.UTF8.GetBytes("secret"), id.SigningPublicKey, id.SigningPrivateKey, context);

        var forged = new MessageEnvelope(attacker.SigningPublicKey, env.Nonce, env.Ciphertext, env.Tag, env.Signature);

        Assert.ThrowsAny<CryptographicException>(() => MessageEnvelope.Open(forged, key, context));
    }

    [Fact]
    public void Open_DetectsWrongSessionKey()
    {
        var id = KeyGeneration.CreateIdentity();
        var key = TestData.Key();
        var wrong = TestData.Key();
        var context = Context();
        var env = MessageEnvelope.Seal(
            key, Encoding.UTF8.GetBytes("secret"), id.SigningPublicKey, id.SigningPrivateKey, context);

        Assert.ThrowsAny<CryptographicException>(() => MessageEnvelope.Open(env, wrong, context));
    }

    [Fact]
    public void Open_DetectsWrongContext()
    {
        var id = KeyGeneration.CreateIdentity();
        var key = TestData.Key();
        var env = MessageEnvelope.Seal(
            key, Encoding.UTF8.GetBytes("secret"), id.SigningPublicKey, id.SigningPrivateKey, Context());

        var wrongContext = MessageContext.Build(2, 2);
        Assert.ThrowsAny<CryptographicException>(() => MessageEnvelope.Open(env, key, wrongContext));
    }
}
