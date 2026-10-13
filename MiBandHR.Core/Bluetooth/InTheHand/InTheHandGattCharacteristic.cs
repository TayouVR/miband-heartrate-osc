using InTheHand.Bluetooth;

namespace MiBandHR.Core.Bluetooth.InTheHand;

/// <summary>
/// Wraps an <see cref="InTheHand.Bluetooth.GattCharacteristic"/> into the
/// platform-neutral <see cref="IGattCharacteristic"/> abstraction.
///
/// Used on non-Linux platforms; Linux uses the native BlueZ backend
/// (<c>BlueZGattCharacteristic</c>).
/// </summary>
public sealed class InTheHandGattCharacteristic : IGattCharacteristic
{
    private readonly GattCharacteristic _characteristic;

    public InTheHandGattCharacteristic(GattCharacteristic characteristic)
    {
        _characteristic = characteristic;
    }

    public Guid Uuid => _characteristic.Uuid;

    public event EventHandler<GattCharacteristicValueChangedEventArgs>? CharacteristicValueChanged;

    /// <summary>Wires up the local value-changed event handler.</summary>
    public void SubscribeToValueChanges()
    {
        _characteristic.CharacteristicValueChanged += OnInTheHandValueChanged;
    }

    private void OnInTheHandValueChanged(object? sender,
        global::InTheHand.Bluetooth.GattCharacteristicValueChangedEventArgs args)
    {
        CharacteristicValueChanged?.Invoke(this, new GattCharacteristicValueChangedEventArgs(args.Value));
    }

    public Task WriteValueWithResponseAsync(byte[] data)
        => _characteristic.WriteValueWithResponseAsync(data);

    public Task WriteValueWithoutResponseAsync(byte[] data)
        => _characteristic.WriteValueWithoutResponseAsync(data);

    public Task StartNotificationsAsync()
        => _characteristic.StartNotificationsAsync();

    public Task StopNotificationsAsync()
        => _characteristic.StopNotificationsAsync();
}
