using InTheHand.Bluetooth;

namespace MiBandHR.Core.Bluetooth.InTheHand;

/// <summary>
/// <see cref="IBluetoothScanner"/> implementation backed by InTheHand.BluetoothLE.
/// Used on non-Linux platforms; Linux uses the native BlueZ backend.
/// </summary>
public sealed class InTheHandBluetoothScanner : IBluetoothScanner
{
    public event EventHandler<IBluetoothDevice>? DeviceFound;

    public async Task ScanForDevicesAsync(CancellationToken cancellationToken = default)
    {
        BluetoothLog.Info("Starting scan for BLE devices...");
        try
        {
            var devices = await global::InTheHand.Bluetooth.Bluetooth.ScanForDevicesAsync();
            BluetoothLog.Info($"Scan finished, {devices.Count} device(s) found.");

            foreach (var device in devices)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                DeviceFound?.Invoke(this, new InTheHandBluetoothDevice(device));
            }
        }
        catch (Exception e)
        {
            BluetoothLog.Error($"Scan failed: {e.Message}");
            throw;
        }
    }
}
