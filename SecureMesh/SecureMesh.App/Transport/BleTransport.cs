using SecureMesh.Core.Domain;
using Shiny;
using Shiny.BluetoothLE;
using Shiny.BluetoothLE.Hosting;
using Microsoft.Maui.ApplicationModel;
// Both Shiny.BluetoothLE and Shiny.BluetoothLE.Hosting export an "IPeripheral"
// type; alias the central-role one to keep the two roles unambiguous.
using BlePeripheral = Shiny.BluetoothLE.IPeripheral;
using HostPeripheral = Shiny.BluetoothLE.Hosting.IPeripheral;

namespace SecureMesh.App.Transport;

/// <summary>
/// Android Bluetooth Low Energy implementation of <see cref="ITransport"/> built
/// on Shiny. Every device runs BOTH roles simultaneously so that two phones can
/// form a symmetric peer-to-peer link without a designated "server":
///
///  * Peripheral role (Shiny.BluetoothLE.Hosting) advertises the SecureMesh
///    service and exposes two GATT characteristics:
///      - RX (write): a remote central writes message fragments here; we
///        reassemble them with <see cref="BleMessageReassembler"/> and raise
///        <see cref="MessageReceived"/>.
///      - TX (notify): we notify fragments to the subscribed central.
///
///  * Central role (Shiny.BluetoothLE) scans for peers advertising the service,
///    connects, subscribes to the peer's TX characteristic (reassembling its
///    notifications), and sends by writing fragments to the peer's RX
///    characteristic.
///
/// Messages are length-prefixed and fragmented to fit the negotiated MTU using
/// Shiny's <see cref="BleMessageFraming"/> / <see cref="BleMessageReassembler"/>.
/// The central scan is a self-healing loop: Android silently stops a scan after
/// ~30 minutes (Shiny surfaces this as the scan observer completing), and Shiny
/// can also error the observer, so every completion/error is treated as a signal
/// to restart the scan with bounded backoff. After a peer disconnects we
/// re-enter the scan loop so a long-running session can always rediscover a peer.
/// </summary>
public sealed class BleTransport : ITransport
{
    private const string ServiceUuid = "6a1f4d10-1f8a-4f2a-9c4b-000000000001";
    private const string RxCharacteristicUuid = "6a1f4d10-1f8a-4f2a-9c4b-000000000002";
    private const string TxCharacteristicUuid = "6a1f4d10-1f8a-4f2a-9c4b-000000000003";
    private const int MaxMessageBytes = 64 * 1024;

    // Shiny's IPeripheral.Mtu already reports the *usable* payload per GATT
    // write (negotiated ATT MTU minus the 3-byte ATT header); before any MTU is
    // negotiated it reports 20 (not 23). When Mtu <= 0 (no link/unknown) fall
    // back to that same 20-byte default payload — do NOT use 23, which would
    // overrun the write by 3 bytes.
    private const int DefaultPayloadSize = 20;
    private const int WriteTimeoutMs = 5000;

    // Bounded host-setup retry: the GATT server/advertiser occasionally fails to
    // start while the adapter is still coming up.
    private const int HostSetupMaxAttempts = 3;
    private const int HostSetupRetryDelayMs = 1000;

    // Bounded scan-restart backoff. A normal scan expiry/completion restarts
    // after the base delay; repeated *subscribe* failures back off exponentially
    // up to the cap, so a dead adapter cannot spin a tight loop.
    private const int ScanRetryBaseDelayMs = 1000;
    private const int ScanRetryMaxDelayMs = 30_000;
    private const int ScanRetryMaxBackoffShift = 4; // base << 4 => 16s, then capped

    private readonly IBleManager _bleManager;
    private readonly IBleHostingManager _hostingManager;

    private readonly object _sync = new();
    private bool _started;
    private bool _isSearching;

    // Central role state.
    private IDisposable? _scanSubscription;
    private BlePeripheral? _centralPeer;
    private IDisposable? _centralNotifySubscription;
    private IDisposable? _centralStatusSubscription;
    private BleMessageReassembler? _centralReassembler;
    private int _scanFailures;
    private bool _scanRestartPending;

    // Peripheral (host) role state.
    private IGattCharacteristic? _rxCharacteristic;
    private IGattCharacteristic? _txCharacteristic;
    private BleMessageReassembler? _hostReassembler;
    private readonly List<HostPeripheral> _hostCentrals = new();

