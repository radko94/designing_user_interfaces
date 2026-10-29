using System.Text.Json;
using SecureMesh.Core;

namespace SecureMesh.App.Services;

/// <summary>
/// The QR payload exchanged when adding a contact. It carries the peer's
/// Ed25519 signing public key, X25519 key-agreement public key, and nickname.
/// The QR content is prefixed with a scheme marker so a scanner can reject
/// unrelated barcodes.
/// </summary>
public sealed class PeerQrPayload
{
    public const string Scheme = "securemesh";

    public string Nickname { get; set; } = "";
    public string SigningPublic { get; set; } = "";
    public string DhPublic { get; set; } = "";

    public static PeerQrPayload FromIdentity(string nickname, IdentityKeyPair identity) => new()
    {
        Nickname = nickname,
        SigningPublic = Convert.ToBase64String(identity.SigningPublicKey),
        DhPublic = Convert.ToBase64String(identity.DhPublicKey),
    };

    public string Encode() => $"{Scheme}:{JsonSerializer.Serialize(this)}";

    /// <summary>
    /// Parses a scanned string into a payload, returning <c>null</c> when the
    /// text is not a valid SecureMesh identity QR code.
    /// </summary>
    public static PeerQrPayload? TryDecode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var json = text;
        if (text.StartsWith(Scheme + ":", StringComparison.Ordinal))
            json = text[(Scheme.Length + 1)..];

        try
        {
            var payload = JsonSerializer.Deserialize<PeerQrPayload>(json);
            if (payload is null)
                return null;

            var signing = Convert.FromBase64String(payload.SigningPublic);
            var dh = Convert.FromBase64String(payload.DhPublic);
            if (signing.Length != IdentityKeyPair.Ed25519PublicKeyBytes)
                return null;
            if (dh.Length != IdentityKeyPair.X25519PublicKeyBytes)
                return null;
            if (string.IsNullOrWhiteSpace(payload.Nickname))
                payload.Nickname = "Непознат";

            return payload;
        }
        catch
        {
            return null;
        }
    }

    public byte[] SigningPublicKey => Convert.FromBase64String(SigningPublic);

    public byte[] DhPublicKey => Convert.FromBase64String(DhPublic);
}
