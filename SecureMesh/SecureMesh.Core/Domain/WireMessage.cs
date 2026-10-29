using System.Text.Json;

namespace SecureMesh.Core.Domain;

/// <summary>
/// The framed wire payload sent over <see cref="ITransport"/>. It carries the
/// routing metadata (conversation id and sequence number) in the clear together
/// with the signed, authenticated <see cref="MessageEnvelope"/> fields. The
/// plaintext is never present.
/// </summary>
public sealed class WireMessage
{
    public int ConversationId { get; set; }

    public long Seq { get; set; }

    public byte[] SenderPublic { get; set; } = Array.Empty<byte>();

    public byte[] Nonce { get; set; } = Array.Empty<byte>();

    public byte[] Ciphertext { get; set; } = Array.Empty<byte>();

    public byte[] Tag { get; set; } = Array.Empty<byte>();

    public byte[] Signature { get; set; } = Array.Empty<byte>();

    public static WireMessage FromEnvelope(int conversationId, long seq, MessageEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return new WireMessage
        {
            ConversationId = conversationId,
            Seq = seq,
            SenderPublic = envelope.SenderPublic,
            Nonce = envelope.Nonce,
            Ciphertext = envelope.Ciphertext,
            Tag = envelope.Tag,
            Signature = envelope.Signature,
        };
    }

    public MessageEnvelope ToEnvelope() =>
        new MessageEnvelope(SenderPublic, Nonce, Ciphertext, Tag, Signature);

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this);

    public static WireMessage Deserialize(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return JsonSerializer.Deserialize<WireMessage>(bytes)
            ?? throw new InvalidDataException("Could not deserialize a WireMessage.");
    }
}