    public event Action<byte[]>? MessageReceived;
    public event Action<string>? PeerConnected;
    public event Action<string>? PeerDisconnected;
    public event Action<bool>? SearchingChanged;

    /// <inheritdoc />
    public bool IsSearching
    {
        get { lock (_sync) return _isSearching; }
    }

    public BleTransport(IBleManager bleManager, IBleHostingManager hostingManager)
    {
        _bleManager = bleManager;
        _hostingManager = hostingManager;
    }

    public async Task StartAsync()
    {
        lock (_sync)
        {
            if (_started)
                return;
            _started = true;
        }

        // Request runtime permissions; never crash if denied/unsupported.
        try { _ = await _bleManager.RequestAccessAsync(); }
        catch (Exception ex) { Log($"request central access failed: {ex.Message}"); }
        try { _ = await _hostingManager.RequestAccess(advertise: true, connect: true); }
        catch (Exception ex) { Log($"request host access failed: {ex.Message}"); }

        await SetupHostAsync();
        await StartScanAsync();
    }

    public async Task StopAsync()
    {
        BlePeripheral? peer;
        lock (_sync)
        {
            if (!_started)
                return;
            _started = false;
            peer = _centralPeer;
        }

        StopScanLoop();
        DisposeCentralPeerState();

        try { peer?.CancelConnection(); }
        catch (Exception ex) { Log($"cancel connection failed: {ex.Message}"); }
        try { _hostingManager.StopAdvertising(); }
        catch (Exception ex) { Log($"stop advertising failed: {ex.Message}"); }

        lock (_sync)
        {
            _hostCentrals.Clear();
            _hostReassembler = null;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Sends a fully-framed payload. Prefers the central path (write to the
    /// remote RX characteristic) and falls back to the host path (notify the
    /// subscribed central on TX).
    /// </summary>
    public async Task SendAsync(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        BlePeripheral? central;
        HostPeripheral[] centrals;
        IGattCharacteristic? tx;
        lock (_sync)
        {
            central = _centralPeer;
            tx = _txCharacteristic;
            centrals = _hostCentrals.ToArray();
        }

        if (central is not null && central.Status == ConnectionState.Connected)
        {
            var mtu = central.Mtu > 0 ? central.Mtu : DefaultPayloadSize;
            foreach (var fragment in Frame(payload, mtu))
            {
                await central.WriteCharacteristicAsync(
                    ServiceUuid,
                    RxCharacteristicUuid,
                    fragment,
                    withResponse: true,
                    CancellationToken.None,
                    WriteTimeoutMs);
            }
            return;
        }

        if (tx is not null && centrals.Length > 0)
        {
            var mtu = centrals[0].Mtu > 0 ? centrals[0].Mtu : DefaultPayloadSize;
            foreach (var fragment in Frame(payload, mtu))
            {
                await tx.Notify(fragment, centrals);
            }
            return;
        }

        throw new InvalidOperationException("Няма свързан партньор по Bluetooth.");
    }

    // ---- Peripheral / host role --------------------------------------------

    private async Task SetupHostAsync()
    {
        for (var attempt = 1; attempt <= HostSetupMaxAttempts; attempt++)
        {
            try
            {
                await _hostingManager.AddService(ServiceUuid, primary: true, service =>
                {
                    _rxCharacteristic = service.AddCharacteristic(RxCharacteristicUuid, ch =>
                    {
                        ch.SetWrite(OnCentralWrite, WriteOptions.Write);
                    });

                    _txCharacteristic = service.AddCharacteristic(TxCharacteristicUuid, ch =>
                    {
                        ch.SetNotification(OnCentralSubscription, NotificationOptions.Notify);
                    });
                });

                await _hostingManager.StartAdvertising(new AdvertisementOptions(
                    LocalName: "SecureMesh",
                    ServiceUuids: new[] { ServiceUuid }));

                return;
            }
            catch (Exception ex)
            {
                Log($"host setup failed (attempt {attempt}/{HostSetupMaxAttempts}): {ex.Message}");
                if (attempt < HostSetupMaxAttempts)
                    await Task.Delay(HostSetupRetryDelayMs);
            }
        }
    }

    private Task OnCentralWrite(WriteRequest request)
    {
        try
        {
            BleMessageReassembler? reassembler;
            lock (_sync)
            {
                _hostReassembler ??= new BleMessageReassembler(MaxMessageBytes);
                reassembler = _hostReassembler;
            }

            var status = reassembler.Push(request.Data, out var message);
            switch (status)
            {
                case BleMessageFrameResult.Complete:
                    lock (_sync) { _hostReassembler = null; }
                    if (message is not null)
                    {
                        var complete = (byte[])message.Clone();
                        MainThread.BeginInvokeOnMainThread(() => MessageReceived?.Invoke(complete));
                    }
                    break;
                case BleMessageFrameResult.Malformed:
                case BleMessageFrameResult.OutOfSequence:
                case BleMessageFrameResult.TooLarge:
                    lock (_sync) { _hostReassembler = null; }
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"central write handler failed: {ex.Message}");
            lock (_sync) { _hostReassembler = null; }
        }

        if (request.IsReplyNeeded)
            request.Respond(GattState.Success);

        return Task.CompletedTask;
    }

    private Task OnCentralSubscription(CharacteristicSubscription subscription)
    {
        lock (_sync)
        {
            if (subscription.IsSubscribing)
            {
                if (!_hostCentrals.Contains(subscription.Peripheral))
                    _hostCentrals.Add(subscription.Peripheral);
            }
            else
            {
                _hostCentrals.Remove(subscription.Peripheral);
                // Abandon any partial message the departing central was mid-way through.
                _hostReassembler = null;
            }
        }

        var peerId = subscription.Peripheral.Uuid;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (subscription.IsSubscribing)
                PeerConnected?.Invoke(peerId);
            else
                PeerDisconnected?.Invoke(peerId);
        });

        return Task.CompletedTask;
    }

    // ---- Central role ------------------------------------------------------

    private async Task StartScanAsync()
    {
        // Verify we can scan before subscribing; never tight-loop on a permanent
        // permission denial (the user has to grant it). Transient scan failures
        // are handled with bounded backoff inside the subscribe path below.
        AccessState access;
        try
        {
            access = await _bleManager.RequestAccessAsync();
        }
        catch (Exception ex)
        {
            Log($"request scan access failed: {ex.Message}");
            SetSearching(false);
            return;
        }

        if (access != AccessState.Available)
        {
            Log($"scan access not available ({access})");
            SetSearching(false);
            return;
        }

        SubscribeScan();
    }

    /// <summary>
    /// Starts (or restarts) the central scan. Subscribing to
    /// <see cref="IBleManager.Scan(ScanConfig)"/> returns an observer whose
    /// OnCompleted fires when Android's ~30-minute scan window expires and whose
    /// OnError fires when the scan faults; both are routed to a bounded restart
    /// instead of being silently swallowed.
    /// </summary>
    private void SubscribeScan()
    {
        bool shouldScan;
        lock (_sync)
        {
            shouldScan = _started && _centralPeer is not { Status: ConnectionState.Connected };
        }

        if (!shouldScan)
        {
            SetSearching(false);
            return;
        }

        IDisposable? subscription = null;
        try
        {
            SetSearching(true);
            subscription = _bleManager
                .Scan(new ScanConfig(ServiceUuids: new[] { ServiceUuid }))
                .Subscribe(new SimpleObserver<ScanResult>(
                    OnScanResult,
                    ex =>
                    {
                        Log($"scan observer error: {ex.Message}");
                        ScheduleScanRestart();
                    },
                    () =>
                    {
                        Log("scan observer completed (Android scan window expired); restarting");
                        ScheduleScanRestart();
                    }));

            lock (_sync)
            {
                _scanSubscription?.Dispose();
                _scanSubscription = subscription;
                _scanRestartPending = false;
                _scanFailures = 0; // a live scan resets the backoff window
            }
        }
        catch (Exception ex)
        {
            Log($"scan subscribe failed: {ex.Message}");
            subscription?.Dispose();
            ScheduleScanRestart();
        }
    }

    private void OnScanResult(ScanResult result)
    {
        try
        {
            // The scan was requested with our service UUID, but some Android
            // stacks ignore the filter and deliver every advertisement, so
            // accept a device only when it either advertises our service or the
            // advertisement carries no service list at all.
            var uuids = result.AdvertisementData?.ServiceUuids;
            if (uuids is not null &&
                !uuids.Any(u => string.Equals(u, ServiceUuid, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            // ConnectCentralAsync atomically claims the single active peer slot,
            // so duplicate advertisements for the same device are harmless.
            _ = ConnectCentralAsync(result.Peripheral);
        }
        catch (Exception ex)
        {
            Log($"scan result failed: {ex.Message}");
        }
    }

    private async Task ConnectCentralAsync(BlePeripheral peripheral)
    {
        IDisposable? statusSub = null;
        try
        {
            lock (_sync)
            {
                if (_centralPeer is not null)
                    return; // a connect is already in flight or established
                ClearCentralSubscriptions();
                _centralPeer = peripheral;
            }

            statusSub = peripheral.WhenStatusChanged().Subscribe(new SimpleObserver<ConnectionState>(
                state =>
                {
                    if (state == ConnectionState.Connected)
                        OnCentralConnected(peripheral);
                    else if (state == ConnectionState.Disconnected)
                        OnCentralDisconnected(peripheral);
                },
                ex =>
                {
                    Log($"central status stream error: {ex.Message}");
                    OnCentralDisconnected(peripheral);
                }));

            lock (_sync)
            {
                _centralStatusSubscription = statusSub;
            }

            await peripheral.ConnectAsync(new ConnectionConfig(AutoConnect: false), CancellationToken.None, null);

            // Force service discovery, then subscribe to the peer's TX
            // characteristic (peripheral -> central notifications).
            await peripheral.GetServicesAsync(CancellationToken.None);
            await peripheral.GetCharacteristicAsync(ServiceUuid, TxCharacteristicUuid, CancellationToken.None);

            var reassembler = new BleMessageReassembler(MaxMessageBytes);
            var notifySub = peripheral
                .NotifyCharacteristic(ServiceUuid, TxCharacteristicUuid, useIndicationsIfAvailable: false)
                .Subscribe(new SimpleObserver<BleCharacteristicResult>(OnCentralNotification, OnCentralNotifyError));

            lock (_sync)
            {
                _centralReassembler = reassembler;
                _centralNotifySubscription = notifySub;
            }

            // The link is up: stop scanning to save battery and reflect IsSearching.
            // (PeerConnected was raised by the status-stream handler.)
            StopScanLoop();
        }
        catch (Exception ex)
        {
            Log($"connect failed: {ex.Message}");
            statusSub?.Dispose();
            OnCentralDisconnected(peripheral);
        }
    }

    private void OnCentralConnected(BlePeripheral peripheral)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_centralPeer, peripheral))
                return;
        }

        MainThread.BeginInvokeOnMainThread(() => PeerConnected?.Invoke(peripheral.Uuid));
        StopScanLoop();
    }

