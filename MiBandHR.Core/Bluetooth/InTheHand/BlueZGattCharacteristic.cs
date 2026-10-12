namespace MiBandHR.Core.Bluetooth.InTheHand;

/// <summary>
/// Linux (BlueZ) <see cref="IGattCharacteristic"/> backed by a native
/// <c>IGattCharacteristic1</c> D-Bus proxy. Notifications use the correct
/// subscribe-then-start ordering and tolerate the transient BlueZ
/// <c>InProgress</c> race (handled by <see cref="LinuxBlueZInterop"/>).
/// </summary>
public sealed class BlueZGattCharacteristic : IGattCharacteristic
{
    private readonly object _nativeCharacteristic;
    private IDisposable? _propertyWatcher;

    public BlueZGattCharacteristic(object nativeCharacteristic, Guid uuid)
    {
        _nativeCharacteristic = nativeCharacteristic;
        Uuid = uuid;
    }

    public Guid Uuid { get; }

    public event EventHandler<GattCharacteristicValueChangedEventArgs>? CharacteristicValueChanged;

    public void SubscribeToValueChanges()
    {
        // The D-Bus subscription is established in StartNotificationsAsync,
        // right before enabling notify, so nothing is missed in between.
    }

    public Task WriteValueWithResponseAsync(byte[] data)
        => BlueZNativeInterop.WriteValueAsync(_nativeCharacteristic, data);

    public Task WriteValueWithoutResponseAsync(byte[] data)
        => BlueZNativeInterop.WriteValueWithoutResponseAsync(_nativeCharacteristic, data);

    public async Task StartNotificationsAsync()
    {
        // Subscribe first so no notification is lost, then start notify.
        _propertyWatcher ??= await LinuxBlueZInterop.WatchValueAsync(_nativeCharacteristic, RaiseValueChanged);
        await LinuxBlueZInterop.StartNotifyWithRetryAsync(_nativeCharacteristic);

        var flags = await BlueZNativeInterop.GetCharacteristicFlagsAsync(_nativeCharacteristic);
        var notifying = await BlueZNativeInterop.GetNotifyingAsync(_nativeCharacteristic);
        BluetoothLog.Info($"Notifications enabled on {Uuid} (flags: {flags}, notifying: {notifying}).");

        // Some peripherals (e.g. the Mi Band) don't actually start notifying
        // after StartNotify even though BlueZ reports success. Explicitly write
        // the CCCD as a fallback.
        try
        {
            await BlueZNativeInterop.WriteCccdAsync(_nativeCharacteristic, new byte[] { 0x01, 0x00 });
        }
        catch (Exception ex)
        {
            BluetoothLog.Retry($"Explicit CCCD write failed: {ex.Message}");
        }
    }

    public async Task StopNotificationsAsync()
    {
        try
        {
            await LinuxBlueZInterop.StopNotifyAsync(_nativeCharacteristic);
        }
        finally
        {
            _propertyWatcher?.Dispose();
            _propertyWatcher = null;
        }
    }

    private void RaiseValueChanged(byte[] value)
    {
        CharacteristicValueChanged?.Invoke(this, new GattCharacteristicValueChangedEventArgs(value));
    }
}
