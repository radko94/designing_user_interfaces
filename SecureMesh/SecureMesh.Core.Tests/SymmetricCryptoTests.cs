using System.Security.Cryptography;
using System.Text;
using SecureMesh.Core;

namespace SecureMesh.Core.Tests;

public class SymmetricCryptoTests
{
    [Fact]
    public void EncryptDecrypt_RoundTrips()
    {
        var key = TestData.Key();
        var plaintext = Encoding.UTF8.GetBytes("hello secure mesh");
        var aad = Encoding.UTF8.GetBytes("header");

        var (ciphertext, nonce, tag) = SymmetricCrypto.Encrypt(key, plaintext, aad);
        var decrypted = SymmetricCrypto.Decrypt(key, nonce, ciphertext, tag, aad);

        Assert.Equal(plaintext, decrypted);
        Assert.Equal(12, nonce.Length);
        Assert.Equal(16, tag.Length);
        Assert.Equal(plaintext.Length, ciphertext.Length);
    }

    [Fact]
    public void Decrypt_DetectsCiphertextTampering()
    {
        var key = TestData.Key();
        var plaintext = Encoding.UTF8.GetBytes("tamper me");
        var (ciphertext, nonce, tag) = SymmetricCrypto.Encrypt(key, plaintext, Array.Empty<byte>());

        ciphertext[0] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() =>
            SymmetricCrypto.Decrypt(key, nonce, ciphertext, tag, Array.Empty<byte>()));
    }

    [Fact]
    public void Decrypt_DetectsTagTampering()
    {
        var key = TestData.Key();
        var plaintext = Encoding.UTF8.GetBytes("tamper me");
        var (ciphertext, nonce, tag) = SymmetricCrypto.Encrypt(key, plaintext, Array.Empty<byte>());

        tag[0] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() =>
            SymmetricCrypto.Decrypt(key, nonce, ciphertext, tag, Array.Empty<byte>()));
    }

    [Fact]
    public void Decrypt_DetectsWrongKey()
    {
        var key = TestData.Key();
        var wrong = TestData.Key();
        var plaintext = Encoding.UTF8.GetBytes("secret");
        var (ciphertext, nonce, tag) = SymmetricCrypto.Encrypt(key, plaintext, Array.Empty<byte>());

        Assert.ThrowsAny<CryptographicException>(() =>
            SymmetricCrypto.Decrypt(wrong, nonce, ciphertext, tag, Array.Empty<byte>()));
    }

    [Fact]
    public void Decrypt_DetectsAadTampering()
    {
        var key = TestData.Key();
        var plaintext = Encoding.UTF8.GetBytes("secret");
        var aad = Encoding.UTF8.GetBytes("header");
        var (ciphertext, nonce, tag) = SymmetricCrypto.Encrypt(key, plaintext, aad);

        Assert.ThrowsAny<CryptographicException>(() =>
            SymmetricCrypto.Decrypt(key, nonce, ciphertext, tag, Encoding.UTF8.GetBytes("hacker")));
    }
}
