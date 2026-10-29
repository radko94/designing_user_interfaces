namespace SecureMesh.Core.Domain;

/// <summary>
/// The transport seam that a Bluetooth Low Energy (or any peer-to-peer)
/// implementation will satisfy. It delivers raw, already-framed byte payloads
/// from the currently connected peer; the domain layer above it is responsible
/// for serializing/deserializing <see cref="WireMessage"/> frames.
/// </summary>
public interface ITransport : IDisposable
{
    /// <summary>Raw framed bytes received from a peer.</summary>
    event Action<byte[]>? MessageReceived;

    /// <summary>Raised with the peer id when a peer connects.</summary>
    event Action<string>? PeerConnected;

    /// <summary>Raised with the peer id when a peer disconnects.</summary>
    event Action<string>? PeerDisconnected;

    Task StartAsync();

    Task StopAsync();

    /// <summary>Sends a raw framed payload to the currently-connected peer.</summary>
    Task SendAsync(byte[] payload);
}
