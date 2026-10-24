using MiBandHR.Core.Devices;

namespace MiBandHR.CliChecks;

internal static class CliChecks {
    public static async Task SuccessCleansUp() {
        var bluetoothDevice = new FakeBluetoothDevice();
        var scanner = new FakeScanner(bluetoothDevice);
        var outputs = new FakeOutputFactory {
            BeforeCreate = device => device.HeartrateMonitorStarted
        };
        var scenario = new Scenario(scanner, outputs);

        await scenario.RunAsync();

        Check.True(scanner.ScanCalls == 1, "Expected one scan.");
        Check.True(scanner.SubscriptionCount == 0, "Scanner handler was not detached.");
        Check.True(bluetoothDevice.ServiceCalls == 0 && bluetoothDevice.ConnectCalls == 0 &&
                   bluetoothDevice.DisconnectCalls == 0,
            "Bluetooth operations occurred during passive scanning.");
        Check.True(scenario.FactoryDevices.Count == 1 && ReferenceEquals(scenario.FactoryDevices[0], bluetoothDevice),
            "The selected discovery result was not the only device passed to the factory.");
        Check.True(scenario.Console.SubscriptionCountAtSelection == 0,
            "Device selection started before the discovery handler was detached.");
        Check.True(scenario.Device.ConnectCalls == 1 && scenario.Device.StartCalls == 1,
            "Expected one awaited connect and one monitoring start.");
        Check.True(scenario.Device.StopCalls == 1 && scenario.Device.DisposeCalls == 1,
            "Device stop/disposal did not complete.");
        Check.True(outputs.CreateCalls == 1 && outputs.Outputs.Count == 2 &&
                   outputs.Outputs.All(output => output.DisposeCalls == 1),
            "Outputs were not created after startup and disposed exactly once.");
    }

    public static async Task DuplicateAndLateDiscovery() {
        var bluetoothDevice = new FakeBluetoothDevice("duplicate-id");
        var duplicateDevice = new FakeBluetoothDevice("duplicate-id");
        var scanner = new FakeScanner(bluetoothDevice, duplicateDevice);
        var scenario = new Scenario(scanner);

        await scenario.RunAsync();

        Check.True(scenario.Console.DevicesAtSelection.Count == 1 &&
                   ReferenceEquals(scenario.Console.DevicesAtSelection[0], bluetoothDevice),
            "Duplicate IDs were not deduplicated to their first discovered device.");
        Check.True(scanner.SubscriptionCount == 0, "Scanner event handler remained attached.");
        var lateDevice = new FakeBluetoothDevice("late-device");
        scanner.Emit(lateDevice);
        await Task.Delay(30);
        Check.True(lateDevice.ServiceCalls == 0 && lateDevice.ConnectCalls == 0 &&
                   lateDevice.DisconnectCalls == 0 && scenario.Console.DevicesAtSelection.Count == 1,
            "Late discovery was processed after scan cleanup.");
        Check.True(bluetoothDevice.ServiceCalls == 0 && duplicateDevice.ServiceCalls == 0 &&
                   bluetoothDevice.ConnectCalls == 0 && duplicateDevice.ConnectCalls == 0 &&
                   bluetoothDevice.DisconnectCalls == 0 && duplicateDevice.DisconnectCalls == 0,
            "Duplicate discovery caused Bluetooth operations.");
    }

    public static async Task PassiveDiscoveryIncludesUnrelatedDevices() {
        var band = new FakeBluetoothDevice("band");
        var unrelatedDevice = new FakeBluetoothDevice("speaker", supportsHeartRateService: false);
        var scanner = new FakeScanner(band, unrelatedDevice);
        var scenario = new Scenario(scanner);
        scenario.Console.DeviceSelector = devices => devices.Single(device => ReferenceEquals(device, unrelatedDevice));

        await scenario.RunAsync();

        Check.True(scenario.Console.DevicesAtSelection.Count == 2 &&
                   scenario.Console.DevicesAtSelection.Contains(band) &&
                   scenario.Console.DevicesAtSelection.Contains(unrelatedDevice),
            "Discovery filtered a device before presenting the selection list.");
        Check.True(scenario.Console.SubscriptionCountAtSelection == 0,
            "Device selection started before the discovery handler was detached.");
        Check.True(scenario.FactoryDevices.Count == 1 && ReferenceEquals(scenario.FactoryDevices[0], unrelatedDevice),
            "The device factory did not receive only the selected device.");
        Check.True(new[] { band, unrelatedDevice }.All(device =>
                device.ServiceCalls == 0 && device.ConnectCalls == 0 && device.DisconnectCalls == 0),
            "Passive discovery performed a Bluetooth connection or service query.");
    }

    public static async Task CancellationWaitsForScan() {
        var scanGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var discoveredDevice = new FakeBluetoothDevice("discovered-before-cancellation");
        var scanner = new FakeScanner(discoveredDevice) { ScanOperation = _ => scanGate.Task };
        var scenario = new Scenario(scanner);
        using var cancellation = new CancellationTokenSource();
        var run = scenario.RunAsync(cancellation.Token);

        await scanner.ScanStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await Check.Until(() => scanner.SubscriptionCount == 0, "Scanner handler was not detached on cancellation.");
        await Check.Pending(run, "Run abandoned a scan that had not completed.");
        scanGate.TrySetResult();
        await Check.ThrowsAsync<OperationCanceledException>(run);
        Check.True(scanner.ScanCalls == 1, "Cancellation unexpectedly retried the scan.");
        Check.True(scenario.Console.SelectionCalls == 0 && scenario.FactoryDevices.Count == 0,
            "Cancellation proceeded to device selection or construction.");
        Check.True(scenario.Device.ConnectCalls == 0 && discoveredDevice.ServiceCalls == 0 &&
                   discoveredDevice.ConnectCalls == 0 && discoveredDevice.DisconnectCalls == 0,
            "Cancellation proceeded to a Bluetooth operation or connection selection.");
    }

