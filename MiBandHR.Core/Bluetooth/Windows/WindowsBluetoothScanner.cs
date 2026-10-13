#if WINDOWS
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace MiBandHR.Core.Bluetooth.Windows;

/// <summary>
/// <see cref="IBluetoothScanner"/> backed by the original
/// <c>Windows.Devices.Bluetooth</c> (WinRT) stack, using a
/// <see cref="DeviceWatcher"/> to enumerate paired BLE devices.
///
/// This mirrors the original <c>MiBand-Heartrate</c> behaviour: it watches for
/// already-paired BLE devices rather than scanning for advertisements.
/// </summary>
public sealed class WindowsBluetoothScanner : IBluetoothScanner
{
    private static readonly TimeSpan ScanDuration = TimeSpan.FromSeconds(10);

    private DeviceWatcher? _watcher;

    public event EventHandler<IBluetoothDevice>? DeviceFound;

    public async Task ScanForDevicesAsync(CancellationToken cancellationToken = default)
    {
        BluetoothLog.Info("Starting scan for BLE devices (Windows)...");
        try
        {
            var foundDevices = new System.Collections.Generic.HashSet<string>();

            _watcher = DeviceInformation.CreateWatcher(
                BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
                null,
                DeviceInformationKind.AssociationEndpoint
            );

            _watcher.Added += (sender, args) =>
            {
                if (!foundDevices.Add(args.Id))
                {
                    return;
                }

                var name = args.Name;
                DeviceFound?.Invoke(this, new WindowsBluetoothDevice(args.Id, string.IsNullOrEmpty(name) ? "Unknown" : name));
            };

            _watcher.Start();

            try
            {
                await Task.Delay(ScanDuration, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Scan cancelled early.
            }
            finally
            {
                if (_watcher.Status == DeviceWatcherStatus.Started)
                {
                    _watcher.Stop();
                }
                _watcher = null;
            }

            BluetoothLog.Info($"Scan finished (Windows), {foundDevices.Count} device(s) found.");
        }
        catch (Exception e)
        {
            BluetoothLog.Error($"Scan failed (Windows): {e.Message}");
            throw;
        }
    }
}

#endif
