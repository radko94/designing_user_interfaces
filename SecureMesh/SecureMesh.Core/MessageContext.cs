using System.Buffers.Binary;

namespace SecureMesh.Core;

/// <summary>
/// Deterministic context-binding bytes that bind a message envelope to a stable
/// channel id and the sender's per-sender sequence number. Both the sender and
/// the recipient compute the identical bytes (the sender from its own channel
/// id, the recipient from the same channel id derived from the shared public
/// keys or group key) and pass them as the associated data of the authenticated
/// envelope. This is what prevents cross-conversation confusion and
/// replay/reordering.
/// </summary>
public static class MessageContext
{
    /// <summary>
    /// Builds a context as the raw <paramref name="channelId"/> bytes followed by
    /// the little-endian 8-byte sequence number. For 1:1 channels the channel id
    /// is 4 bytes; for groups it is the 32-byte SHA-256 of the group key.
    /// </summary>
    public static byte[] Build(byte[] channelId, long seq)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        if (channelId.Length == 0)
            throw new ArgumentException("The channel id must not be empty.", nameof(channelId));

        var context = new byte[channelId.Length + 8];
        channelId.CopyTo(context, 0);
        BinaryPrimitives.WriteInt64LittleEndian(context.AsSpan(channelId.Length, 8), seq);
        return context;
    }

    /// <summary>
    /// Builds a context from a 4-byte little-endian channel id and a sequence
    /// number. Convenience overload kept for primitive tests and callers that
    /// already hold a 4-byte integer channel id.
    /// </summary>
    public static byte[] Build(int channelId, long seq)
    {
        var id = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(id, channelId);
        return Build(id, seq);
    }
}