    public static async Task CancellationDuringConnect() {
        var scanner = new FakeScanner(new FakeBluetoothDevice());
        var scenario = new Scenario(scanner);
        var connectGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scenario.Device.ConnectGate = connectGate;
        using var cancellation = new CancellationTokenSource();
        var run = scenario.RunAsync(cancellation.Token);

        await scenario.Device.ConnectStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await Check.Pending(run, "Run stopped waiting for the owned connection attempt.");
        connectGate.TrySetResult();
        await Check.ThrowsAsync<OperationCanceledException>(run);
        Check.True(scenario.Device.ConnectCalls == 1 && scenario.Device.StartCalls == 0,
            "Connection cancellation retried or proceeded to monitoring.");
        Check.True(scenario.Device.DisposeCalls == 1, "Device was not disposed after cancelled connection.");
    }

    public static async Task CancellationDuringMonitoringStart() {
        var scenario = new Scenario();
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scenario.Device.StartGate = startGate;
        using var cancellation = new CancellationTokenSource();
        var run = scenario.RunAsync(cancellation.Token);

        await scenario.Device.StartStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await Check.Pending(run, "Run stopped waiting for monitoring startup.");
        startGate.TrySetResult();
        await Check.ThrowsAsync<OperationCanceledException>(run);
        Check.True(scenario.Device.StopCalls == 1 && scenario.Device.DisposeCalls == 1,
            "Partial monitoring startup was not cleaned up.");
        Check.True(scenario.Outputs.CreateCalls == 0, "Outputs were created before monitoring startup completed.");
    }

    public static async Task CancellationWhileMonitoring() {
        var scenario = new Scenario();
        scenario.Console.WaitForStop = true;
        using var cancellation = new CancellationTokenSource();
        var run = scenario.RunAsync(cancellation.Token);

        await scenario.Device.StartStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Check.Until(() => scenario.Outputs.CreateCalls == 1, "Outputs were not created after monitoring started.");
        cancellation.Cancel();
        await Check.ThrowsAsync<OperationCanceledException>(run);
        Check.True(scenario.Device.StopCalls == 1 && scenario.Device.DisposeCalls == 1,
            "Cancellation during monitoring did not stop/dispose the device.");
    }

    public static async Task ConnectionFailureCleansUp() {
        var scenario = new Scenario();
        scenario.Device.ConnectFailure = new InvalidOperationException("injected connect failure");

        await Check.ThrowsAsync<InvalidOperationException>(scenario.RunAsync());

        Check.True(scenario.Device.ConnectCalls == 1, "CLI retried a failed connection.");
        Check.True(scenario.Device.DisposeCalls == 1, "Failed connection did not dispose the device.");
        Check.True(scenario.Device.StartCalls == 0, "Monitoring started after connection failure.");
    }

    public static async Task MonitoringFailureCleansUp() {
        var scenario = new Scenario();
        scenario.Device.StartFailure = new InvalidOperationException("injected start failure");

        await Check.ThrowsAsync<InvalidOperationException>(scenario.RunAsync());

        Check.True(scenario.Device.ConnectCalls == 1 && scenario.Device.StartCalls == 1,
            "Unexpected retry during monitoring startup failure.");
        Check.True(scenario.Device.StopCalls == 1 && scenario.Device.DisposeCalls == 1,
            "Monitoring startup failure did not clean up the device.");
        Check.True(scenario.Outputs.CreateCalls == 0, "Outputs were created after startup failure.");
    }

    public static async Task OutputFailureStillCleansEverything() {
        var outputs = new FakeOutputFactory {
            FailAfterFirstOutput = true,
            FirstOutputDisposeFailure = new InvalidOperationException("output cleanup failure")
        };
        var scenario = new Scenario(outputs: outputs);
        scenario.Device.StopFailure = new InvalidOperationException("monitor cleanup failure");
        scenario.Device.DisposeFailure = new InvalidOperationException("device cleanup failure");

        await Check.ThrowsAsync<InvalidOperationException>(scenario.RunAsync());

        Check.True(outputs.Outputs.Count == 1 && outputs.Outputs[0].DisposeCalls == 1,
            "Partially created output was not disposed despite its cleanup failure.");
        Check.True(scenario.Device.StopCalls == 1 && scenario.Device.DisposeCalls == 1,
            "An earlier cleanup error skipped device disposal.");
        Check.True(scenario.Console.Errors.Count >= 3, "Cleanup failures were not observed.");
    }

    public static async Task ScanExitPathsDetach() {
        var emptyScanner = new FakeScanner();
        var emptyScenario = new Scenario(emptyScanner);
        await emptyScenario.RunAsync();
        Check.True(emptyScanner.SubscriptionCount == 0 && emptyScenario.Device.ConnectCalls == 0,
            "Empty scan failed to detach or unexpectedly connected a device.");
        Check.True(emptyScenario.Console.SelectionCalls == 0, "Prompt was shown after an empty scan.");

        var failedScanner = new FakeScanner { ScanOperation = _ => Task.FromException(new IOException("scan failed")) };
        var failedScenario = new Scenario(failedScanner);
        await Check.ThrowsAsync<IOException>(failedScenario.RunAsync());
        Check.True(failedScanner.SubscriptionCount == 0 && failedScenario.Device.DisposeCalls == 0,
            "Failed scan did not detach or incorrectly created a device.");
    }
}