using System.Security.Cryptography;

namespace SecureMesh.Core;

/// <summary>
/// An authenticated, signed message envelope. The plaintext is encrypted with
/// AES-256-GCM under a session key; the AAD is the header (sender Ed25519
/// public key || nonce), and the sender then signs (AAD || ciphertext || tag)
/// with their Ed25519 key so the recipient can authenticate the sender.
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
    /// Builds the associated data: sender Ed25519 public key followed by the
    /// nonce. (The task signature carries no metadata parameter, so the
    /// "serialized metadata" portion of the header is empty.)
    /// </summary>
    private static byte[] BuildAad(byte[] senderPublic, byte[] nonce)
    {
        var aad = new byte[senderPublic.Length + nonce.Length];
        senderPublic.CopyTo(aad, 0);
        nonce.CopyTo(aad, senderPublic.Length);
        return aad;
    }

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> with AES-256-GCM under
    /// <paramref name="key"/>, then signs the header + ciphertext + tag with the
    /// sender's Ed25519 key.
    /// </summary>
    public static MessageEnvelope Seal(byte[] key, byte[] plaintext, byte[] senderEdPublic, byte[] senderEdPrivate)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(senderEdPublic);
        ArgumentNullException.ThrowIfNull(senderEdPrivate);
        if (key.Length != SymmetricCrypto.KeyBytes)
            throw new ArgumentException($"AES-256 requires a {SymmetricCrypto.KeyBytes}-byte key.", nameof(key));

        var nonce = new byte[SymmetricCrypto.NonceBytes];
        RandomNumberGenerator.Fill(nonce);

        var aad = BuildAad(senderEdPublic, nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[SymmetricCrypto.TagBytes];
        using (var aes = new AesGcm(key, SymmetricCrypto.TagBytes))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
        }

        // Sign the authenticated material: AAD || ciphertext || tag.
        var signed = new byte[aad.Length + ciphertext.Length + tag.Length];
        aad.CopyTo(signed, 0);
        ciphertext.CopyTo(signed, aad.Length);
        tag.CopyTo(signed, aad.Length + ciphertext.Length);

        var signature = Signing.Sign(signed, senderEdPrivate);

        return new MessageEnvelope(senderEdPublic, nonce, ciphertext, tag, signature);
    }

    /// <summary>
    /// Verifies the sender's signature, then decrypts and authenticates the
    /// ciphertext. Throws <see cref="CryptographicException"/> on a signature
    /// failure or a tampered/incorrectly-keyed envelope.
    /// </summary>
    public static byte[] Open(MessageEnvelope env, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(key);

        var senderPublic = env.SenderPublic;
        var nonce = env.Nonce;
        var ciphertext = env.Ciphertext;
        var tag = env.Tag;
        var signature = env.Signature;

        var aad = BuildAad(senderPublic, nonce);

        var signed = new byte[aad.Length + ciphertext.Length + tag.Length];
        aad.CopyTo(signed, 0);
        ciphertext.CopyTo(signed, aad.Length);
        tag.CopyTo(signed, aad.Length + ciphertext.Length);

        if (!Signing.Verify(signed, signature, senderPublic))
            throw new CryptographicException("Message envelope signature verification failed.");

        return SymmetricCrypto.Decrypt(key, nonce, ciphertext, tag, aad);
    }
}
