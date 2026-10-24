using System.Collections.Concurrent;
using MiBandHR.Core.Bluetooth;
using MiBandHR.Core.Configuration;
using MiBandHR.Core.Devices;
using MiBandHR.Core.Extras;

namespace MiBandHR.Cli;

public class App {
    private readonly string? _configPath;
    private readonly Func<IBluetoothScanner> _scannerFactory;
    private readonly ICliConsole _console;
    private readonly Func<IBluetoothDevice, DeviceModel, string?, Device> _deviceFactory;
    private readonly ICliOutputFactory _outputFactory;

    public App(string? configPath) {
        _configPath = configPath;
        _scannerFactory = () => BluetoothScannerFactory.Create(
            ConfigurationManager.Instance.CurrentConfig.Device.BluetoothBackend);
        _console = new CliConsole();
        _deviceFactory = CreateDevice;
        _outputFactory = new CliOutputFactory();
    }

    internal App(string? configPath, Func<IBluetoothScanner> scannerFactory, ICliConsole console,
        Func<IBluetoothDevice, DeviceModel, string?, Device> deviceFactory, ICliOutputFactory outputFactory) {
        _configPath = configPath;
        _scannerFactory = scannerFactory;
        _console = console;
        _deviceFactory = deviceFactory;
        _outputFactory = outputFactory;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default) {
        ConfigurationManager.Initialize(_configPath);
        _console.ShowBanner();

        IBluetoothScanner? scanner = null;
        EventHandler<IBluetoothDevice>? deviceFoundHandler = null;
        Task? scanTask = null;
        var discoveredDevices = new ConcurrentDictionary<string, IBluetoothDevice>();
        var discoveryLock = new object();
        var acceptingDiscoveries = true;
        var outputs = new List<IAsyncDisposable>();
        Device? device = null;
        var monitoringEnded = false;

        try {
            scanner = _scannerFactory();
            deviceFoundHandler = (_, discoveredDevice) => {
                lock (discoveryLock) {
                    if (!acceptingDiscoveries) return;
                    discoveredDevices.TryAdd(discoveredDevice.Id, discoveredDevice);
                }
            };
            scanner.DeviceFound += deviceFoundHandler;

            _console.WriteInfo("Scanning for devices...");
            scanTask = scanner.ScanForDevicesAsync(cancellationToken);
            await _console.ShowScanningAsync(() => discoveredDevices.Values.ToArray(), scanTask, cancellationToken);
            await scanTask.ConfigureAwait(false);

            lock (discoveryLock) acceptingDiscoveries = false;
            DetachDiscoveryHandler(scanner, deviceFoundHandler);
            deviceFoundHandler = null;
            cancellationToken.ThrowIfCancellationRequested();

            var deviceList = discoveredDevices.Values.ToArray();
            if (deviceList.Length == 0) {
                _console.WriteError("No devices found.");
                return;
            }

            var selectedBluetoothDevice = await _console.SelectBluetoothDeviceAsync(deviceList, cancellationToken);
            var modelChoice = await _console.SelectDeviceModelAsync(cancellationToken);
            var authKey = modelChoice == DeviceModel.Miband4_5
                ? await _console.AskAuthenticationKeyAsync(cancellationToken)
                : null;

            device = _deviceFactory(selectedBluetoothDevice, modelChoice, authKey);
            _console.WriteInfo($"Connecting to {device.Name}...");
            await device.ConnectAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            _console.WriteInfo("Connected and Authenticated!");
            await device.StartHeartrateMonitorAsync(
                ConfigurationManager.Instance.CurrentConfig.Monitoring.ContinuousMode, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var output in _outputFactory.CreateOutputs(device, ConfigurationManager.Instance.CurrentConfig)) {
                outputs.Add(output);
            }

            await _console.WaitForStopAsync(device, cancellationToken);
            monitoringEnded = true;
        } finally {
            lock (discoveryLock) acceptingDiscoveries = false;
            DetachDiscoveryHandler(scanner, deviceFoundHandler);

            await TryCleanupAsync("Bluetooth scan", async () => {
                if (scanTask == null) return;
                try {
                    await scanTask.ConfigureAwait(false);
                } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    // Cancellation was requested by the caller; the native scan is now drained.
                }
            });

            if (device != null) {
                await TryCleanupAsync("heart rate monitoring", () => device.StopHeartrateMonitorAsync());
            }

            for (var index = outputs.Count - 1; index >= 0; index--) {
                var output = outputs[index];
                await TryCleanupAsync("heart rate output", () => output.DisposeAsync().AsTask());
            }

            if (device != null) {
                await TryCleanupAsync("device", () => device.DisposeAsync().AsTask());
            }
        }

        if (monitoringEnded) _console.WriteInfo("Disconnected.");
    }

    private static Device CreateDevice(IBluetoothDevice bluetoothDevice, DeviceModel model, string? authKey) {
        return model switch {
            DeviceModel.Miband2_3 => new MiBand2_3_Device(bluetoothDevice),
            DeviceModel.Miband4_5 => new MiBand4_Device(bluetoothDevice, authKey ?? string.Empty),
            DeviceModel.Dummy => new Dummy_Device(),
            _ => throw new ArgumentOutOfRangeException(nameof(model), model, "Unknown device model.")
        };
    }

    private async Task TryCleanupAsync(string resource, Func<Task> cleanup) {
        try {
            await cleanup().ConfigureAwait(false);
        } catch (Exception exception) {
            WriteErrorSafely($"Unable to clean up {resource}: {exception.Message}");
        }
    }

    private void WriteErrorSafely(string message) {
        try {
            _console.WriteError(message);
        } catch {
            // Logging must not interrupt resource cleanup.
        }
    }

    private void DetachDiscoveryHandler(IBluetoothScanner? scanner, EventHandler<IBluetoothDevice>? handler) {
        if (scanner == null || handler == null) return;
        try {
            scanner.DeviceFound -= handler;
        } catch (Exception exception) {
            WriteErrorSafely($"Unable to detach Bluetooth discovery handler: {exception.Message}");
        }
    }
}
