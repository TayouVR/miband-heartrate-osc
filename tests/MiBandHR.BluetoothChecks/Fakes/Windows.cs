#if WINDOWS
using MiBandHR.BluetoothChecks.Fakes;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace System {
    public static class FakeWinRtTaskExtensions {
        public static Task<T> AsTask<T>(this Task<T> task) => task;
    }
}

namespace Windows.Devices.Bluetooth {
    public enum BluetoothConnectionStatus { Disconnected, Connected }

    public sealed class BluetoothLEDevice : IDisposable {
        public static NativeOperation Open { get; private set; } = new();
        public static BluetoothLEDevice Next { get; private set; } = new();
        public BluetoothConnectionStatus ConnectionStatus { get; private set; }
        public int DisposeCalls { get; private set; }
        public static void Reset() { Open = new(); Next = new(); }
        public static async Task<BluetoothLEDevice?> FromIdAsync(string id) {
            var device = Next;
            await Open.Invoke();
            device.ConnectionStatus = BluetoothConnectionStatus.Connected;
            return device;
        }
        public void Dispose() { DisposeCalls++; ConnectionStatus = BluetoothConnectionStatus.Disconnected; }
        public Task<GattDeviceServicesResult> GetGattServicesForUuidAsync(Guid uuid) => GetGattServicesAsync();
        public Task<GattDeviceServicesResult> GetGattServicesAsync() => Task.FromResult(new GattDeviceServicesResult());
    }
}

namespace Windows.Devices.Bluetooth.GenericAttributeProfile {
    public enum GattCommunicationStatus { Success, Unreachable, ProtocolError, AccessDenied }
    public enum GattWriteOption { WriteWithResponse, WriteWithoutResponse }
    public enum GattClientCharacteristicConfigurationDescriptorValue { None, Notify }

    public sealed class GattDeviceServicesResult {
        public GattCommunicationStatus Status => GattCommunicationStatus.Success;
        public IReadOnlyList<GattDeviceService> Services => [];
    }

    public sealed class GattCharacteristicsResult {
        public GattCommunicationStatus Status => GattCommunicationStatus.Success;
        public IReadOnlyList<GattCharacteristic> Characteristics => [];
    }

    public sealed class GattDeviceService {
        public Guid Uuid { get; } = Guid.NewGuid();
        public Task<GattCharacteristicsResult> GetCharacteristicsForUuidAsync(Guid uuid) => Task.FromResult(new GattCharacteristicsResult());
    }

    public sealed class GattValueChangedEventArgs(IBuffer buffer) : EventArgs {
        public IBuffer CharacteristicValue => buffer;
    }

    public sealed class GattCharacteristic {
        public Guid Uuid { get; } = Guid.NewGuid();
        public NativeOperation Start { get; } = new();
        public NativeOperation Stop { get; } = new();
        public GattCommunicationStatus NextStartStatus { get; set; } = GattCommunicationStatus.Success;
        public bool NativeActive { get; private set; }
        public Handlers<Action<GattCharacteristic, GattValueChangedEventArgs>> Handlers { get; } = new();
        public event Action<GattCharacteristic, GattValueChangedEventArgs> ValueChanged {
            add => Handlers.Add(value);
            remove => Handlers.Remove(value);
        }
        public Task<GattCommunicationStatus> WriteValueAsync(IBuffer buffer, GattWriteOption option)
            => Task.FromResult(GattCommunicationStatus.Success);
        public async Task<GattCommunicationStatus> WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue value) {
            if (value == GattClientCharacteristicConfigurationDescriptorValue.Notify) {
                NativeActive = true;
                await Start.Invoke();
                var status = NextStartStatus;
                NextStartStatus = GattCommunicationStatus.Success;
                return status;
            }
            await Stop.Invoke();
            NativeActive = false;
            return GattCommunicationStatus.Success;
        }
        public void Emit(byte[] bytes) {
            foreach (var handler in Handlers.Snapshot()) handler(this, new GattValueChangedEventArgs(new Windows.Storage.Streams.Buffer(bytes)));
        }
    }
}

namespace Windows.Storage.Streams {
    public interface IBuffer { byte[] Bytes { get; } }
    public sealed class Buffer(byte[] bytes) : IBuffer { public byte[] Bytes => bytes; }
    public sealed class DataWriter : IDisposable {
        private byte[] _bytes = [];
        public void WriteBytes(byte[] bytes) => _bytes = bytes.ToArray();
        public IBuffer DetachBuffer() => new Buffer(_bytes);
        public void Dispose() { }
    }
    public sealed class DataReader(IBuffer buffer) : IDisposable {
        public uint UnconsumedBufferLength => (uint)buffer.Bytes.Length;
        public static DataReader FromBuffer(IBuffer buffer) => new(buffer);
        public void ReadBytes(byte[] bytes) => buffer.Bytes.CopyTo(bytes, 0);
        public void Dispose() { }
    }
}
#endif