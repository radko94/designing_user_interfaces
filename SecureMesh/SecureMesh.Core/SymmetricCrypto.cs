using System.Security.Cryptography;

namespace SecureMesh.Core;

/// <summary>AES-256-GCM authenticated symmetric encryption.</summary>
public static class SymmetricCrypto
{
    public const int KeyBytes = 32;   // AES-256
    public const int NonceBytes = 12; // recommended GCM nonce size
    public const int TagBytes = 16;   // 128-bit authentication tag

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> with AES-256-GCM under
    /// <paramref name="key"/> and authenticates <paramref name="aad"/>.
    /// Returns the ciphertext, the 12-byte nonce, and the 16-byte tag.
    /// </summary>
    public static (byte[] Ciphertext, byte[] Nonce, byte[] Tag) Encrypt(byte[] key, byte[] plaintext, byte[] aad)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(plaintext);
        if (key.Length != KeyBytes)
            throw new ArgumentException($"AES-256 requires a {KeyBytes}-byte key.", nameof(key));

        var nonce = new byte[NonceBytes];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagBytes];

        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);

        return (ciphertext, nonce, tag);
    }

    /// <summary>
    /// Decrypts and authenticates. Throws
    /// <see cref="AuthenticationTagMismatchException"/> (a
    /// <see cref="CryptographicException"/>) on tamper or an incorrect key.
    /// </summary>
    public static byte[] Decrypt(byte[] key, byte[] nonce, byte[] ciphertext, byte[] tag, byte[] aad)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(nonce);
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentNullException.ThrowIfNull(tag);
        if (key.Length != KeyBytes)
            throw new ArgumentException($"AES-256 requires a {KeyBytes}-byte key.", nameof(key));
        if (nonce.Length != NonceBytes)
            throw new ArgumentException($"GCM nonce must be {NonceBytes} bytes.", nameof(nonce));
        if (tag.Length != TagBytes)
            throw new ArgumentException($"GCM tag must be {TagBytes} bytes.", nameof(tag));

        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);

        return plaintext;
    }
}
