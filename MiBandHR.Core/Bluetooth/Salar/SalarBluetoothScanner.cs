#if !WINDOWS

using Salar.BluetoothLE;
using Salar.BluetoothLE.Core.Enums;
using Salar.BluetoothLE.Core.Models;
using Salar.BluetoothLE.Linux;

namespace MiBandHR.Core.Bluetooth.Salar;

/// <summary>
/// <see cref="IBluetoothScanner"/> backed by Salar.BluetoothLE.
///
/// This is the default backend on Linux: Salar's BlueZ implementation connects
/// to devices <b>without pairing</b>, which is what the Mi Band 3 needs.
/// </summary>
public sealed class SalarBluetoothScanner : IBluetoothScanner
{
    private static readonly TimeSpan ScanDuration = TimeSpan.FromSeconds(10);

    private readonly IBleAdapter _adapter;

    public SalarBluetoothScanner()
        : this(new LinuxBleAdapter())
    {
    }

    internal SalarBluetoothScanner(IBleAdapter adapter)
    {
        _adapter = adapter;
        _adapter.ScanResultReceived.Subscribe(OnScanResult);
    }

    public event EventHandler<IBluetoothDevice>? DeviceFound;

    public async Task ScanForDevicesAsync(CancellationToken cancellationToken = default)
    {
        BluetoothLog.Info("Starting scan for BLE devices...");
        try
        {
            var access = await _adapter.RequestAccessAsync(cancellationToken);
            if (access != BlePermissionStatus.Granted)
            {
                BluetoothLog.Error($"Bluetooth access not granted (status: {access}).");
                return;
            }

            await _adapter.StartScanAsync(new ScanConfig
            {
                Duration = ScanDuration,
                ScanMode = ScanMode.LowLatency,
                AllowDuplicates = false
            }, cancellationToken);

            try
            {
                await Task.Delay(ScanDuration, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Scan cancelled early; fall through to stop.
            }

            await _adapter.StopScanAsync(cancellationToken);
            BluetoothLog.Info("Scan finished.");
        }
        catch (Exception e)
        {
            BluetoothLog.Error($"Scan failed: {e.Message}");
            throw;
        }
    }

    private void OnScanResult(ScanResult result)
    {
        if (string.IsNullOrEmpty(result.Address))
        {
            return;
        }

        DeviceFound?.Invoke(this, new SalarBluetoothDevice(_adapter, result.Address, result.Name ?? string.Empty));
    }
}

#endif
