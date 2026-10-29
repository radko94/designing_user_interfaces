using System.Security.Cryptography;
using System.Text;

namespace SecureMesh.Core.Domain;

/// <summary>
/// A per-conversation cryptographic session. The session (root) key is derived
/// from X25519 key agreement; a one-way ratchet chain seeded with that root key
/// yields a fresh AES-256 key for every message. Messages are sealed into a
/// signed, authenticated <see cref="MessageEnvelope"/>.
/// </summary>
public sealed class CryptoSession
{
    /// <summary>HKDF info context used when deriving the root session key.</summary>
    public const string DefaultInfo = "SecureMesh.Session/v1";

    /// <summary>HKDF salt used when deriving the root session key (all zeroes).</summary>
    public static readonly byte[] DefaultSalt = new byte[32];

    /// <summary>
    /// Matches the info context used internally by <see cref="Ratchet"/> so that
    /// a chain seeded identically here advances to the same message keys.
    /// </summary>
    private static readonly byte[] RatchetInfo = Encoding.UTF8.GetBytes("SecureMesh.Ratchet/v1");

    /// <summary>Maximum look-ahead steps when decrypting an out-of-order message.</summary>
    private const int MaxSkip = 16;

    private readonly byte[] _sessionKey;
    private readonly byte[] _senderEdPublic;
    private readonly byte[] _senderEdPrivate;
    private byte[] _ratchetState;

    public CryptoSession(
        byte[] sessionKey,
        byte[] ratchetState,
        byte[] senderEdPublic,
        byte[] senderEdPrivate)
    {
        ArgumentNullException.ThrowIfNull(sessionKey);
        ArgumentNullException.ThrowIfNull(ratchetState);
        ArgumentNullException.ThrowIfNull(senderEdPublic);
        ArgumentNullException.ThrowIfNull(senderEdPrivate);

        if (sessionKey.Length != SymmetricCrypto.KeyBytes)
            throw new ArgumentException($"A session key must be {SymmetricCrypto.KeyBytes} bytes.", nameof(sessionKey));
        if (ratchetState.Length == 0)
            throw new ArgumentException("The ratchet state must not be empty.", nameof(ratchetState));

        _sessionKey = (byte[])sessionKey.Clone();
        _ratchetState = (byte[])ratchetState.Clone();
        _senderEdPublic = (byte[])senderEdPublic.Clone();
        _senderEdPrivate = (byte[])senderEdPrivate.Clone();
    }

    /// <summary>The DH-derived root key (32 bytes).</summary>
    public byte[] SessionKey => (byte[])_sessionKey.Clone();

    /// <summary>The current ratchet chain state.</summary>
    public byte[] RatchetState => (byte[])_ratchetState.Clone();

    /// <summary>
    /// Establishes the root session key: X25519 agreement followed by
    /// HKDF-SHA256 derivation. Both peers call this with their own private key
    /// and the peer's public key and obtain the same result.
    /// </summary>
    public static byte[] EstablishSessionKey(
        byte[] myDhPrivate,
        byte[] peerDhPublic,
        byte[]? salt = null,
        string? info = null)
    {
        var shared = KeyAgreement.Agree(myDhPrivate, peerDhPublic);
        return KeyAgreement.DeriveSessionKey(shared, salt ?? DefaultSalt, info ?? DefaultInfo);
    }

    /// <summary>
    /// Advances the ratchet chain one step and returns the new 32-byte message
    /// key together with the next chain state.
    /// </summary>
    public static (byte[] Key, byte[] State) AdvanceRatchet(byte[] state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Length == 0)
            throw new ArgumentException("The ratchet state must not be empty.", nameof(state));

        var okm = HKDF.DeriveKey(HashAlgorithmName.SHA256, state, SymmetricCrypto.KeyBytes * 2, salt: null, RatchetInfo);

        var key = okm.AsSpan(0, SymmetricCrypto.KeyBytes).ToArray();
        var next = okm.AsSpan(SymmetricCrypto.KeyBytes, SymmetricCrypto.KeyBytes).ToArray();
        CryptographicOperations.ZeroMemory(okm);

        return (key, next);
    }

    /// <summary>
    /// Encrypts and signs <paramref name="plaintext"/> with the NEXT ratchet
    /// key, then commits the advanced state.
    /// </summary>
    public MessageEnvelope Seal(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var (key, nextState) = AdvanceRatchet(_ratchetState);
        _ratchetState = nextState;

        return MessageEnvelope.Seal(key, plaintext, _senderEdPublic, _senderEdPrivate);
    }

    /// <summary>
    /// Verifies and decrypts an incoming envelope by trying the next ratchet
    /// key and advancing up to <see cref="MaxSkip"/> steps to tolerate minor
    /// reordering. On success the ratchet state is committed; on failure
    /// nothing is committed and a <see cref="CryptographicException"/> is
    /// thrown.
    /// </summary>
    public byte[] Open(MessageEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var candidate = (byte[])_ratchetState.Clone();
        for (var i = 0; i <= MaxSkip; i++)
        {
            var (key, nextState) = AdvanceRatchet(candidate);
            try
            {
                var plaintext = MessageEnvelope.Open(envelope, key);
                _ratchetState = nextState;
                return plaintext;
            }
            catch (CryptographicException)
            {
                candidate = nextState;
            }
        }

        throw new CryptographicException("Could not decrypt the message with the current ratchet state.");
    }
}
