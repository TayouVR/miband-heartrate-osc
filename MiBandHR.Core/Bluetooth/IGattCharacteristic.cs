namespace MiBandHR.Core.Bluetooth;

/// <summary>
/// Platform-neutral GATT characteristic abstraction.
/// Implementations wrap the platform specific BLE stack
/// (e.g. InTheHand.BluetoothLE, Plugin.BLE, Windows.Devices.Bluetooth, ...).
/// </summary>
public interface IGattCharacteristic {
    /// <summary>The 128-bit UUID of this characteristic.</summary>
    Guid Uuid { get; }

    /// <summary>
    /// Raised whenever the peripheral sends a notification/indication
    /// for this characteristic. Call <see cref="SubscribeToValueChanges"/>
    /// (and <see cref="StartNotificationsAsync"/>) before relying on it.
    /// </summary>
    event EventHandler<GattCharacteristicValueChangedEventArgs>? CharacteristicValueChanged;

    /// <summary>Wires up the local event handler for value changes.</summary>
    void SubscribeToValueChanges();

    /// <summary>Writes a value to the characteristic requesting a response (write with response).</summary>
    Task WriteValueWithResponseAsync(byte[] data);

    /// <summary>
    /// Writes a value to the characteristic without waiting for a response
    /// (write without response / signed write). The Mi Band authentication
    /// protocol requires this write type for all command frames.
    /// </summary>
    Task WriteValueWithoutResponseAsync(byte[] data);

    /// <summary>
    /// Asks the peripheral to start sending notifications/indications for
    /// this characteristic.
    /// </summary>
    Task StartNotificationsAsync();

    /// <summary>Disables notifications/indications for this characteristic.</summary>
    Task StopNotificationsAsync();
}

/// <summary>
/// Arguments for the <see cref="IGattCharacteristic.CharacteristicValueChanged"/> event.
/// </summary>
public sealed class GattCharacteristicValueChangedEventArgs : EventArgs {
    public GattCharacteristicValueChangedEventArgs(byte[] value) {
        Value = value;
    }

    /// <summary>The raw value reported by the peripheral.</summary>
    public byte[] Value { get; }
}
