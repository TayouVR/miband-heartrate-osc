using InTheHand.Bluetooth;

namespace MiBandHR.Core.Bluetooth.InTheHand;

/// <summary>
/// <see cref="IBluetoothScanner"/> implementation backed by InTheHand.BluetoothLE.
/// Works on Windows, Linux (BlueZ) and macOS.
/// </summary>
public sealed class InTheHandBluetoothScanner : IBluetoothScanner
{
    public event EventHandler<IBluetoothDevice>? DeviceFound;

    public async Task ScanForDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = await global::InTheHand.Bluetooth.Bluetooth.ScanForDevicesAsync();
        foreach (var device in devices)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            DeviceFound?.Invoke(this, new InTheHandBluetoothDevice(device));
        }
    }
}
