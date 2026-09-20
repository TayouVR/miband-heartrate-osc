using System.Reflection;
using InTheHand.Bluetooth;

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
    private object? _nativeDevice; // Linux.Bluetooth.Device

    public BlueZBluetoothDevice(BluetoothDevice device)
    {
        _inTheHandDevice = device;
    }

    public string Id => _inTheHandDevice.Id;

    public string Name => _inTheHandDevice.Name;

    public bool IsConnected
    {
        get
        {
            try
            {
                return _nativeDevice != null && BlueZNativeInterop.GetConnected(_nativeDevice);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Connects to the device via BlueZ <b>without pairing</b>, then waits for
    /// BlueZ to report the device connected and its GATT services resolved.
    /// </summary>
    public async Task ConnectAsync()
    {
        var device = GetNativeDevice();

        if (BlueZNativeInterop.GetConnected(device))
        {
            return;
        }

        BluetoothLog.Info($"Connecting to {Name} ({Id})...");
        try
        {
            await BlueZNativeInterop.ConnectNoPairingAsync(device, ConnectTimeout);
            BluetoothLog.Success($"Connected to {Name}.");
        }
        catch (Exception e)
        {
            BluetoothLog.Error($"Connection to {Name} failed: {e.Message}");
            throw;
        }
    }

    public async Task<IGattService?> GetPrimaryServiceAsync(Guid uuid)
    {
        await ConnectAsync();

        var service = await BlueZNativeInterop.GetServiceAsync(GetNativeDevice(), uuid.ToString());
        return service == null ? null : new BlueZGattService(service);
    }

    public async Task<IReadOnlyList<IGattService>> GetPrimaryServicesAsync()
    {
        await ConnectAsync();

        var services = await BlueZNativeInterop.GetServicesAsync(GetNativeDevice());
        var result = new List<IGattService>();
        foreach (var service in services)
        {
            result.Add(new BlueZGattService(service));
        }
        return result;
    }

    public void Disconnect()
    {
        if (_nativeDevice != null)
        {
            BlueZNativeInterop.Disconnect(_nativeDevice);
        }
    }

    /// <summary>
    /// Gets the native BlueZ <c>Device</c> D-Bus proxy from the InTheHand
    /// device (stored in its private <c>_device</c> field). Cached.
    /// </summary>
    private object GetNativeDevice()
    {
        if (_nativeDevice != null)
        {
            return _nativeDevice;
        }

        var field = typeof(BluetoothDevice).GetField("_device", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Could not access the native BlueZ device on Linux.");

        _nativeDevice = field.GetValue(_inTheHandDevice)
            ?? throw new InvalidOperationException("The native BlueZ device is null.");

        return _nativeDevice;
    }
}
