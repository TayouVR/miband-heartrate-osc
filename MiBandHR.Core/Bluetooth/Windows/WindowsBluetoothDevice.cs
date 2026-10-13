#if WINDOWS

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace MiBandHR.Core.Bluetooth.Windows;

/// <summary>
/// <see cref="IBluetoothDevice"/> backed by the original
/// <c>Windows.Devices.Bluetooth</c> (WinRT) stack.
///
/// Devices are looked up by their DeviceInformation.Id (the value reported by the
/// <see cref="WindowsBluetoothScanner"/>'s DeviceWatcher).
/// </summary>
public sealed class WindowsBluetoothDevice : IBluetoothDevice
{
    private readonly string _deviceId;
    private BluetoothLEDevice? _device;

    public WindowsBluetoothDevice(string deviceId, string name)
    {
        _deviceId = deviceId;
        Name = name;
    }

    public string Id => _deviceId;

    public string Name { get; }

    public bool IsConnected => _device?.ConnectionStatus == BluetoothConnectionStatus.Connected;

    // TODO: implement disconnect detection and raise Disconnected for this backend.
#pragma warning disable CS0067
    public event EventHandler? Disconnected;
#pragma warning restore CS0067

    public async Task ConnectAsync()
    {
        if (IsConnected)
        {
            return;
        }

        BluetoothLog.Info($"Connecting to {Name} ({Id})...");
        var device = await BluetoothLEDevice.FromIdAsync(_deviceId);
        if (device == null)
        {
            throw new InvalidOperationException("Could not open the Bluetooth LE device.");
        }

        _device = device;
        BluetoothLog.Success($"Connected to {Name}.");
    }

    public async Task<IGattService?> GetPrimaryServiceAsync(Guid uuid)
    {
        await ConnectAsync();

        var result = await _device!.GetGattServicesForUuidAsync(uuid);
        if (result.Status != GattCommunicationStatus.Success || result.Services.Count == 0)
        {
            return null;
        }

        return new WindowsGattService(result.Services[0]);
    }

    public async Task<IReadOnlyList<IGattService>> GetPrimaryServicesAsync()
    {
        await ConnectAsync();

        var result = await _device!.GetGattServicesAsync();
        var services = new List<IGattService>();
        if (result.Status == GattCommunicationStatus.Success)
        {
            foreach (var service in result.Services)
            {
                services.Add(new WindowsGattService(service));
            }
        }
        return services;
    }

    public void Disconnect()
    {
        _device?.Dispose();
        _device = null;
    }
}

#endif