    private void OnCentralDisconnected(BlePeripheral peripheral)
    {
        bool isCurrent;
        lock (_sync)
        {
            isCurrent = ReferenceEquals(_centralPeer, peripheral);
            if (isCurrent)
            {
                ClearCentralSubscriptions();
                _centralPeer = null;
            }
        }

        if (!isCurrent)
            return;

        MainThread.BeginInvokeOnMainThread(() => PeerDisconnected?.Invoke(peripheral.Uuid));

        // Re-enter the scan loop to (re)establish the link.
        ScheduleScanRestart();
    }

    private void OnCentralNotifyError(Exception error)
    {
        Log($"central notify stream error: {error.Message}");

        BlePeripheral? peer;
        lock (_sync)
        {
            _centralReassembler = null;
            peer = _centralPeer;
        }

        // A dead notification stream means we can no longer receive on this
        // link; tear it down so the scan loop can reconnect cleanly.
        if (peer is not null)
            OnCentralDisconnected(peer);
    }

    private void OnCentralNotification(BleCharacteristicResult result)
    {
        try
        {
            if (result.Event != BleCharacteristicEvent.Notification)
                return;
            if (result.Data is not { Length: > 0 })
                return;

            BleMessageReassembler? reassembler;
            lock (_sync)
            {
                _centralReassembler ??= new BleMessageReassembler(MaxMessageBytes);
                reassembler = _centralReassembler;
            }

            var status = reassembler.Push(result.Data, out var message);
            switch (status)
            {
                case BleMessageFrameResult.Complete:
                    lock (_sync) { _centralReassembler = null; }
                    if (message is not null)
                    {
                        var complete = (byte[])message.Clone();
                        MainThread.BeginInvokeOnMainThread(() => MessageReceived?.Invoke(complete));
                    }
                    break;
                case BleMessageFrameResult.Malformed:
                case BleMessageFrameResult.OutOfSequence:
                case BleMessageFrameResult.TooLarge:
                    lock (_sync) { _centralReassembler = null; }
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"central notification handler failed: {ex.Message}");
            lock (_sync) { _centralReassembler = null; }
        }
    }

