using MiBandHR.Core.Bluetooth;
using MiBandHR.Core.Devices;

namespace MiBandHR.GuiChecks;

internal sealed class FakeDevice : Dummy_Device {
    private int _activeConnects;
    private int _maxConcurrentConnects;
    private int _startFailures;

    public TaskCompletionSource? ConnectGate { get; set; }
    public TaskCompletionSource? StartGate { get; set; }
    public int ConnectCalls;
    public int StartCalls;
    public int DisposeCalls;
    public int OutputCreated;
    public int OutputDisposed;
    public int MaxConcurrentConnects => Volatile.Read(ref _maxConcurrentConnects);

    public int StartFailures {
        set => Volatile.Write(ref _startFailures, value);
    }

    public override async Task ConnectAsync(CancellationToken cancellationToken = default) {
        Interlocked.Increment(ref ConnectCalls);
        var active = Interlocked.Increment(ref _activeConnects);
        UpdateMaximum(active);
        try {
            if (ConnectGate != null) await ConnectGate.Task;
            await base.ConnectAsync(cancellationToken);
        } finally {
            Interlocked.Decrement(ref _activeConnects);
        }
    }

    public override async Task StartHeartrateMonitorAsync(bool continuous = false,
        CancellationToken cancellationToken = default) {
        Interlocked.Increment(ref StartCalls);
        if (StartGate != null) await StartGate.Task;
        if (ConsumeStartFailure()) throw new InvalidOperationException("Injected monitoring startup failure.");
        await base.StartHeartrateMonitorAsync(continuous, cancellationToken);
    }

    public override ValueTask DisposeAsync() {
        Interlocked.Increment(ref DisposeCalls);
        return base.DisposeAsync();
    }

    private bool ConsumeStartFailure() {
        while (true) {
            var remaining = Volatile.Read(ref _startFailures);
            if (remaining <= 0) return false;
            if (Interlocked.CompareExchange(ref _startFailures, remaining - 1, remaining) == remaining) return true;
        }
    }

    private void UpdateMaximum(int value) {
        while (true) {
            var current = Volatile.Read(ref _maxConcurrentConnects);
            if (current >= value || Interlocked.CompareExchange(ref _maxConcurrentConnects, value, current) == current) return;
        }
    }
}

internal sealed class FakeOutput(FakeDevice device) : IAsyncDisposable {
    private int _disposed;

    public ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Increment(ref device.OutputDisposed);
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeBluetoothDevice(string name) : IBluetoothDevice {
    public string Id { get; } = Guid.NewGuid().ToString();
    public string Name { get; } = name;
    public bool IsConnected { get; private set; }
    public event EventHandler? Disconnected;

    public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IGattService?> GetPrimaryServiceAsync(Guid uuid) => Task.FromResult<IGattService?>(null);
    public Task<IReadOnlyList<IGattService>> GetPrimaryServicesAsync()
        => Task.FromResult<IReadOnlyList<IGattService>>([]);
    public Task DisconnectAsync() {
        IsConnected = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}

internal sealed class FakeScanner(bool emitMatches, TaskCompletionSource? release = null) : IBluetoothScanner {
    private EventHandler<IBluetoothDevice>? _deviceFound;
    public int SubscriptionCount { get; private set; }
    public int ScanCalls;

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
        _deviceFound?.Invoke(this, new FakeBluetoothDevice("not-the-configured-band"));
        if (emitMatches) {
            _deviceFound?.Invoke(this, new FakeBluetoothDevice("Test Band"));
            _deviceFound?.Invoke(this, new FakeBluetoothDevice("Test Band"));
        }

        if (release != null) await release.Task;
    }

    public void EmitLateMatchingDevice() => _deviceFound?.Invoke(this, new FakeBluetoothDevice("Test Band"));
}