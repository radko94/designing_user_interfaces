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
/// Every BLE call is wrapped in try/catch so scan/connect failures degrade
/// gracefully instead of crashing the app.
/// </summary>
public sealed class BleTransport : ITransport
{
    private const string ServiceUuid = "6a1f4d10-1f8a-4f2a-9c4b-000000000001";
    private const string RxCharacteristicUuid = "6a1f4d10-1f8a-4f2a-9c4b-000000000002";
    private const string TxCharacteristicUuid = "6a1f4d10-1f8a-4f2a-9c4b-000000000003";
    private const int MaxMessageBytes = 64 * 1024;
    private const int DefaultMtu = 23;
    private const int WriteTimeoutMs = 5000;

    private readonly IBleManager _bleManager;
    private readonly IBleHostingManager _hostingManager;

    private readonly object _sync = new();
    private bool _started;

    // Central role state.
    private IDisposable? _scanSubscription;
    private BlePeripheral? _centralPeer;
    private IDisposable? _centralNotifySubscription;
    private BleMessageReassembler? _centralReassembler;

    // Peripheral (host) role state.
    private IGattCharacteristic? _rxCharacteristic;
    private IGattCharacteristic? _txCharacteristic;
    private BleMessageReassembler? _hostReassembler;
    private readonly List<HostPeripheral> _hostCentrals = new();

    public event Action<byte[]>? MessageReceived;
    public event Action<string>? PeerConnected;
    public event Action<string>? PeerDisconnected;

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
        try { _ = await _bleManager.RequestAccessAsync(); } catch { }
        try { _ = await _hostingManager.RequestAccess(advertise: true, connect: true); } catch { }

