using Linux.Bluetooth;
using Tmds.DBus;

namespace MiBandHR.Core.Bluetooth.InTheHand;

/// <summary>
/// Linux (BlueZ) <see cref="IGattCharacteristic"/> backed by a native
/// <c>IGattCharacteristic1</c> D-Bus proxy. Notifications use the correct
/// subscribe-then-start ordering and tolerate the transient BlueZ
/// <c>InProgress</c> race (handled by <see cref="LinuxBlueZInterop"/>).
/// </summary>
public sealed class BlueZGattCharacteristic(GattCharacteristic nativeCharacteristic, Guid uuid) : IGattCharacteristic {
    private IDisposable? _propertyWatcher;

    public Guid Uuid { get; } = uuid;

    public event EventHandler<GattCharacteristicValueChangedEventArgs>? CharacteristicValueChanged;

    public void SubscribeToValueChanges() {
        // The D-Bus subscription is established in StartNotificationsAsync,
        // right before enabling notify, so nothing is missed in between.
    }

    public Task WriteValueWithResponseAsync(byte[] data)
        => BlueZNativeInterop.WriteValueAsync(nativeCharacteristic, data);

    public Task WriteValueWithoutResponseAsync(byte[] data)
        => BlueZNativeInterop.WriteValueWithoutResponseAsync(nativeCharacteristic, data);

    public async Task StartNotificationsAsync() {
        // Subscribe first so no notification is lost, then start notify.
        _propertyWatcher ??= await nativeCharacteristic.WatchPropertiesAsync(RaiseValueChanged);
        await LinuxBlueZInterop.StartNotifyWithRetryAsync(nativeCharacteristic);

        var flags = await nativeCharacteristic.GetFlagsAsync();
        var notifying = await nativeCharacteristic.GetNotifyingAsync();
        //BluetoothLog.Info($"Notifications enabled on {Uuid} (flags: {flags}, notifying: {notifying}).");

        // Work around a BlueZ quirk where some peripherals don't start notifying
        // after StartNotify alone (e.g. the Mi Band HR characteristic).
        await BlueZNativeInterop.PrimeNotificationsAsync(nativeCharacteristic);
    }

    public async Task StopNotificationsAsync() {
        try {
            await LinuxBlueZInterop.StopNotifyAsync(nativeCharacteristic);
        }
        finally {
            _propertyWatcher?.Dispose();
            _propertyWatcher = null;
        }
    }

    private void RaiseValueChanged(PropertyChanges value) {
        if (value.Changed.FirstOrDefault(x => x.Key == "Value").Value is byte[] bytes) {
            CharacteristicValueChanged?.Invoke(this, new GattCharacteristicValueChangedEventArgs(bytes));
        }
    }
}
