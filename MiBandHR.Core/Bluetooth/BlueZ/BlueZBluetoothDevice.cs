#if !WINDOWS
using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;
using Tmds.DBus;

namespace MiBandHR.Core.Bluetooth.BlueZ;

/// <summary>
/// Linux (BlueZ) specific <see cref="IBluetoothDevice"/> backed directly by a
/// <see cref="Linux.Bluetooth.Device"/> D-Bus proxy.
///
/// Connects <b>without pairing</b>: the Mi Band authentication is done at the
/// application layer (AES challenge/response), so forcing BlueZ to pair would
/// only trigger an extra "confirm pairing" prompt on the band without adding
/// anything. The native device is obtained directly from BlueZ (no InTheHand
/// involvement on Linux).
/// </summary>
public sealed class BlueZBluetoothDevice : IBluetoothDevice {
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private readonly Device _device;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _connectionStateLock = new();
    private volatile bool _isConnected;
    private int _watcherGeneration;
    private int _disconnectVersion;

    public event EventHandler? Disconnected;
    private IDisposable? _connectedWatcher;

    public BlueZBluetoothDevice(Device device, string id, string name) {
        _device = device;
        Id = id;
        Name = name;
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>
    /// True while a connection is believed to be established.
    ///
    /// This is a cached flag rather than a live D-Bus read: <see cref="IsConnected"/>
    /// is a synchronous property that may be polled from the UI thread, and a
    /// blocking <c>GetAllAsync</c> D-Bus round-trip there would freeze the UI.
    /// The flag is set after <see cref="ConnectAsync"/> succeeds and cleared by
    /// <see cref="DisconnectAsync"/> or the D-Bus <c>Connected</c> watcher.
    /// </summary>
    public bool IsConnected => _isConnected;

    /// <summary>
    /// Connects to the device via BlueZ <b>without pairing</b>, then waits for
    /// BlueZ to report the device connected and its GATT services resolved.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default) {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            var wasConnected = _isConnected;
            var disconnectVersion = Volatile.Read(ref _disconnectVersion);
            if (!wasConnected) BluetoothLog.Info($"Connecting to {Name} ({Id})...");
            await BlueZNativeInterop.ConnectNoPairingAsync(_device, ConnectTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (_connectedWatcher == null) {
                var generation = Interlocked.Increment(ref _watcherGeneration);
                _connectedWatcher = await _device.WatchPropertiesAsync(value => {
                    if (!value.Changed.Any(x => x.Key == "Connected" && x.Value is bool connected && !connected)) return;
                    bool notify;
                    lock (_connectionStateLock) {
                        if (generation != _watcherGeneration) return;
                        _disconnectVersion++;
                        notify = _isConnected;
                        _isConnected = false;
                    }
                    if (notify) Disconnected?.Invoke(this, EventArgs.Empty);
                }).ConfigureAwait(false);
            }
            if (!await BlueZNativeInterop.GetReadyAsync(_device).ConfigureAwait(false)) {
                throw new InvalidOperationException("Device disconnected while establishing the connection.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (_connectionStateLock) {
                if (disconnectVersion != _disconnectVersion) {
                    throw new InvalidOperationException("Device disconnected while establishing the connection.");
                }
                _isConnected = true;
            }
            if (!wasConnected) BluetoothLog.Success($"Connected to {Name}.");
        } catch (Exception e) {
            await DisconnectCoreAsync().ConfigureAwait(false);
            if (e is not OperationCanceledException) {
                BluetoothLog.Error($"Connection to {Name} failed: {e.Message}");
            }
            throw;
        } finally {
            _lifecycleGate.Release();
        }
    }

    public async Task<IGattService?> GetPrimaryServiceAsync(Guid uuid) {
        await ConnectAsync();

        var service = await _device.GetServiceAsync(uuid.ToString());
        return service == null ? null : new BlueZGattService(service, uuid);
    }

    public async Task<IReadOnlyList<IGattService>> GetPrimaryServicesAsync() {
        await ConnectAsync();

        var services = await _device.GetServicesAsync();
        var result = new List<IGattService>();
        foreach (var service in services) {
            // Resolve each service UUID asynchronously here (rather than via a
            // blocking getter later) so the UI never waits on a D-Bus round-trip.
            var uuidString = await service.GetUUIDAsync();
            result.Add(new BlueZGattService(service, Guid.Parse(uuidString)));
        }

        return result;
    }

    public async Task DisconnectAsync() {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try {
            await DisconnectCoreAsync().ConfigureAwait(false);
        } finally {
            _lifecycleGate.Release();
        }
    }

    private async Task DisconnectCoreAsync() {
        lock (_connectionStateLock) {
            _isConnected = false;
            _watcherGeneration++;
        }
        try {
            _connectedWatcher?.Dispose();
        } finally {
            _connectedWatcher = null;
            await BlueZNativeInterop.Disconnect(_device).ConfigureAwait(false);
        }
    }
}
#endif