        await SetupHostAsync();
        await StartScanAsync();
    }

    public async Task StopAsync()
    {
        lock (_sync)
        {
            if (!_started)
                return;
            _started = false;
        }

        _scanSubscription?.Dispose();
        _scanSubscription = null;
        _centralNotifySubscription?.Dispose();
        _centralNotifySubscription = null;

        try { _bleManager.StopScan(); } catch { }
        try { _centralPeer?.CancelConnection(); } catch { }
        try { _hostingManager.StopAdvertising(); } catch { }

        lock (_sync)
        {
            _centralPeer = null;
            _hostCentrals.Clear();
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
            var mtu = central.Mtu > 0 ? central.Mtu : DefaultMtu;
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
            var mtu = centrals[0].Mtu > 0 ? centrals[0].Mtu : DefaultMtu;
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
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BleTransport] host setup failed: {ex.Message}");
        }
    }

    private Task OnCentralWrite(WriteRequest request)
    {
        try
        {
            _hostReassembler ??= new BleMessageReassembler(MaxMessageBytes);
            var status = _hostReassembler.Push(request.Data, out var message);
            switch (status)
            {
                case BleMessageFrameResult.Complete:
                    _hostReassembler = null;
                    if (message is not null)
                    {
                        var complete = (byte[])message.Clone();
                        MainThread.BeginInvokeOnMainThread(() => MessageReceived?.Invoke(complete));
                    }
                    break;
                case BleMessageFrameResult.Malformed:
                case BleMessageFrameResult.OutOfSequence:
                case BleMessageFrameResult.TooLarge:
                    _hostReassembler = null;
                    break;
            }
        }
        catch
        {
            _hostReassembler = null;
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
        try
        {
            var access = await _bleManager.RequestAccessAsync();
            if (access != AccessState.Available)
                return;
        }
        catch
        {
            return;
        }

        try
        {
            _scanSubscription = _bleManager
                .Scan(new ScanConfig(ServiceUuids: new[] { ServiceUuid }))
                .Subscribe(new SimpleObserver<ScanResult>(OnScanResult, _ => { }));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BleTransport] scan start failed: {ex.Message}");
        }
    }

    private void OnScanResult(ScanResult result)
    {
        try
        {
            lock (_sync)
            {
                if (_centralPeer is not null)
                    return;
            }

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

            _ = ConnectCentralAsync(result.Peripheral);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BleTransport] scan result failed: {ex.Message}");
        }
    }

    private async Task ConnectCentralAsync(BlePeripheral peripheral)
    {
        try
        {
            lock (_sync)
            {
                _centralPeer = peripheral;
            }

            peripheral.WhenStatusChanged().Subscribe(new SimpleObserver<ConnectionState>(
                state =>
                {
                    if (state == ConnectionState.Connected)
                    {
                        MainThread.BeginInvokeOnMainThread(() => PeerConnected?.Invoke(peripheral.Uuid));
                    }
                    else if (state == ConnectionState.Disconnected)
                    {
                        MainThread.BeginInvokeOnMainThread(() => PeerDisconnected?.Invoke(peripheral.Uuid));
                        lock (_sync)
                        {
                            if (ReferenceEquals(_centralPeer, peripheral))
                                _centralPeer = null;
                        }
                    }
                },
                _ => { }));

            await peripheral.ConnectAsync(new ConnectionConfig(AutoConnect: false), CancellationToken.None, null);

            // Force service discovery, then subscribe to the peer's TX
            // characteristic (peripheral -> central notifications).
            await peripheral.GetServicesAsync(CancellationToken.None);
            await peripheral.GetCharacteristicAsync(ServiceUuid, TxCharacteristicUuid, CancellationToken.None);

            _centralReassembler = new BleMessageReassembler(MaxMessageBytes);
            _centralNotifySubscription = peripheral
                .NotifyCharacteristic(ServiceUuid, TxCharacteristicUuid, useIndicationsIfAvailable: false)
                .Subscribe(new SimpleObserver<BleCharacteristicResult>(OnCentralNotification, _ => { }));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BleTransport] connect failed: {ex.Message}");
            lock (_sync)
            {
                if (ReferenceEquals(_centralPeer, peripheral))
                    _centralPeer = null;
            }
        }
    }

    private void OnCentralNotification(BleCharacteristicResult result)
    {
        try
        {
            if (result.Event != BleCharacteristicEvent.Notification)
                return;
            if (result.Data is not { Length: > 0 })
                return;

            _centralReassembler ??= new BleMessageReassembler(MaxMessageBytes);
            var status = _centralReassembler.Push(result.Data, out var message);
            switch (status)
            {
                case BleMessageFrameResult.Complete:
                    _centralReassembler = null;
                    if (message is not null)
                    {
                        var complete = (byte[])message.Clone();
                        MainThread.BeginInvokeOnMainThread(() => MessageReceived?.Invoke(complete));
                    }
                    break;
                case BleMessageFrameResult.Malformed:
                case BleMessageFrameResult.OutOfSequence:
                case BleMessageFrameResult.TooLarge:
                    _centralReassembler = null;
                    break;
            }
        }
        catch
        {
            _centralReassembler = null;
        }
    }

    // ---- Framing helpers ---------------------------------------------------

    private static IReadOnlyList<byte[]> Frame(byte[] payload, int mtu) =>
        BleMessageFraming.Encode(payload, mtu);

    public void Dispose()
    {
        _scanSubscription?.Dispose();
        _scanSubscription = null;
        _centralNotifySubscription?.Dispose();
        _centralNotifySubscription = null;

        try { _bleManager.StopScan(); } catch { }
        try { _centralPeer?.CancelConnection(); } catch { }
        try { _hostingManager.StopAdvertising(); } catch { }
    }

    /// <summary>BCL-only <see cref="IObserver{T}"/> so we avoid depending on Rx LINQ.</summary>
    private sealed class SimpleObserver<T> : IObserver<T>
    {
        private readonly Action<T> _onNext;
        private readonly Action<Exception> _onError;

        public SimpleObserver(Action<T> onNext, Action<Exception>? onError = null)
        {
            _onNext = onNext;
            _onError = onError ?? (_ => { });
        }

        public void OnNext(T value) => _onNext(value);
        public void OnError(Exception error) => _onError(error);
        public void OnCompleted() { }
    }
}
