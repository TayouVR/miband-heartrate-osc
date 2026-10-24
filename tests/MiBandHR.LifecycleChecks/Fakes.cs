using System.Collections.Concurrent;
using MiBandHR.Core.Bluetooth;
using MiBandHR.Core.Devices;

namespace MiBandHR.LifecycleChecks;

internal sealed class FakeBluetoothDevice : IBluetoothDevice {
    private readonly Dictionary<Guid, IGattService?> _services = new();
    private int _isConnected;

    public string Id => "fake-device";
    public string Name => "Fake Mi Band";
    public bool IsConnected => Volatile.Read(ref _isConnected) != 0;
    public int ConnectCalls;
    public int DisconnectCalls;
    public TaskCompletionSource? ConnectGate { get; set; }
    public TaskCompletionSource? DisconnectGate { get; set; }
    public event EventHandler? Disconnected;

    public void AddService(Guid uuid, IGattService? service) => _services[uuid] = service;

    public async Task ConnectAsync(CancellationToken cancellationToken = default) {
        Interlocked.Increment(ref ConnectCalls);
        cancellationToken.ThrowIfCancellationRequested();
        if (ConnectGate != null) await ConnectGate.Task.ConfigureAwait(false);
        Volatile.Write(ref _isConnected, 1);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public Task<IGattService?> GetPrimaryServiceAsync(Guid uuid) {
        _services.TryGetValue(uuid, out var service);
        return Task.FromResult(service);
    }

    public Task<IReadOnlyList<IGattService>> GetPrimaryServicesAsync()
        => Task.FromResult<IReadOnlyList<IGattService>>(_services.Values.OfType<IGattService>().ToArray());

    public async Task DisconnectAsync() {
        Interlocked.Increment(ref DisconnectCalls);
        if (DisconnectGate != null) await DisconnectGate.Task.ConfigureAwait(false);
        Volatile.Write(ref _isConnected, 0);
    }

    public void RaiseDisconnected() {
        Volatile.Write(ref _isConnected, 0);
        Disconnected?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class FakeGattService(params IGattCharacteristic[] characteristics) : IGattService {
    private readonly Dictionary<Guid, IGattCharacteristic> _characteristics =
        characteristics.ToDictionary(characteristic => characteristic.Uuid);

    public Guid Uuid { get; } = Guid.NewGuid();

    public Task<IGattCharacteristic?> GetCharacteristicAsync(Guid uuid) {
        _characteristics.TryGetValue(uuid, out var characteristic);
        return Task.FromResult(characteristic);
    }
}

internal sealed class FakeGattCharacteristic(Guid uuid) : IGattCharacteristic {
    private readonly object _eventLock = new();
    private EventHandler<GattCharacteristicValueChangedEventArgs>? _handlers;
    private int _notificationsStarted;

    public Guid Uuid { get; } = uuid;
    public int SubscribeCalls;
    public int StartNotificationCalls;
    public int StopNotificationCalls;
    public ConcurrentQueue<byte[]> WritesWithoutResponse { get; } = new();
    public ConcurrentQueue<byte[]> WritesWithResponse { get; } = new();
    public Func<byte[], Exception?>? FailWriteWithoutResponse { get; set; }
    public Func<byte[], Exception?>? FailWriteWithResponse { get; set; }
    public Exception? StartFailure { get; set; }
    public TaskCompletionSource? StartGate { get; set; }
    public int SubscriberCount {
        get {
            lock (_eventLock) return _handlers?.GetInvocationList().Length ?? 0;
        }
    }
    public bool NotificationsStarted => Volatile.Read(ref _notificationsStarted) != 0;

    public EventHandler<GattCharacteristicValueChangedEventArgs>? CaptureHandlers() {
        lock (_eventLock) return _handlers;
    }

    public event EventHandler<GattCharacteristicValueChangedEventArgs>? CharacteristicValueChanged {
        add { lock (_eventLock) _handlers += value; }
        remove { lock (_eventLock) _handlers -= value; }
    }

    public void SubscribeToValueChanges() => Interlocked.Increment(ref SubscribeCalls);

    public Task WriteValueWithResponseAsync(byte[] data) {
        var copy = data.ToArray();
        WritesWithResponse.Enqueue(copy);
        if (FailWriteWithResponse?.Invoke(copy) is { } exception) throw exception;
        return Task.CompletedTask;
    }

    public Task WriteValueWithoutResponseAsync(byte[] data) {
        var copy = data.ToArray();
        WritesWithoutResponse.Enqueue(copy);
        if (FailWriteWithoutResponse?.Invoke(copy) is { } exception) throw exception;
        return Task.CompletedTask;
    }

    public async Task StartNotificationsAsync() {
        Interlocked.Increment(ref StartNotificationCalls);
        if (StartGate != null) await StartGate.Task.ConfigureAwait(false);
        if (StartFailure != null) throw StartFailure;
        Volatile.Write(ref _notificationsStarted, 1);
    }

    public Task StopNotificationsAsync() {
        Interlocked.Increment(ref StopNotificationCalls);
        Volatile.Write(ref _notificationsStarted, 0);
        return Task.CompletedTask;
    }

    public void Emit(params byte[] value) {
        EventHandler<GattCharacteristicValueChangedEventArgs>? handlers;
        lock (_eventLock) handlers = _handlers;
        handlers?.Invoke(this, new GattCharacteristicValueChangedEventArgs(value));
    }

    public void EmitTo(EventHandler<GattCharacteristicValueChangedEventArgs>? handlers, params byte[] value)
        => handlers?.Invoke(this, new GattCharacteristicValueChangedEventArgs(value));
}

internal sealed class TestMiBandDevice(FakeBluetoothDevice device, TimeSpan? authTimeout = null)
    : MiBand4_Device(device, "0123456789ABCDEF0123456789ABCDEF") {
    protected override TimeSpan AuthenticationTimeout => authTimeout ?? TimeSpan.FromSeconds(2);
    protected override TimeSpan HeartrateStartupDelay => TimeSpan.Zero;
    protected override TimeSpan HeartRateKeepAliveInterval => TimeSpan.FromMilliseconds(20);
}

internal sealed class DeviceHarness {
    private static readonly Guid AuthServiceUuid = new("0000fee1-0000-1000-8000-00805f9b34fb");
    private static readonly Guid AuthCharacteristicUuid = new("00000009-0000-3512-2118-0009af100700");
    private static readonly Guid HeartRateServiceUuid = new("0000180d-0000-1000-8000-00805f9b34fb");
    private static readonly Guid ControlPointUuid = new("00002a39-0000-1000-8000-00805f9b34fb");
    private static readonly Guid MeasurementUuid = new("00002a37-0000-1000-8000-00805f9b34fb");

    private DeviceHarness(FakeBluetoothDevice bluetoothDevice, FakeGattCharacteristic authentication,
        FakeGattCharacteristic controlPoint, FakeGattCharacteristic measurement, TestMiBandDevice device) {
        BluetoothDevice = bluetoothDevice;
        Authentication = authentication;
        ControlPoint = controlPoint;
        Measurement = measurement;
        Device = device;
    }

    public FakeBluetoothDevice BluetoothDevice { get; }
    public FakeGattCharacteristic Authentication { get; }
    public FakeGattCharacteristic ControlPoint { get; }
    public FakeGattCharacteristic Measurement { get; }
    public TestMiBandDevice Device { get; }

    public static DeviceHarness Create(TimeSpan? authTimeout = null, bool includeAuthService = true,
        bool includeHeartrateService = true, bool includeAuthCharacteristic = true) {
        var bluetooth = new FakeBluetoothDevice();
        var authentication = new FakeGattCharacteristic(AuthCharacteristicUuid);
        var controlPoint = new FakeGattCharacteristic(ControlPointUuid);
        var measurement = new FakeGattCharacteristic(MeasurementUuid);
        if (includeAuthService) {
            bluetooth.AddService(AuthServiceUuid, includeAuthCharacteristic
                ? new FakeGattService(authentication)
                : new FakeGattService());
        }
        if (includeHeartrateService) {
            bluetooth.AddService(HeartRateServiceUuid, new FakeGattService(controlPoint, measurement));
        }

        return new DeviceHarness(bluetooth, authentication, controlPoint, measurement,
            new TestMiBandDevice(bluetooth, authTimeout));
    }

    public async Task ConnectAuthenticatedAsync() {
        var initialWriteCount = Authentication.WritesWithoutResponse.Count;
        var connectTask = Device.ConnectAsync();
        await Check.Until(() => Authentication.WritesWithoutResponse.Count >= initialWriteCount + 1,
            "Initial authentication command was not written.");
        Authentication.Emit(ChallengeNotification());
        await Check.Until(() => Authentication.WritesWithoutResponse.Count >= initialWriteCount + 2,
            "Encrypted challenge response was not written.");
        Authentication.Emit([0x10, 0x03, 0x01]);
        await connectTask.WaitAsync(Check.Timeout);
    }

    public static byte[] ChallengeNotification() => [0x10, 0x02, 0x01,
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f];
}