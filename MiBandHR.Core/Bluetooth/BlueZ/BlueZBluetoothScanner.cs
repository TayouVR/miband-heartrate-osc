#if !WINDOWS
using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;

namespace MiBandHR.Core.Bluetooth.BlueZ;

/// <summary>
/// <see cref="IBluetoothScanner"/> backed directly by BlueZ via the
/// <c>Linux.Bluetooth</c> library (no InTheHand involvement).
/// </summary>
public sealed class BlueZBluetoothScanner : IBluetoothScanner {
    private static readonly TimeSpan ScanDuration = TimeSpan.FromSeconds(10);

    public event EventHandler<IBluetoothDevice>? DeviceFound;

    public async Task ScanForDevicesAsync(CancellationToken cancellationToken = default) {
        BluetoothLog.Info("Starting scan for BLE devices...");

        var adapter = (await BlueZManager.GetAdaptersAsync()).FirstOrDefault()
                      ?? throw new InvalidOperationException("No Bluetooth adapter found.");

        if (!await adapter.GetPoweredAsync()) {
            BluetoothLog.Error("Bluetooth adapter is not powered on.");
            return;
        }

        var watcher = await adapter.WatchDevicesAddedAsync(device => _ = PublishDeviceAsync(device));

        try {
            await adapter.StartDiscoveryAsync();

            // Emit devices BlueZ already knows about.
            foreach (var device in await adapter.GetDevicesAsync()) {
                if (cancellationToken.IsCancellationRequested) break;
                await PublishDeviceAsync(device);
            }

            try {
                await Task.Delay(ScanDuration, cancellationToken);
            } catch (OperationCanceledException) {
                // Scan cancelled early.
            }
        }
        finally {
            watcher.Dispose();
            try {
                await adapter.StopDiscoveryAsync();
            } catch {
                // Best effort; discovery may already be stopped.
            }
        }

        BluetoothLog.Info("Scan finished.");
    }

    private async Task PublishDeviceAsync(Device device) {
        try {
            var id = await device.GetAddressAsync();
            var name = await device.GetNameAsync();
            if (string.IsNullOrEmpty(name)) {
                name = await device.GetAliasAsync();
            }

            DeviceFound?.Invoke(this, new BlueZBluetoothDevice(device, id, name));
        } catch (Exception e) {
            BluetoothLog.Error($"Failed to resolve device: {e.Message}");
        }
    }
}
#endif