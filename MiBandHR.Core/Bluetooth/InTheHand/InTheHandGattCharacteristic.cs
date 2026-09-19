using InTheHand.Bluetooth;

namespace MiBandHR.Core.Bluetooth.InTheHand;

/// <summary>
/// Wraps an <see cref="InTheHand.Bluetooth.GattCharacteristic"/> into the
/// platform-neutral <see cref="IGattCharacteristic"/> abstraction.
/// </summary>
public sealed class InTheHandGattCharacteristic : IGattCharacteristic
{
    private readonly GattCharacteristic _characteristic;

    public InTheHandGattCharacteristic(GattCharacteristic characteristic)
    {
        _characteristic = characteristic;
        _characteristic.CharacteristicValueChanged += OnCharacteristicValueChanged;
    }

    public Guid Uuid => _characteristic.Uuid;

    public event EventHandler<GattCharacteristicValueChangedEventArgs>? CharacteristicValueChanged;

    private void OnCharacteristicValueChanged(object? sender, global::InTheHand.Bluetooth.GattCharacteristicValueChangedEventArgs args)
    {
        CharacteristicValueChanged?.Invoke(this, new GattCharacteristicValueChangedEventArgs(args.Value));
    }

    public Task WriteValueWithResponseAsync(byte[] data) => _characteristic.WriteValueWithResponseAsync(data);

    public Task StartNotificationsAsync() => _characteristic.StartNotificationsAsync();

    public Task StopNotificationsAsync() => _characteristic.StopNotificationsAsync();
}
