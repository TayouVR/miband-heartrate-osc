using MiBandHR.BluetoothChecks.Fakes;

namespace InTheHand.Bluetooth;

public readonly record struct BluetoothUuid(Guid Value) {
    public static BluetoothUuid FromGuid(Guid uuid) => new(uuid);
    public static implicit operator Guid(BluetoothUuid uuid) => uuid.Value;
}

public sealed class BluetoothDevice {
    public string Id => "fake InTheHand";
    public string Name => Id;
    public RemoteGattServer Gatt { get; } = new();
}

public sealed class RemoteGattServer {
    public NativeOperation Connect { get; } = new();
    public bool IsConnected { get; private set; }
    public int DisconnectCalls { get; private set; }
    public async Task ConnectAsync() { await Connect.Invoke(); IsConnected = true; }
    public void Disconnect() { DisconnectCalls++; IsConnected = false; }
    public Task<GattService?> GetPrimaryServiceAsync(BluetoothUuid uuid) => Task.FromResult<GattService?>(null);
    public Task<IReadOnlyList<GattService>> GetPrimaryServicesAsync() => Task.FromResult<IReadOnlyList<GattService>>([]);
}

public sealed class GattService {
    public BluetoothUuid Uuid { get; } = BluetoothUuid.FromGuid(Guid.NewGuid());
    public Task<GattCharacteristic?> GetCharacteristicAsync(BluetoothUuid uuid) => Task.FromResult<GattCharacteristic?>(null);
}

public sealed class GattCharacteristicValueChangedEventArgs(byte[]? value) : EventArgs {
    public byte[]? Value => value;
}

public sealed class GattCharacteristic {
    public BluetoothUuid Uuid { get; } = BluetoothUuid.FromGuid(Guid.NewGuid());
    public NativeOperation Start { get; } = new();
    public NativeOperation Stop { get; } = new();
    public bool NativeActive { get; private set; }
    public Handlers<EventHandler<GattCharacteristicValueChangedEventArgs>> Handlers { get; } = new();
    public event EventHandler<GattCharacteristicValueChangedEventArgs> CharacteristicValueChanged {
        add => Handlers.Add(value);
        remove => Handlers.Remove(value);
    }
    public Task WriteValueWithResponseAsync(byte[] data) => Task.CompletedTask;
    public Task WriteValueWithoutResponseAsync(byte[] data) => Task.CompletedTask;
    public async Task StartNotificationsAsync() { NativeActive = true; await Start.Invoke(); }
    public async Task StopNotificationsAsync() { await Stop.Invoke(); NativeActive = false; }
    public void Emit(byte[]? bytes) {
        foreach (var handler in Handlers.Snapshot()) handler(this, new GattCharacteristicValueChangedEventArgs(bytes));
    }
}