#if !WINDOWS
using MiBandHR.BluetoothChecks.Fakes;
using Tmds.DBus;

namespace Tmds.DBus {
    public readonly record struct ObjectPath(string Value) {
        public override string ToString() => Value;
        public static implicit operator ObjectPath(string value) => new(value);
    }

    public sealed class PropertyChanges {
        public KeyValuePair<string, object>[] Changed { get; init; } = [];
    }

    public sealed class DBusException(string errorName, string message) : Exception(message) {
        public string ErrorName => errorName;
    }

    public interface IObjectManager {
        Task<IDictionary<ObjectPath, IDictionary<string, IDictionary<string, object>>>> GetManagedObjectsAsync();
    }

    public sealed class Connection : IObjectManager {
        public static Connection System { get; } = new();
        public NativeOperation Enumerate { get; } = new();
        public NativeOperation ReadDescriptor { get; } = new();
        public T CreateProxy<T>(string service, ObjectPath path) => (T)(object)(typeof(T) == typeof(IObjectManager)
            ? this : new Descriptor(ReadDescriptor));

        public async Task<IDictionary<ObjectPath, IDictionary<string, IDictionary<string, object>>>> GetManagedObjectsAsync() {
            await Enumerate.Invoke();
            return new Dictionary<ObjectPath, IDictionary<string, IDictionary<string, object>>> {
                ["/fake/characteristic/descriptor"] = new Dictionary<string, IDictionary<string, object>> {
                    ["org.bluez.GattDescriptor1"] = new Dictionary<string, object>()
                }
            };
        }

        private sealed class Descriptor(NativeOperation read) : Linux.Bluetooth.IGattDescriptor1 {
            public async Task<object> GetAllAsync() { await read.Invoke(); return new object(); }
        }
    }
}

namespace Linux.Bluetooth {
    public sealed class Device1Properties {
        public bool Connected { get; set; }
        public bool ServicesResolved { get; set; }
    }

    public sealed class Device {
        public NativeOperation Connect { get; } = new();
        public NativeOperation Disconnect { get; } = new();
        public NativeOperation ReadProperties { get; } = new();
        public Watchers<PropertyChanges> Watchers { get; } = new();
        public Device1Properties Properties { get; } = new();
        public bool ResolveOnConnect { get; set; } = true;

        public async Task ConnectAsync() {
            await Connect.Invoke();
            Properties.Connected = true;
            Properties.ServicesResolved = ResolveOnConnect;
        }

        public async Task DisconnectAsync() {
            await Disconnect.Invoke();
            Properties.Connected = false;
            Properties.ServicesResolved = false;
        }

        public async Task<Device1Properties> GetAllAsync() { await ReadProperties.Invoke(); return Properties; }
        public Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler) => Watchers.Watch(handler);
        public Task<IGattService1?> GetServiceAsync(string uuid) => Task.FromResult<IGattService1?>(null);
        public Task<IReadOnlyList<IGattService1>> GetServicesAsync() => Task.FromResult<IReadOnlyList<IGattService1>>([]);
        public void LoseConnection() {
            Properties.Connected = false;
            Watchers.Raise(new PropertyChanges { Changed = [new("Connected", false)] });
        }
    }

    public interface IGattService1 {
        Task<string> GetUUIDAsync();
        Task<GattCharacteristic?> GetCharacteristicAsync(string uuid);
    }

    public interface IGattDescriptor1 { Task<object> GetAllAsync(); }

    public sealed class GattCharacteristic {
        public ObjectPath ObjectPath => "/fake/characteristic";
        public NativeOperation Start { get; } = new();
        public NativeOperation Stop { get; } = new();
        public Watchers<PropertyChanges> Watchers { get; } = new();
        public bool NativeActive { get; private set; }
        public async Task StartNotifyAsync() { NativeActive = true; await Start.Invoke(); }
        public async Task StopNotifyAsync() { await Stop.Invoke(); NativeActive = false; }
        public Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler) => Watchers.Watch(handler);
        public Task WriteValueAsync(byte[] data, IDictionary<string, object> options) => Task.CompletedTask;
        public void Emit(byte[] bytes) => Watchers.Raise(new PropertyChanges { Changed = [new("Value", bytes)] });
    }
}

namespace Linux.Bluetooth.Extensions {
    public static class NamespaceMarker { }
}
#endif