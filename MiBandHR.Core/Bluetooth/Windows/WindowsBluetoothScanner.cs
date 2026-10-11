#if WINDOWS

using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace MiBandHR.Core.Bluetooth.Windows;

/// <summary>
/// <see cref="IBluetoothScanner"/> backed by the original
/// <c>Windows.Devices.Bluetooth</c> (WinRT) stack, using a
/// <see cref="BluetoothLEAdvertisementWatcher"/> to discover nearby BLE devices
/// and <see cref="BluetoothLEDevice.FromBluetoothAddressAsync"/> to connect.
///
/// This is the Windows-only implementation that mirrors the original
/// <c>MiBand-Heartrate</c> behaviour (the code the portable backends replaced).
/// </summary>
public sealed class WindowsBluetoothScanner : IBluetoothScanner
{
    private static readonly TimeSpan ScanDuration = TimeSpan.FromSeconds(10);

    private BluetoothLEAdvertisementWatcher? _watcher;

    public event EventHandler<IBluetoothDevice>? DeviceFound;

    public async Task ScanForDevicesAsync(CancellationToken cancellationToken = default)
    {
        BluetoothLog.Info("Starting scan for BLE devices (Windows)...");
        try
        {
            var received = new System.Collections.Generic.HashSet<ulong>();

            _watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active,
                SignalStrengthFilter = { SamplingInterval = TimeSpan.FromMilliseconds(0) }
            };

            _watcher.Received += (w, args) =>
            {
                ulong address = args.BluetoothAddress;
                if (!received.Add(address))
                {
                    return;
                }

                var name = args.Advertisement.LocalName;
                DeviceFound?.Invoke(this, new WindowsBluetoothDevice(address, string.IsNullOrEmpty(name) ? "Unknown" : name));
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
                _watcher.Stop();
                _watcher = null;
            }

            BluetoothLog.Info("Scan finished (Windows).");
        }
        catch (Exception e)
        {
            BluetoothLog.Error($"Scan failed (Windows): {e.Message}");
            throw;
        }
    }
}

#endif