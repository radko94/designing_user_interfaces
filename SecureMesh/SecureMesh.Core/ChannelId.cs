using System.Security.Cryptography;
using System.Text;

namespace SecureMesh.Core;

/// <summary>
/// Stable, device-independent channel identifiers used for context binding and
/// skipped-key lookup. A channel id is derived from long-term public keys (or a
/// shared group key) rather than the device-local auto-increment conversation
/// id, so both peers compute the identical value regardless of their local
/// database state.
/// </summary>
public static class ChannelId
{
    /// <summary>
    /// The stable 1:1 channel id: the first 4 bytes of
    /// <c>SHA256(sorted(IK_a, IK_b))</c>. The two identity X25519 public keys
    /// are sorted lexicographically before hashing so both peers produce the
    /// same 4 bytes independent of argument order.
    /// </summary>
    public static byte[] ForOneToOne(byte[] ikA, byte[] ikB)
    {
        ArgumentNullException.ThrowIfNull(ikA);
        ArgumentNullException.ThrowIfNull(ikB);

        byte[] first;
        byte[] second;
        if (LexicographicCompare(ikA, ikB) <= 0)
        {
            first = ikA;
            second = ikB;
        }
        else
        {
            first = ikB;
            second = ikA;
        }

        var concat = new byte[first.Length + second.Length];
        first.CopyTo(concat, 0);
        second.CopyTo(concat, first.Length);

        var hash = SHA256.HashData(concat);
        return hash.AsSpan(0, 4).ToArray();
    }

    /// <summary>
    /// The stable group channel id: the full <c>SHA256(GroupKey)</c> (32 bytes).
    /// Every member of the group derives the same value from the shared key.
    /// </summary>
    public static byte[] ForGroup(string groupKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupKey);
        return SHA256.HashData(Encoding.UTF8.GetBytes(groupKey));
    }

    /// <summary>
    /// Compares two X25519 identity public keys lexicographically (unsigned byte
    /// order). Returns a negative value when <paramref name="a"/> sorts before
    /// <paramref name="b"/>, zero when equal, and a positive value otherwise.
    /// This is the same ordering used by <see cref="ForOneToOne"/>, so a caller
    /// can decide the deterministic initiator role: the peer with the smaller
    /// sorted X25519 identity key initiates the 1:1 session.
    /// </summary>
    public static int CompareKeys(byte[] a, byte[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return LexicographicCompare(a, b);
    }

    private static int LexicographicCompare(byte[] a, byte[] b)
    {
        var len = Math.Min(a.Length, b.Length);
        for (var i = 0; i < len; i++)
        {
            var cmp = a[i].CompareTo(b[i]); // byte is unsigned: 0..255
            if (cmp != 0)
                return cmp;
        }

        return a.Length.CompareTo(b.Length);
    }
}
