using System.ComponentModel;
using InTheHand.Bluetooth;

namespace MiBandHR.Core;

public class BLE
{
    public event EventHandler<BluetoothDevice>? DeviceFound;

    public async Task ScanForDevicesAsync()
    {
        try
        {
            var devices = await Bluetooth.ScanForDevicesAsync();
            foreach (var device in devices)
            {
                DeviceFound?.Invoke(this, device);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error scanning for devices: {ex.Message}");
        }
    }

    public static async Task Write(GattCharacteristic characteristic, byte[] data)
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