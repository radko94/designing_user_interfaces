using System.Security.Cryptography;

namespace SecureMesh.Core;

/// <summary>
/// An authenticated, signed message envelope. The plaintext is encrypted with
/// AES-256-GCM under a session key; the AAD is the context-binding header
/// (context || sender Ed25519 public key || nonce), and the sender then signs
/// (context || sender public key || nonce || ciphertext || tag) with their
/// Ed25519 key so the recipient can authenticate both the sender and the
/// routing context.
/// </summary>
public sealed class MessageEnvelope
{
    private readonly byte[] _senderPublic;
    private readonly byte[] _nonce;
    private readonly byte[] _ciphertext;
    private readonly byte[] _tag;
    private readonly byte[] _signature;

    public MessageEnvelope(byte[] senderPublic, byte[] nonce, byte[] ciphertext, byte[] tag, byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(senderPublic);
        ArgumentNullException.ThrowIfNull(nonce);
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(signature);

        _senderPublic = (byte[])senderPublic.Clone();
        _nonce = (byte[])nonce.Clone();
        _ciphertext = (byte[])ciphertext.Clone();
        _tag = (byte[])tag.Clone();
        _signature = (byte[])signature.Clone();
    }

    /// <summary>Ed25519 public key of the sender, used to verify the signature.</summary>
    public byte[] SenderPublic => (byte[])_senderPublic.Clone();

    public byte[] Nonce => (byte[])_nonce.Clone();

    public byte[] Ciphertext => (byte[])_ciphertext.Clone();

    public byte[] Tag => (byte[])_tag.Clone();

    public byte[] Signature => (byte[])_signature.Clone();

    /// <summary>
    /// Builds the associated data: the context binding, followed by the sender
    /// Ed25519 public key and the nonce.
    /// </summary>
    private static byte[] BuildAad(byte[] context, byte[] senderPublic, byte[] nonce)
    {
        var aad = new byte[context.Length + senderPublic.Length + nonce.Length];
        context.CopyTo(aad, 0);
        senderPublic.CopyTo(aad, context.Length);
        nonce.CopyTo(aad, context.Length + senderPublic.Length);
        return aad;
    }

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> with AES-256-GCM under
    /// <paramref name="key"/>, binding <paramref name="context"/> into the AAD,
    /// then signs context || header || ciphertext || tag with the sender's
    /// Ed25519 key.
    /// </summary>
    public static MessageEnvelope Seal(
        byte[] key,
        byte[] plaintext,
        byte[] senderEdPublic,
        byte[] senderEdPrivate,
        byte[] context)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(senderEdPublic);
        ArgumentNullException.ThrowIfNull(senderEdPrivate);
        ArgumentNullException.ThrowIfNull(context);
        if (key.Length != SymmetricCrypto.KeyBytes)
            throw new ArgumentException($"AES-256 requires a {SymmetricCrypto.KeyBytes}-byte key.", nameof(key));

        var nonce = new byte[SymmetricCrypto.NonceBytes];
        RandomNumberGenerator.Fill(nonce);

        var aad = BuildAad(context, senderEdPublic, nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[SymmetricCrypto.TagBytes];
        using (var aes = new AesGcm(key, SymmetricCrypto.TagBytes))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
        }

        // Sign the authenticated material: context || AAD || ciphertext || tag.
        var signed = new byte[context.Length + aad.Length + ciphertext.Length + tag.Length];
        context.CopyTo(signed, 0);
        aad.CopyTo(signed, context.Length);
        ciphertext.CopyTo(signed, context.Length + aad.Length);
        tag.CopyTo(signed, context.Length + aad.Length + ciphertext.Length);

        var signature = Signing.Sign(signed, senderEdPrivate);

        return new MessageEnvelope(senderEdPublic, nonce, ciphertext, tag, signature);
    }

    /// <summary>
    /// Verifies the sender's signature over the context-bound material, then
    /// decrypts and authenticates the ciphertext. Throws
    /// <see cref="CryptographicException"/> on a signature failure, a
    /// tampered/incorrectly-keyed envelope, or a wrong <paramref name="context"/>.
    /// </summary>
    public static byte[] Open(MessageEnvelope env, byte[] key, byte[] context)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(context);

        var senderPublic = env.SenderPublic;
        var nonce = env.Nonce;
        var ciphertext = env.Ciphertext;
        var tag = env.Tag;
        var signature = env.Signature;

        var aad = BuildAad(context, senderPublic, nonce);

        var signed = new byte[context.Length + aad.Length + ciphertext.Length + tag.Length];
        context.CopyTo(signed, 0);
        aad.CopyTo(signed, context.Length);
        ciphertext.CopyTo(signed, context.Length + aad.Length);
        tag.CopyTo(signed, context.Length + aad.Length + ciphertext.Length);

        if (!Signing.Verify(signed, signature, senderPublic))
            throw new CryptographicException("Message envelope signature verification failed.");

        return SymmetricCrypto.Decrypt(key, nonce, ciphertext, tag, aad);
    }
}
