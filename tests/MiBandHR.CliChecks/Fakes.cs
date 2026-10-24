using MiBandHR.Cli;
using MiBandHR.Core.Bluetooth;
using MiBandHR.Core.Devices;
using MiBandHR.Core.Configuration;

namespace MiBandHR.CliChecks;

internal sealed class FakeScanner(params FakeBluetoothDevice[] devices) : IBluetoothScanner {
    private EventHandler<IBluetoothDevice>? _deviceFound;

    public Func<CancellationToken, Task> ScanOperation { get; set; } = _ => Task.CompletedTask;
    public int ScanCalls;
    public int SubscriptionCount { get; private set; }
    public TaskCompletionSource ScanStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event EventHandler<IBluetoothDevice>? DeviceFound {
        add {
            _deviceFound += value;
            SubscriptionCount++;
        }
        remove {
            _deviceFound -= value;
            SubscriptionCount--;
        }
    }

    public async Task ScanForDevicesAsync(CancellationToken cancellationToken = default) {
        Interlocked.Increment(ref ScanCalls);
        foreach (var device in devices) Emit(device);
        ScanStarted.TrySetResult();
        await ScanOperation(cancellationToken);
    }

    public void Emit(IBluetoothDevice device) => _deviceFound?.Invoke(this, device);
}

internal sealed class FakeBluetoothDevice : IBluetoothDevice {
    public FakeBluetoothDevice(string id = "test-band", bool supportsHeartRateService = true) {
        Id = id;
        ServicesOperation = () => Task.FromResult<IReadOnlyList<IGattService>>(supportsHeartRateService
            ? [new FakeGattService(new Guid("0000180d-0000-1000-8000-00805f9b34fb"))]
            : []);
    }

    public string Id { get; }
    public string Name { get; } = "Test Band";
    public bool IsConnected { get; private set; }
    public int ConnectCalls;
    public int ServiceCalls;
    public int DisconnectCalls;
    public Func<Task<IReadOnlyList<IGattService>>> ServicesOperation { get; set; }

    public event EventHandler? Disconnected;

    public Task ConnectAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref ConnectCalls);
        IsConnected = true;
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<IGattService>> GetPrimaryServicesAsync() {
        Interlocked.Increment(ref ServiceCalls);
        IsConnected = true;
        return await ServicesOperation();
    }

    public Task<IGattService?> GetPrimaryServiceAsync(Guid uuid) =>
        Task.FromResult<IGattService?>(null);

    public Task DisconnectAsync() {
        Interlocked.Increment(ref DisconnectCalls);
        IsConnected = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}

internal sealed class FakeGattService(Guid uuid) : IGattService {
    public Guid Uuid { get; } = uuid;
    public Task<IGattCharacteristic?> GetCharacteristicAsync(Guid characteristicUuid) =>
        Task.FromResult<IGattCharacteristic?>(null);
}

internal sealed class FakeDevice : Dummy_Device {
    public int ConnectCalls;
    public int StartCalls;
    public int StopCalls;
    public int DisposeCalls;
    public TaskCompletionSource ConnectStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource StartStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource? ConnectGate { get; set; }
    public TaskCompletionSource? StartGate { get; set; }
    public Exception? ConnectFailure { get; set; }
    public Exception? StartFailure { get; set; }
    public Exception? StopFailure { get; set; }
    public Exception? DisposeFailure { get; set; }

    public override async Task ConnectAsync(CancellationToken cancellationToken = default) {
        Interlocked.Increment(ref ConnectCalls);
        ConnectStarted.TrySetResult();
        if (ConnectGate != null) await ConnectGate.Task;
        if (ConnectFailure != null) throw ConnectFailure;
        await base.ConnectAsync(cancellationToken);
    }

    public override async Task StartHeartrateMonitorAsync(bool continuous = false,
        CancellationToken cancellationToken = default) {
        Interlocked.Increment(ref StartCalls);
        StartStarted.TrySetResult();
        if (StartGate != null) await StartGate.Task;
        if (StartFailure != null) throw StartFailure;
        await base.StartHeartrateMonitorAsync(continuous, cancellationToken);
    }

    public override async Task StopHeartrateMonitorAsync(CancellationToken cancellationToken = default) {
        Interlocked.Increment(ref StopCalls);
        await base.StopHeartrateMonitorAsync(cancellationToken);
        if (StopFailure != null) throw StopFailure;
    }

    public override async ValueTask DisposeAsync() {
        Interlocked.Increment(ref DisposeCalls);
        await base.DisposeAsync();
        if (DisposeFailure != null) throw DisposeFailure;
    }
}

