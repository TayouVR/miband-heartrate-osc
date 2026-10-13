using InTheHand.Bluetooth;

namespace MiBandHR.Core.Bluetooth.InTheHand;

/// <summary>
/// Wraps an <see cref="InTheHand.Bluetooth.BluetoothDevice"/> into the
/// platform-neutral <see cref="IBluetoothDevice"/> abstraction.
/// </summary>
public sealed class InTheHandBluetoothDevice(BluetoothDevice device) : IBluetoothDevice {
    public string Id => device.Id;

    public string Name => device.Name;

    public bool IsConnected => device.Gatt.IsConnected;

    /// <summary>
    /// Establishes the GATT connection if it is not already open.
    /// On Linux (BlueZ) this also waits for BlueZ to resolve the device's
    /// GATT services before returning.
    /// </summary>
    public async Task ConnectAsync() {
        if (device.Gatt.IsConnected) {
            return;
        }

        await device.Gatt.ConnectAsync();
    }

    public async Task<IGattService?> GetPrimaryServiceAsync(Guid uuid) {
        await ConnectAsync();
        var service = await device.Gatt.GetPrimaryServiceAsync(BluetoothUuid.FromGuid(uuid));
        return service == null ? null : new InTheHandGattService(service);
    }

    public async Task<IReadOnlyList<IGattService>> GetPrimaryServicesAsync() {
        await ConnectAsync();
        var services = await device.Gatt.GetPrimaryServicesAsync();
        return services.Select(s => (IGattService)new InTheHandGattService(s)).ToList();
    }

    public void Disconnect() => device.Gatt.Disconnect();
    // TODO: implement disconnect detection and raise Disconnected for this backend.
#pragma warning disable CS0067
    public event EventHandler? Disconnected;
#pragma warning restore CS0067
}
