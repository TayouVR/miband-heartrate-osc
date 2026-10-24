using InTheHand.Bluetooth;

namespace MiBandHR.Core.Bluetooth.InTheHand;

/// <summary>
/// Wraps an <see cref="InTheHand.Bluetooth.GattCharacteristic"/> into the
/// platform-neutral <see cref="IGattCharacteristic"/> abstraction.
///
/// Used on non-Linux platforms; Linux uses the native BlueZ backend
/// (<c>BlueZGattCharacteristic</c>).
/// </summary>
public sealed class InTheHandGattCharacteristic : IGattCharacteristic {
    private readonly GattCharacteristic _characteristic;
    private readonly SemaphoreSlim _notificationGate = new(1, 1);
    private readonly object _subscriptionLock = new();
    private bool _subscribed;
    private bool _notificationsStarted;

    public InTheHandGattCharacteristic(GattCharacteristic characteristic) {
        _characteristic = characteristic;
    }

    public Guid Uuid => _characteristic.Uuid;

    public event EventHandler<GattCharacteristicValueChangedEventArgs>? CharacteristicValueChanged;

    /// <summary>Wires up the local value-changed event handler.</summary>
    public void SubscribeToValueChanges() {
        lock (_subscriptionLock) {
            if (_subscribed) return;
            _characteristic.CharacteristicValueChanged += OnInTheHandValueChanged;
            _subscribed = true;
        }
    }

    private void UnsubscribeFromValueChanges() {
        lock (_subscriptionLock) {
            if (!_subscribed) return;
            _characteristic.CharacteristicValueChanged -= OnInTheHandValueChanged;
            _subscribed = false;
        }
    }

    private void OnInTheHandValueChanged(object? sender,
        global::InTheHand.Bluetooth.GattCharacteristicValueChangedEventArgs args) {
        if (args.Value is not { } value) return;
        CharacteristicValueChanged?.Invoke(this, new GattCharacteristicValueChangedEventArgs(value));
    }

    public Task WriteValueWithResponseAsync(byte[] data)
        => _characteristic.WriteValueWithResponseAsync(data);

    public Task WriteValueWithoutResponseAsync(byte[] data)
        => _characteristic.WriteValueWithoutResponseAsync(data);

    public async Task StartNotificationsAsync() {
        await _notificationGate.WaitAsync().ConfigureAwait(false);
        try {
            if (_notificationsStarted) return;
            SubscribeToValueChanges();
            try {
                await _characteristic.StartNotificationsAsync().ConfigureAwait(false);
                _notificationsStarted = true;
            } catch {
                try {
                    await _characteristic.StopNotificationsAsync().ConfigureAwait(false);
                } catch {
                    // Preserve the startup error if native cleanup also fails.
                }
                UnsubscribeFromValueChanges();
                throw;
            }
        } finally {
            _notificationGate.Release();
        }
    }

    public async Task StopNotificationsAsync() {
        await _notificationGate.WaitAsync().ConfigureAwait(false);
        try {
            await _characteristic.StopNotificationsAsync().ConfigureAwait(false);
        } finally {
            UnsubscribeFromValueChanges();
            _notificationsStarted = false;
            _notificationGate.Release();
        }
    }
}
