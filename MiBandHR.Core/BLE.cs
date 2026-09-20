using MiBandHR.Core.Bluetooth;

namespace MiBandHR.Core;

/// <summary>
/// Backwards-compatible BLE scanner facade.
///
/// New code should use <see cref="IBluetoothScanner"/> /
/// <see cref="BluetoothScannerFactory"/> directly. This class keeps the
/// original API surface for the frontends while routing everything through
/// the platform-neutral abstraction and the platform-default backend
/// (Salar.BluetoothLE on Linux, InTheHand.BluetoothLE elsewhere).
/// </summary>
public class BLE
{
    private readonly IBluetoothScanner _scanner = BluetoothScannerFactory.CreateDefault();

    /// <summary>
    /// Raised for every discovered device. The device is exposed as the
    /// platform-neutral <see cref="IBluetoothDevice"/>.
    /// </summary>
    public event EventHandler<IBluetoothDevice>? DeviceFound
    {
        add => _scanner.DeviceFound += value;
        remove => _scanner.DeviceFound -= value;
    }

    public Task ScanForDevicesAsync() => _scanner.ScanForDevicesAsync();

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
