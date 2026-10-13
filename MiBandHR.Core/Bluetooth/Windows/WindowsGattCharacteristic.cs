#if WINDOWS
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace MiBandHR.Core.Bluetooth.Windows;

/// <summary>
/// <see cref="IGattCharacteristic"/> backed by a WinRT <see cref="GattCharacteristic"/>.
///
/// Mirrors the original MiBand-Heartrate approach: write with response for
/// commands and a CCCD write to enable notifications.
/// </summary>
public sealed class WindowsGattCharacteristic : IGattCharacteristic
{
    private readonly GattCharacteristic _characteristic;

    public WindowsGattCharacteristic(GattCharacteristic characteristic)
    {
        _characteristic = characteristic;
        // WinRT fires the same event as our abstraction; forward it directly.
        _characteristic.ValueChanged += OnValueChanged;
    }

    public Guid Uuid => _characteristic.Uuid;

    public event EventHandler<GattCharacteristicValueChangedEventArgs>? CharacteristicValueChanged;

    /// <summary>
    /// WinRT wires notifications to <c>ValueChanged</c> via the CCCD write, so
    /// there is nothing separate to subscribe to here.
    /// </summary>
    public void SubscribeToValueChanges()
    {
        // Already subscribed in the constructor.
    }

    public async Task WriteValueWithResponseAsync(byte[] data)
    {
        using var writer = new DataWriter();
        writer.WriteBytes(data);
        await _characteristic.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithResponse);
    }

    public async Task WriteValueWithoutResponseAsync(byte[] data)
    {
        using var writer = new DataWriter();
        writer.WriteBytes(data);
        await _characteristic.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
    }

    public async Task StartNotificationsAsync()
    {
        await _characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify);
    }

    public async Task StopNotificationsAsync()
    {
        await _characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.None);
    }

    private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        byte[] buffer;
        using (var reader = DataReader.FromBuffer(args.CharacteristicValue))
        {
            buffer = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(buffer);
        }

        CharacteristicValueChanged?.Invoke(this, new GattCharacteristicValueChangedEventArgs(buffer));
    }
}

#endif