    // ---- Scan loop helpers -------------------------------------------------

    private void ScheduleScanRestart()
    {
        lock (_sync)
        {
            if (!_started || _scanRestartPending)
                return;
            _scanRestartPending = true;
        }

        _ = RestartScanAfterDelayAsync();
    }

    private async Task RestartScanAfterDelayAsync()
    {
        int delayMs;
        lock (_sync)
        {
            _scanFailures++;
            var shift = Math.Min(_scanFailures - 1, ScanRetryMaxBackoffShift);
            delayMs = Math.Min(ScanRetryMaxDelayMs, ScanRetryBaseDelayMs << shift);
        }

        await Task.Delay(delayMs);

        lock (_sync)
        {
            if (!_started)
            {
                _scanRestartPending = false;
                return;
            }
            _scanRestartPending = false;
        }

        SubscribeScan();
    }

    private void StopScanLoop()
    {
        IDisposable? subscription;
        lock (_sync)
        {
            subscription = _scanSubscription;
            _scanSubscription = null;
            _scanRestartPending = false;
        }

        subscription?.Dispose();
        try { _bleManager.StopScan(); }
        catch (Exception ex) { Log($"stop scan failed: {ex.Message}"); }

        SetSearching(false);
    }

    // ---- Central peer state helpers ---------------------------------------

