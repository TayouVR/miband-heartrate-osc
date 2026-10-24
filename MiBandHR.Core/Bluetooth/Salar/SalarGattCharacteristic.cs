using Salar.BluetoothLE.Core.Enums;
using Salar.BluetoothLE.Core.Interfaces;

namespace MiBandHR.Core.Bluetooth.Salar;

/// <summary>
/// <see cref="IGattCharacteristic"/> backed by a Salar.BluetoothLE
/// <see cref="IBleCharacteristic"/>.
///
/// Unlike the InTheHand/BlueZ wrappers, Salar's
/// <see cref="IBleCharacteristic.StartNotificationsAsync"/> takes the value
/// callback directly and returns a single task — there is no separate
/// "subscribe then start" dance and no transient
/// <c>org.bluez.Error.InProgress</c> race to retry. The callback is simply
/// forwarded to the <see cref="CharacteristicValueChanged"/> event.
/// </summary>
public sealed class SalarGattCharacteristic : IGattCharacteristic {
    private readonly IBleCharacteristic _characteristic;
    private readonly SemaphoreSlim _notificationGate = new(1, 1);
    private bool _notificationsStarted;

    public SalarGattCharacteristic(IBleCharacteristic characteristic) {
        _characteristic = characteristic;
    }

    /// <summary>The wrapped Salar characteristic.</summary>
    public IBleCharacteristic NativeCharacteristic => _characteristic;

    public Guid Uuid => _characteristic.Uuid;

    public event EventHandler<GattCharacteristicValueChangedEventArgs>? CharacteristicValueChanged;

    /// <summary>
    /// No-op for Salar: the value callback is registered in
    /// <see cref="StartNotificationsAsync"/>. Kept to satisfy the interface.
    /// </summary>
    public void SubscribeToValueChanges() {
        // Salar registers the notification handler in StartNotificationsAsync,
        // so there is nothing to wire up ahead of time here.
    }

    public async Task WriteValueWithResponseAsync(byte[] data) {
        await _characteristic.WriteAsync(data, WriteType.WithResponse);
    }

    public async Task WriteValueWithoutResponseAsync(byte[] data) {
        await _characteristic.WriteAsync(data, WriteType.WithoutResponse);
    }

    public async Task StartNotificationsAsync() {
        await _notificationGate.WaitAsync().ConfigureAwait(false);
        try {
            if (_notificationsStarted) return;
            try {
                await _characteristic.StartNotificationsAsync(value => {
                    CharacteristicValueChanged?.Invoke(this, new GattCharacteristicValueChangedEventArgs(value));
                }).ConfigureAwait(false);
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
            _notificationsStarted = false;
            _notificationGate.Release();
        }
    }

    private async Task StopNotificationsCoreAsync() {
        try {
            await _characteristic.StopNotificationsAsync().ConfigureAwait(false);
        } catch {
            // Stopping notifications is best-effort; the connection may already be gone.
        }
    }
}
