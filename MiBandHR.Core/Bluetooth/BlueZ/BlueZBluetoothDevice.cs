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
    private bool _isConnected;

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
    /// <see cref="Disconnect"/> or the D-Bus <c>Connected</c> watcher.
    /// </summary>
    public bool IsConnected => _isConnected;

    /// <summary>
    /// Connects to the device via BlueZ <b>without pairing</b>, then waits for
    /// BlueZ to report the device connected and its GATT services resolved.
    /// </summary>
    public async Task ConnectAsync() {
        if (await BlueZNativeInterop.GetConnectedAsync(_device)) {
            _isConnected = true;
            return;
        }

        BluetoothLog.Info($"Connecting to {Name} ({Id})...");
        try {
            await BlueZNativeInterop.ConnectNoPairingAsync(_device, ConnectTimeout);
            _isConnected = true;
            _connectedWatcher ??= await _device.WatchPropertiesAsync(value => {
                if (!value.Changed.Any(x => x.Key == "Connected" && x.Value is bool connected && !connected)) return;
                _isConnected = false;
                Disconnected?.Invoke(this, EventArgs.Empty);
            });
            BluetoothLog.Success($"Connected to {Name}.");
        } catch (Exception e) {
            _isConnected = false;
            BluetoothLog.Error($"Connection to {Name} failed: {e.Message}");
            throw;
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

    public void Disconnect() {
        _connectedWatcher?.Dispose();
        _connectedWatcher = null;
        _isConnected = false;

        // The BlueZ disconnect is a blocking D-Bus round-trip. Run it on a
        // background thread so calling Disconnect() from the UI thread never
        // freezes the window while the D-Bus call is in flight.
        _ = Task.Run(() => {
            try {
                _ = BlueZNativeInterop.Disconnect(_device);
            } catch {
                // Disconnect is best-effort; the link may already be down.
            }
        });
    }
}
#endif