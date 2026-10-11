using MiBandHR.Core.Bluetooth;
using MiBandHR.Core.Configuration;

namespace MiBandHR.Core;

/// <summary>
/// Backwards-compatible BLE scanner facade.
///
/// New code should use <see cref="IBluetoothScanner"/> /
/// <see cref="BluetoothScannerFactory"/> directly. This class keeps the
/// original API surface for the frontends while routing everything through
/// the platform-neutral abstraction and the configured backend (see
/// <see cref="BluetoothBackend"/>), falling back to a backend available on the
/// current platform when the configured one is not (e.g. a shared config file).
/// </summary>
public class BLE
{
    private readonly IBluetoothScanner _scanner = CreateScanner();

    private static IBluetoothScanner CreateScanner()
    {
        try
        {
            return BluetoothScannerFactory.Create(
                ConfigurationManager.Instance.CurrentConfig.Device.BluetoothBackend);
        }
        catch (InvalidOperationException)
        {
            // Configuration isn't initialized yet (e.g. in design-time data).
            return BluetoothScannerFactory.CreateDefault();
        }
    }

    /// <summary>
    /// Raised for every discovered device. The device is exposed as the
    /// platform-neutral <see cref="IBluetoothDevice"/>.
    /// </summary>
    public event EventHandler<IBluetoothDevice>? DeviceFound
    {
        add => _scanner.DeviceFound += value;
        remove => _scanner.DeviceFound -= value;
    }

    public Task ScanForDevicesAsync() => ScanForDevicesAsync(CancellationToken.None);

    public Task ScanForDevicesAsync(CancellationToken cancellationToken) =>
        _scanner.ScanForDevicesAsync(cancellationToken);

    /// <summary>Writes a payload to a characteristic, swallowing transport errors.</summary>
    public static async Task Write(IGattCharacteristic characteristic, byte[] data)
    {
        try
        {
            await characteristic.WriteValueWithResponseAsync(data);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Unable to write on {characteristic.Uuid} - {e.Message}");
        }
    }
}