internal sealed class FakeCliConsole : ICliConsole {
    public TaskCompletionSource StopGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool WaitForStop { get; set; }
    public List<string> Errors { get; } = [];
    public int SelectionCalls;
    public int ModelSelectionCalls;
    public Func<int>? ScannerSubscriptionCount { get; set; }
    public int SubscriptionCountAtSelection { get; private set; } = -1;
    public IReadOnlyList<IBluetoothDevice> DevicesAtSelection { get; private set; } = [];
    public Func<IReadOnlyList<IBluetoothDevice>, IBluetoothDevice>? DeviceSelector { get; set; }

    public void ShowBanner() { }
    public void WriteInfo(string message) { }
    public void WriteError(string message) => Errors.Add(message);

    public async Task ShowScanningAsync(Func<IReadOnlyList<IBluetoothDevice>> getDevices, Task scanTask,
        CancellationToken cancellationToken) {
        var canceled = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        var completed = await Task.WhenAny(scanTask, canceled);
        if (completed != scanTask) cancellationToken.ThrowIfCancellationRequested();
        await scanTask;
    }

    public Task<IBluetoothDevice> SelectBluetoothDeviceAsync(IReadOnlyList<IBluetoothDevice> devices,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref SelectionCalls);
        DevicesAtSelection = devices.ToArray();
        SubscriptionCountAtSelection = ScannerSubscriptionCount?.Invoke() ?? -1;
        return Task.FromResult(DeviceSelector?.Invoke(DevicesAtSelection) ?? DevicesAtSelection[0]);
    }

    public Task<DeviceModel> SelectDeviceModelAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref ModelSelectionCalls);
        return Task.FromResult(DeviceModel.Dummy);
    }

    public Task<string> AskAuthenticationKeyAsync(CancellationToken cancellationToken) =>
        Task.FromResult("test-auth-key");

    public async Task WaitForStopAsync(Device device, CancellationToken cancellationToken) {
        if (WaitForStop) await StopGate.Task.WaitAsync(cancellationToken);
    }
}

internal sealed class FakeOutput : IAsyncDisposable {
    private int _disposeCalls;

    public int DisposeCalls => Volatile.Read(ref _disposeCalls);
    public Exception? DisposeFailure { get; set; }

    public ValueTask DisposeAsync() {
        Interlocked.Increment(ref _disposeCalls);
        return DisposeFailure == null
            ? ValueTask.CompletedTask
            : ValueTask.FromException(DisposeFailure);
    }
}

internal sealed class FakeOutputFactory : ICliOutputFactory {
    public List<FakeOutput> Outputs { get; } = [];
    public bool FailAfterFirstOutput { get; set; }
    public Exception? FirstOutputDisposeFailure { get; set; }
    public int CreateCalls;
    public Func<Device, bool>? BeforeCreate { get; set; }

    public IEnumerable<IAsyncDisposable> CreateOutputs(Device device, AppConfig config) {
        Interlocked.Increment(ref CreateCalls);
        if (BeforeCreate != null && !BeforeCreate(device)) {
            throw new InvalidOperationException("Outputs were created before monitoring started.");
        }

        var first = new FakeOutput { DisposeFailure = FirstOutputDisposeFailure };
        Outputs.Add(first);
        yield return first;
        if (FailAfterFirstOutput) throw new InvalidOperationException("Injected output creation failure.");

        var second = new FakeOutput();
        Outputs.Add(second);
        yield return second;
    }
}

internal sealed class Scenario {
    private readonly string _configPath = Path.Combine(AppContext.BaseDirectory, $"cli-check-{Guid.NewGuid():N}.toml");

    public FakeScanner Scanner { get; }
    public FakeCliConsole Console { get; } = new();
    public FakeDevice Device { get; } = new();
    public FakeOutputFactory Outputs { get; }
    public List<IBluetoothDevice> FactoryDevices { get; } = [];
    private readonly App _app;

    public Scenario(FakeScanner? scanner = null, FakeOutputFactory? outputs = null) {
        Scanner = scanner ?? new FakeScanner(new FakeBluetoothDevice());
        Outputs = outputs ?? new FakeOutputFactory();
        Console.ScannerSubscriptionCount = () => Scanner.SubscriptionCount;
        _app = new App(_configPath, () => Scanner, Console,
            (bluetoothDevice, _, _) => {
                FactoryDevices.Add(bluetoothDevice);
                return Device;
            }, Outputs);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default) {
        try {
            await _app.RunAsync(cancellationToken);
        } finally {
            if (File.Exists(_configPath)) File.Delete(_configPath);
        }
    }
}