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
    private readonly SemaphoreSlim _notificationGate = new(1, 1);
    private readonly object _subscriptionLock = new();
    private bool _subscribed;
    private bool _notificationsStarted;

    public WindowsGattCharacteristic(GattCharacteristic characteristic)
    {
        _characteristic = characteristic;
    }

    public Guid Uuid => _characteristic.Uuid;

    public event EventHandler<GattCharacteristicValueChangedEventArgs>? CharacteristicValueChanged;

    /// <summary>
    /// Wires up the local WinRT value-changed event handler.
    /// </summary>
    public void SubscribeToValueChanges()
    {
        lock (_subscriptionLock)
        {
            if (_subscribed) return;
            _characteristic.ValueChanged += OnValueChanged;
            _subscribed = true;
        }
    }

    private void UnsubscribeFromValueChanges()
    {
        lock (_subscriptionLock)
        {
            if (!_subscribed) return;
            _characteristic.ValueChanged -= OnValueChanged;
            _subscribed = false;
        }
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
        await _notificationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_notificationsStarted) return;
            SubscribeToValueChanges();
            try
            {
                var status = await _characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask().ConfigureAwait(false);
                if (status != GattCommunicationStatus.Success)
                {
                    throw new InvalidOperationException($"Could not enable notifications: {status}.");
                }
                _notificationsStarted = true;
            }
            catch
            {
                try
                {
                    await _characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None).AsTask().ConfigureAwait(false);
                }
                catch
                {
                    // Preserve the startup error if native cleanup also fails.
                }
                UnsubscribeFromValueChanges();
                throw;
            }
        }
        finally
        {
            _notificationGate.Release();
        }
    }

    public async Task StopNotificationsAsync()
    {
        await _notificationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.None).AsTask().ConfigureAwait(false);
        }
        finally
        {
            UnsubscribeFromValueChanges();
            _notificationsStarted = false;
            _notificationGate.Release();
        }
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
