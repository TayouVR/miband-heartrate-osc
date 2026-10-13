using System.Reflection;
using InTheHand.Bluetooth;
using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;
using Tmds.DBus;

namespace MiBandHR.Core.Bluetooth.InTheHand;

/// <summary>
/// Linux (BlueZ) specific <see cref="IBluetoothDevice"/> that wraps an
/// InTheHand <see cref="BluetoothDevice"/> but drives the connection and GATT
/// operations through the native BlueZ <c>Device</c> D-Bus proxy directly.
///
/// Unlike InTheHand's own Linux connect path, this implementation
/// <b>does not pair</b> the device before connecting. Pairing is neither
/// required nor desired for Mi Band heart rate monitoring, and forcing it
/// (as InTheHand does) can leave the connection unsettled — the root cause of
/// the <c>org.bluez.Error.NotConnected</c> failures seen with Mi Band 3.
/// It mirrors the connect behaviour of Salar.BluetoothLE's Linux backend
/// (connect → wait for Connected → wait for ServicesResolved), reimplemented
/// against the MIT-licensed <c>Linux.Bluetooth</c> library.
/// </summary>
public sealed class BlueZBluetoothDevice : IBluetoothDevice
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private readonly BluetoothDevice _inTheHandDevice;
    private Device? _nativeDevice; // Linux.Bluetooth.Device
    private bool _isConnected;
    
    public event EventHandler? Disconnected;
    private IDisposable? _connectedWatcher;

    public BlueZBluetoothDevice(BluetoothDevice device)
    {
        _inTheHandDevice = device;
    }

    public string Id => _inTheHandDevice.Id;

    public string Name => _inTheHandDevice.Name;

    /// <summary>
    /// True while a connection is believed to be established.
    ///
    /// This is a cached flag rather than a live D-Bus read: <see cref="IsConnected"/>
    /// is a synchronous property that may be polled from the UI thread, and a
    /// blocking <c>GetAllAsync</c> D-Bus round-trip there would freeze the UI.
    /// The flag is set after <see cref="ConnectAsync"/> succeeds and cleared by
    /// <see cref="Disconnect"/>.
    /// </summary>
    public bool IsConnected => _isConnected;

    /// <summary>
    /// Connects to the device via BlueZ <b>without pairing</b>, then waits for
    /// BlueZ to report the device connected and its GATT services resolved.
    /// </summary>
    public async Task ConnectAsync()
    {
        var device = GetNativeDevice();

        if (await BlueZNativeInterop.GetConnectedAsync(device))
        {
            _isConnected = true;
            return;
        }

        BluetoothLog.Info($"Connecting to {Name} ({Id})...");
        try
        {
            await BlueZNativeInterop.ConnectNoPairingAsync(device, ConnectTimeout);
            _isConnected = true;
            _connectedWatcher ??= await device.WatchPropertiesAsync(value => {
                if (!value.Changed.Any(x => x.Key == "Connected" && x.Value is bool connected && !connected)) return;
                _isConnected = false;
                Disconnected?.Invoke(this, EventArgs.Empty);
            });
            BluetoothLog.Success($"Connected to {Name}.");
        }
        catch (Exception e)
        {
            _isConnected = false;
            BluetoothLog.Error($"Connection to {Name} failed: {e.Message}");
            throw;
        }
    }

    public async Task<IGattService?> GetPrimaryServiceAsync(Guid uuid)
    {
        await ConnectAsync();

        var service = await GetNativeDevice().GetServiceAsync(uuid.ToString());
        return service == null ? null : new BlueZGattService(service, uuid);
    }

    public async Task<IReadOnlyList<IGattService>> GetPrimaryServicesAsync()
    {
        await ConnectAsync();

        var services = await GetNativeDevice().GetServicesAsync();
        var result = new List<IGattService>();
        foreach (var service in services)
        {
            // Resolve each service UUID asynchronously here (rather than via a
            // blocking getter later) so the UI never waits on a D-Bus round-trip.
            var uuidString = await service.GetUUIDAsync();
            result.Add(new BlueZGattService(service, Guid.Parse(uuidString)));
        }
        return result;
    }

    public void Disconnect()
    {
        _connectedWatcher?.Dispose();
        _connectedWatcher = null;
        _isConnected = false;

        var nativeDevice = _nativeDevice;
        if (nativeDevice == null)
        {
            return;
        }

        // The BlueZ disconnect is a blocking D-Bus round-trip. Run it on a
        // background thread so calling Disconnect() from the UI thread never
        // freezes the window while the D-Bus call is in flight.
        _ = Task.Run(() =>
        {
            try
            {
                BlueZNativeInterop.Disconnect(nativeDevice);
            }
            catch
            {
                // Disconnect is best-effort; the link may already be down.
            }
        });
    }

    /// <summary>
    /// Gets the native BlueZ <c>Device</c> D-Bus proxy from the InTheHand
    /// device (stored in its private <c>_device</c> field). Cached.
    /// </summary>
    private Device GetNativeDevice()
    {
        if (_nativeDevice != null)
        {
            return _nativeDevice;
        }

        // not public - needs reflection
        var field = typeof(BluetoothDevice).GetField("_device", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Could not access the native BlueZ device on Linux.");

        _nativeDevice = field.GetValue(_inTheHandDevice) as Device
            ?? throw new InvalidOperationException("The native BlueZ device is null.");

        return _nativeDevice;
    }
}
