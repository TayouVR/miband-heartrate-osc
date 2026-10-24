using MiBandHR.BluetoothChecks.Fakes;
using Salar.BluetoothLE.Core.Enums;
using Salar.BluetoothLE.Core.Interfaces;
using Salar.BluetoothLE.Core.Models;

namespace Salar.BluetoothLE.Core.Enums {
    public enum BleDeviceState { Disconnected, Connected }
    public enum WriteType { WithResponse, WithoutResponse }
}

namespace Salar.BluetoothLE.Core.Models {
    public sealed class ConnectionConfig {
        public bool AutoConnect { get; set; }
        public TimeSpan ConnectionTimeout { get; set; }
    }
}

namespace Salar.BluetoothLE.Core.Interfaces {
    public interface IBleAdapter { Task<IBleDevice> ConnectAsync(string address, ConnectionConfig config); }
    public interface IBleDevice : IDisposable {
        string Name { get; }
        BleDeviceState State { get; }
        Task DisconnectAsync();
        Task<IBleService?> GetServiceAsync(Guid uuid);
        Task<IReadOnlyList<IBleService>> GetServicesAsync();
    }
    public interface IBleService {
        Guid Uuid { get; }
        Task<IBleCharacteristic?> GetCharacteristicAsync(Guid uuid);
    }
    public interface IBleCharacteristic {
        Guid Uuid { get; }
        Task WriteAsync(byte[] data, WriteType type);
        Task StartNotificationsAsync(Action<byte[]> callback);
        Task StopNotificationsAsync();
    }
}

namespace MiBandHR.BluetoothChecks.Fakes {
    public sealed class SalarAdapter : IBleAdapter {
        public NativeOperation Connect { get; } = new();
        public SalarDevice Device { get; set; } = new();
        public ConnectionConfig? LastConfig { get; private set; }
        public async Task<IBleDevice> ConnectAsync(string address, ConnectionConfig config) {
            LastConfig = config;
            var device = Device;
            await Connect.Invoke();
            device.State = BleDeviceState.Connected;
            return device;
        }
    }

    public sealed class SalarDevice : IBleDevice {
        public string Name => "fake Salar";
        public BleDeviceState State { get; set; }
        public NativeOperation Disconnect { get; } = new();
        public int DisposeCalls { get; private set; }
        public async Task DisconnectAsync() { await Disconnect.Invoke(); State = BleDeviceState.Disconnected; }
        public void Dispose() { DisposeCalls++; State = BleDeviceState.Disconnected; }
        public Task<IBleService?> GetServiceAsync(Guid uuid) => Task.FromResult<IBleService?>(null);
        public Task<IReadOnlyList<IBleService>> GetServicesAsync() => Task.FromResult<IReadOnlyList<IBleService>>([]);
    }

    public sealed class SalarCharacteristic : IBleCharacteristic {
        public Guid Uuid { get; } = Guid.NewGuid();
        public NativeOperation Start { get; } = new();
        public NativeOperation Stop { get; } = new();
        public Handlers<Action<byte[]>> Callbacks { get; } = new();
        public bool NativeActive { get; private set; }
        public Task WriteAsync(byte[] data, WriteType type) => Task.CompletedTask;
        public async Task StartNotificationsAsync(Action<byte[]> callback) {
            Callbacks.Add(callback);
            NativeActive = true;
            await Start.Invoke();
        }
        public async Task StopNotificationsAsync() {
            await Stop.Invoke();
            Callbacks.Clear();
            NativeActive = false;
        }
        public void Emit(byte[] bytes) { foreach (var callback in Callbacks.Snapshot()) callback(bytes); }
    }
}