using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecureMesh.Core.Domain;

/// <summary>A single recipient's authenticated ciphertext on the group wire.</summary>
public sealed class GroupWireRecipient
{
    /// <summary>Sender-local contact id of the member this copy is encrypted to.</summary>
    public int RecipientContactId { get; set; }

    public byte[] Nonce { get; set; } = Array.Empty<byte>();

    public byte[] Ciphertext { get; set; } = Array.Empty<byte>();

    public byte[] Tag { get; set; } = Array.Empty<byte>();

    public byte[] Signature { get; set; } = Array.Empty<byte>();

    public MessageEnvelope ToEnvelope(byte[] senderPublic) =>
        new(senderPublic, Nonce, Ciphertext, Tag, Signature);
}

/// <summary>
/// The framed group-message payload sent over <see cref="ITransport"/>. It carries
/// the stable group key, the sender's per-sender sequence number, the sender's
/// Ed25519 public key, and one authenticated ciphertext per recipient — each
/// encrypted under that recipient's own pairwise session key. A recipient can only
/// decrypt the copy encrypted with their own key; the others fail authentication.
/// A first (prekey) message additionally carries the sender's one-time EK and
/// identity public keys so not-yet-established members can derive their pairwise
/// session. Group frames never use <see cref="FrameType.Hello"/> (identity hello
/// is a 1:1-only exchange), but the discriminator is present for wire uniformity.
/// </summary>
public sealed class GroupWireMessage
{
    /// <summary>Stable cross-device group identifier used for routing.</summary>
    public string GroupKey { get; set; } = "";

    /// <summary>The sender's per-sender sequence number (bound into the context).</summary>
    public long Seq { get; set; }

    public byte[] SenderPublic { get; set; } = Array.Empty<byte>();

    public List<GroupWireRecipient> Recipients { get; set; } = new();

    /// <summary>The frame kind. Omitted on the wire when it is <see cref="FrameType.Message"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public FrameType FrameType { get; set; } = FrameType.Message;

    /// <summary>True when this is a prekey (first) message establishing pairwise sessions.</summary>
    [JsonIgnore]
    public bool IsPrekey => FrameType == FrameType.Prekey;

    /// <summary>The sender's one-time X25519 EK public key (prekey messages only).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? SenderEkPublic { get; set; }

    /// <summary>The sender's long-term X25519 identity public key (prekey messages only).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? SenderIkPublic { get; set; }

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this);

    public static GroupWireMessage Deserialize(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return JsonSerializer.Deserialize<GroupWireMessage>(bytes)
            ?? throw new InvalidDataException("Could not deserialize a GroupWireMessage.");
    }
}
