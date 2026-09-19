namespace MiBandHR.Core.Bluetooth;

/// <summary>
/// Scans for nearby BLE peripherals and raises <see cref="DeviceFound"/>
/// for every discovered device.
/// </summary>
public interface IBluetoothScanner
{
    event EventHandler<IBluetoothDevice>? DeviceFound;

    /// <summary>
    /// Performs a single scan pass and reports every discovered device
    /// through <see cref="DeviceFound"/>.
    /// </summary>
    Task ScanForDevicesAsync(CancellationToken cancellationToken = default);
}
