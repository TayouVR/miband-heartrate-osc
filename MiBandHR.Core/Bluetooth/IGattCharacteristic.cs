namespace MiBandHR.Core.Bluetooth;

/// <summary>
/// Platform-neutral GATT characteristic abstraction.
/// Implementations wrap the platform specific BLE stack
/// (e.g. InTheHand.BluetoothLE, Plugin.BLE, Windows.Devices.Bluetooth, ...).
/// </summary>
public interface IGattCharacteristic
{
    /// <summary>The 128-bit UUID of this characteristic.</summary>
    Guid Uuid { get; }

    /// <summary>
    /// Raised whenever the peripheral sends a notification/indication
    /// for this characteristic. Subscribe before calling
    /// <see cref="StartNotificationsAsync"/>.
    /// </summary>
    event EventHandler<GattCharacteristicValueChangedEventArgs>? CharacteristicValueChanged;

    /// <summary>Writes a value to the characteristic requesting a response (write with response).</summary>
    Task WriteValueWithResponseAsync(byte[] data);

    /// <summary>Enables notifications/indications for this characteristic.</summary>
    Task StartNotificationsAsync();

    /// <summary>Disables notifications/indications for this characteristic.</summary>
    Task StopNotificationsAsync();
}

/// <summary>
/// Arguments for the <see cref="IGattCharacteristic.CharacteristicValueChanged"/> event.
/// </summary>
public sealed class GattCharacteristicValueChangedEventArgs : EventArgs
{
    public GattCharacteristicValueChangedEventArgs(byte[] value)
    {
        Value = value;
    }

    /// <summary>The raw value reported by the peripheral.</summary>
    public byte[] Value { get; }
}
