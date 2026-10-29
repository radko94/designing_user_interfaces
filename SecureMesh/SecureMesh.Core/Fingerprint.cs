using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace SecureMesh.Core;

/// <summary>Deterministic safety numbers for out-of-band key verification.</summary>
public static class Fingerprint
{
    private const int DigitCount = 60;   // 12 groups of 5 digits
    private const int GroupSize = 5;

    /// <summary>
    /// Computes a deterministic, order-independent safety number from two
    /// public keys: the keys are sorted lexicographically before concatenation,
    /// hashed with SHA-256, and rendered as a grouped decimal string so both
    /// peers derive the same value regardless of argument order.
    /// </summary>
    public static string Compute(byte[] publicKeyA, byte[] publicKeyB)
    {
        ArgumentNullException.ThrowIfNull(publicKeyA);
        ArgumentNullException.ThrowIfNull(publicKeyB);

        byte[] first;
        byte[] second;
        if (LexicographicCompare(publicKeyA, publicKeyB) <= 0)
        {
            first = publicKeyA;
            second = publicKeyB;
        }
        else
        {
            first = publicKeyB;
            second = publicKeyA;
        }

        var concat = new byte[first.Length + second.Length];
        first.CopyTo(concat, 0);
        second.CopyTo(concat, first.Length);

        var hash = SHA256.HashData(concat);

        // Interpret the hash as a big-endian unsigned integer and render its
        // decimal form, padded/truncated to a fixed width.
        var value = new BigInteger(hash, isUnsigned: true, isBigEndian: true);
        var digits = value.ToString();
        if (digits.Length < DigitCount)
            digits = digits.PadLeft(DigitCount, '0');
        digits = digits.Substring(0, DigitCount);

        var sb = new StringBuilder(DigitCount + DigitCount / GroupSize);
        for (var i = 0; i < DigitCount; i += GroupSize)
        {
            if (i > 0)
                sb.Append(' ');
            sb.Append(digits, i, GroupSize);
        }

        return sb.ToString();
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
