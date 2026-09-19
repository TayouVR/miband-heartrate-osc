using InTheHand.Bluetooth;

namespace MiBandHR.Core.Bluetooth.InTheHand;

/// <summary>
/// Wraps an <see cref="InTheHand.Bluetooth.BluetoothDevice"/> into the
/// platform-neutral <see cref="IBluetoothDevice"/> abstraction.
/// </summary>
public sealed class InTheHandBluetoothDevice : IBluetoothDevice
{
    private readonly BluetoothDevice _device;

    public InTheHandBluetoothDevice(BluetoothDevice device)
    {
        _device = device;
    }

    /// <summary>The underlying concrete device, in case a consumer needs library-specific features.</summary>
    public BluetoothDevice NativeDevice => _device;

    public string Id => _device.Id;

    public string Name => _device.Name;

    public bool IsConnected => _device.Gatt.IsConnected;

    /// <summary>
    /// Establishes the GATT connection if it is not already open.
    /// On Linux (BlueZ) this also waits for BlueZ to resolve the device's
    /// GATT services before returning.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (_device.Gatt.IsConnected)
        {
            return;
        }

        await _device.Gatt.ConnectAsync();
    }

    public async Task<IGattService?> GetPrimaryServiceAsync(Guid uuid)
    {
        await ConnectAsync();
        var service = await _device.Gatt.GetPrimaryServiceAsync(BluetoothUuid.FromGuid(uuid));
        return service == null ? null : new InTheHandGattService(service);
    }

    public async Task<IReadOnlyList<IGattService>> GetPrimaryServicesAsync()
    {
        await ConnectAsync();
        var services = await _device.Gatt.GetPrimaryServicesAsync();
        return services.Select(s => (IGattService)new InTheHandGattService(s)).ToList();
    }

    public void Disconnect() => _device.Gatt.Disconnect();
}