    /// <summary>Disposes the central notify/status subscriptions and resets the
    /// reassembler. Callers must hold <see cref="_sync"/>.</summary>
    private void ClearCentralSubscriptions()
    {
        _centralNotifySubscription?.Dispose();
        _centralNotifySubscription = null;
        _centralStatusSubscription?.Dispose();
        _centralStatusSubscription = null;
        _centralReassembler = null;
    }

    private void DisposeCentralPeerState()
    {
        lock (_sync)
        {
            ClearCentralSubscriptions();
            _centralPeer = null;
        }
    }

    // ---- Searching state ---------------------------------------------------

    private void SetSearching(bool searching)
    {
        bool changed;
        lock (_sync)
        {
            changed = _isSearching != searching;
            _isSearching = searching;
        }

        if (changed)
            MainThread.BeginInvokeOnMainThread(() => SearchingChanged?.Invoke(searching));
    }

    // ---- Framing helpers ---------------------------------------------------

    private static IReadOnlyList<byte[]> Frame(byte[] payload, int mtu) =>
        BleMessageFraming.Encode(payload, mtu);

    private static void Log(string message) =>
        System.Diagnostics.Debug.WriteLine($"[BleTransport] {message}");

    public void Dispose()
    {
        BlePeripheral? peer;
        lock (_sync)
        {
            _started = false;
            peer = _centralPeer;
        }

        StopScanLoop();
        DisposeCentralPeerState();

        try { peer?.CancelConnection(); }
        catch (Exception ex) { Log($"cancel connection failed: {ex.Message}"); }
        try { _hostingManager.StopAdvertising(); }
        catch (Exception ex) { Log($"stop advertising failed: {ex.Message}"); }

        lock (_sync)
        {
            _hostCentrals.Clear();
            _hostReassembler = null;
        }
    }

    /// <summary>BCL-only <see cref="IObserver{T}"/> so we avoid depending on Rx LINQ.</summary>
    private sealed class SimpleObserver<T> : IObserver<T>
    {
        private readonly Action<T> _onNext;
        private readonly Action<Exception> _onError;
        private readonly Action _onCompleted;

        public SimpleObserver(Action<T> onNext, Action<Exception>? onError = null, Action? onCompleted = null)
        {
            _onNext = onNext;
            _onError = onError ?? (_ => { });
            _onCompleted = onCompleted ?? (() => { });
        }

        public void OnNext(T value) => _onNext(value);
        public void OnError(Exception error) => _onError(error);
        public void OnCompleted() => _onCompleted();
    }
}
