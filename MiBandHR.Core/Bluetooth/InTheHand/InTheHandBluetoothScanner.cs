using InTheHand.Bluetooth;

namespace MiBandHR.Core.Bluetooth.InTheHand;

/// <summary>
/// <see cref="IBluetoothScanner"/> implementation backed by InTheHand.BluetoothLE.
/// Works on Windows, Linux (BlueZ) and macOS.
///
/// On Linux the discovered devices are wrapped in
/// <see cref="BlueZBluetoothDevice"/> (which connects without pairing);
/// elsewhere they use <see cref="InTheHandBluetoothDevice"/>.
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
                DeviceFound?.Invoke(this, WrapDevice(device));
            }
        }
        catch (Exception e)
        {
            BluetoothLog.Error($"Scan failed: {e.Message}");
            throw;
        }
    }

    internal static IBluetoothDevice WrapDevice(BluetoothDevice device)
    {
        return OperatingSystem.IsLinux()
            ? new BlueZBluetoothDevice(device)
            : new InTheHandBluetoothDevice(device);
    }
}
