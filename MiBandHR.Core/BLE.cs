using MiBandHR.Core.Bluetooth;
using MiBandHR.Core.Bluetooth.InTheHand;

namespace MiBandHR.Core;

/// <summary>
/// Backwards-compatible BLE scanner facade.
///
/// New code should use <see cref="IBluetoothScanner"/> /
/// <see cref="InTheHandBluetoothScanner"/> directly. This class keeps the
/// original API surface for the WPF frontend while routing everything
/// through the platform-neutral abstraction.
/// </summary>
public class BLE
{
    private readonly InTheHandBluetoothScanner _scanner = new();

    /// <summary>
    /// Raised for every discovered device. The device is exposed as the
    /// platform-neutral <see cref="IBluetoothDevice"/>; WPF code can unwrap
    /// the native instance via <see cref="InTheHandBluetoothDevice.NativeDevice"/>.
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
