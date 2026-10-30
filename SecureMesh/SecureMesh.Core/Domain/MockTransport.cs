namespace SecureMesh.Core.Domain;

/// <summary>
/// An in-memory <see cref="ITransport"/> used by the desktop integration tests
/// and as a reference for the future BLE implementation. Two instances are
/// wired together with <see cref="Connect"/> (or <see cref="CreatePair"/>);
/// <see cref="SendAsync"/> synchronously delivers to the connected peer.
/// </summary>
public sealed class MockTransport : ITransport
{
    private readonly object _sync = new();
    private MockTransport? _peer;

    public MockTransport(string peerId) => PeerId = peerId;

    /// <summary>Stable identifier for this transport endpoint (e.g. a device name).</summary>
    public string PeerId { get; }

    public event Action<byte[]>? MessageReceived;
    public event Action<string>? PeerConnected;
    public event Action<string>? PeerDisconnected;

    /// <summary>The mock transport never searches; the event is never raised.</summary>
    public bool IsSearching => false;

    public event Action<bool>? SearchingChanged;

    /// <summary>The last payload passed to <see cref="SendAsync"/> (defensive copy).</summary>
    public byte[]? LastSentPayload { get; private set; }

    public int SentCount { get; private set; }

    public Task StartAsync() => Task.CompletedTask;

    public Task StopAsync()
    {
        Disconnect();
        return Task.CompletedTask;
    }

    public Task SendAsync(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        MockTransport? peer;
        lock (_sync)
        {
            peer = _peer;
            LastSentPayload = (byte[])payload.Clone();
            SentCount++;
        }

        if (peer is null)
            throw new InvalidOperationException($"Transport '{PeerId}' has no connected peer.");

        peer.Deliver(payload);
        return Task.CompletedTask;
    }

    /// <summary>Wires this transport to <paramref name="peer"/> in both directions.</summary>
    public void Connect(MockTransport peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        if (ReferenceEquals(peer, this))
            throw new InvalidOperationException("A transport cannot be connected to itself.");

        lock (_sync) { _peer = peer; }
        lock (peer._sync) { peer._peer = this; }

        PeerConnected?.Invoke(peer.PeerId);
        peer.PeerConnected?.Invoke(PeerId);
    }

    public void Disconnect()
    {
        MockTransport? other;
        lock (_sync)
        {
            other = _peer;
            _peer = null;
        }

        if (other is null)
            return;

        lock (other._sync) { other._peer = null; }

        PeerDisconnected?.Invoke(other.PeerId);
        other.PeerDisconnected?.Invoke(PeerId);
    }

    /// <summary>Creates two transports already wired together.</summary>
    public static (MockTransport Left, MockTransport Right) CreatePair(
        string leftId = "left", string rightId = "right")
    {
        var left = new MockTransport(leftId);
        var right = new MockTransport(rightId);
        left.Connect(right);
        return (left, right);
    }

    private void Deliver(byte[] payload) => MessageReceived?.Invoke(payload);

    public void Dispose() => Disconnect();
}
