using System.Text.Json;

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
/// the stable group key, the sequence number, the sender's Ed25519 public key,
/// and one authenticated ciphertext per recipient — each encrypted under that
/// recipient's own pairwise session key. A recipient can only decrypt the copy
/// encrypted with their own key; the others fail authentication.
/// </summary>
public sealed class GroupWireMessage
{
    /// <summary>Stable cross-device group identifier used for routing.</summary>
    public string GroupKey { get; set; } = "";

    public long Seq { get; set; }

    public byte[] SenderPublic { get; set; } = Array.Empty<byte>();

    public List<GroupWireRecipient> Recipients { get; set; } = new();

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this);

    public static GroupWireMessage Deserialize(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return JsonSerializer.Deserialize<GroupWireMessage>(bytes)
            ?? throw new InvalidDataException("Could not deserialize a GroupWireMessage.");
    }
}
