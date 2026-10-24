#if !WINDOWS
using Linux.Bluetooth;
using Tmds.DBus;

namespace MiBandHR.Core.Bluetooth.BlueZ;

/// <summary>
/// Linux (BlueZ) <see cref="IGattCharacteristic"/> backed by a native
/// <c>GattCharacteristic</c> D-Bus proxy. Notifications use the correct
/// subscribe-then-start ordering and tolerate the transient BlueZ
/// <c>InProgress</c> race (handled by <see cref="LinuxBlueZInterop"/>).
/// </summary>
public sealed class BlueZGattCharacteristic(GattCharacteristic nativeCharacteristic, Guid uuid) : IGattCharacteristic {
    private readonly SemaphoreSlim _notificationGate = new(1, 1);
    private IDisposable? _propertyWatcher;
    private bool _notificationsStarted;

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
        await _notificationGate.WaitAsync().ConfigureAwait(false);
        try {
            if (_notificationsStarted) return;
            try {
                // Subscribe first so no notification is lost, then start notify.
                _propertyWatcher = await nativeCharacteristic.WatchPropertiesAsync(RaiseValueChanged)
                    .ConfigureAwait(false);
                await LinuxBlueZInterop.StartNotifyWithRetryAsync(nativeCharacteristic).ConfigureAwait(false);

                // Work around a BlueZ quirk where some peripherals don't start notifying
                // after StartNotify alone (e.g. the Mi Band HR characteristic).
                await BlueZNativeInterop.PrimeNotificationsAsync(nativeCharacteristic).ConfigureAwait(false);
                _notificationsStarted = true;
            } catch {
                await StopNotificationsCoreAsync().ConfigureAwait(false);
                throw;
            }
        } finally {
            _notificationGate.Release();
        }
    }

    public async Task StopNotificationsAsync() {
        await _notificationGate.WaitAsync().ConfigureAwait(false);
        try {
            await StopNotificationsCoreAsync().ConfigureAwait(false);
        } finally {
            _notificationGate.Release();
        }
    }

    private async Task StopNotificationsCoreAsync() {
        try {
            await LinuxBlueZInterop.StopNotifyAsync(nativeCharacteristic).ConfigureAwait(false);
        } finally {
            _propertyWatcher?.Dispose();
            _propertyWatcher = null;
            _notificationsStarted = false;
        }
    }

    private void RaiseValueChanged(PropertyChanges value) {
        if (value.Changed.FirstOrDefault(x => x.Key == "Value").Value is byte[] bytes) {
            CharacteristicValueChanged?.Invoke(this, new GattCharacteristicValueChangedEventArgs(bytes));
        }
    }
}
#endif
