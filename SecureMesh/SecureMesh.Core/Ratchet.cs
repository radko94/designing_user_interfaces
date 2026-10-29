using System.Security.Cryptography;
using System.Text;

namespace SecureMesh.Core;

/// <summary>
/// A forward-secrecy key ratchet: a one-way KDF chain (root -> key1 -> key2 ->
/// ...). Each call to <see cref="Next"/> returns a fresh 32-byte message key
/// and irreversibly advances the internal chain state. Knowing a message key
/// does not reveal any earlier or later key; the only way to continue the
/// chain is to keep ratcheting forward from the current state.
/// </summary>
public sealed class Ratchet
{
    private const int KeyBytes = 32;

    private static readonly byte[] Info = Encoding.UTF8.GetBytes("SecureMesh.Ratchet/v1");

    private readonly object _sync = new();
    private byte[] _state;

    public Ratchet(byte[] rootKey)
    {
        ArgumentNullException.ThrowIfNull(rootKey);
        if (rootKey.Length == 0)
            throw new ArgumentException("The root key must not be empty.", nameof(rootKey));

        _state = (byte[])rootKey.Clone();
    }

    /// <summary>
    /// Derives and returns the next 32-byte message key, advancing the chain.
    /// </summary>
    public byte[] Next()
    {
        lock (_sync)
        {
            // HKDF-SHA256 expand: first half is the output key, second half is
            // the new chain state. HKDF is one-way, so the old state cannot be
            // recovered from the output key.
            var okm = HKDF.DeriveKey(HashAlgorithmName.SHA256, _state, KeyBytes * 2, salt: null, Info);

            var messageKey = okm.AsSpan(0, KeyBytes).ToArray();
            var nextState = okm.AsSpan(KeyBytes, KeyBytes).ToArray();

            CryptographicOperations.ZeroMemory(_state);
            _state = nextState;

            return messageKey;
        }
    }
}
