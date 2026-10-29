using System.Text.Json;

namespace SecureMesh.Core.Domain;

/// <summary>An inclusive range of message sequence numbers [Start, End].</summary>
public readonly record struct SeqRange(long Start, long End);

/// <summary>
/// A sync payload produced by one peer and applied by another. It carries the
/// encrypted messages the peer is missing, keyed by the conversation id.
/// </summary>
public sealed class SyncPayload
{
    public int ConversationId { get; set; }

    public List<Message> Messages { get; set; } = new();

    /// <summary>
    /// Per-member ciphertexts for group messages in this conversation. Carried
    /// so a member who missed a group message can catch up and decrypt their own
    /// copy; empty for 1:1 conversations.
    /// </summary>
    public List<GroupMessageEnvelope> GroupEnvelopes { get; set; } = new();

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this);

    public static SyncPayload Deserialize(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return JsonSerializer.Deserialize<SyncPayload>(bytes)
            ?? throw new InvalidDataException("Could not deserialize a SyncPayload.");
    }
}
