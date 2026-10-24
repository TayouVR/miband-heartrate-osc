using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using MiBandHR.Avalonia.ViewModels;
using MiBandHR.Avalonia.Views;
using MiBandHR.Core.Bluetooth;
using MiBandHR.Core.Configuration;
using MiBandHR.Core.Devices;

namespace MiBandHR.GuiChecks;

internal static class GuiChecks {
    public static IEnumerable<(string Name, Func<Task> Run)> All() {
        yield return ("log-window settings default, load legacy TOML, and round-trip", LogWindowConfiguration);
        yield return ("log window restores on startup and preserves state through shutdown", LogWindowLifecycle);
        yield return ("async command rejects re-entry, observes failure, and cancels", AsyncCommandReentryAndFailure);
        yield return ("GUI retry is serialized and explicit disconnect suppresses retry", RetryAndDisconnect);
        yield return ("replacement waits for prior connect teardown", ReplacementWaitsForTeardown);
        yield return ("monitor startup failure does not create outputs", StartupFailureDoesNotCreateOutputs);
        yield return ("shutdown waits for monitor startup cleanup", ShutdownWaitsForMonitorStartup);
        yield return ("autoconnect accepts one matching discovery and detaches", AutoConnectAcceptsOneMatch);
        yield return ("late discovery cannot attach during disconnect", LateDiscoveryIsIgnored);
    }

    private static Task LogWindowConfiguration() {
        var originalConfigPath = Path.Combine(AppContext.BaseDirectory, "gui-checks-config.toml");
        var configPath = Path.Combine(Path.GetTempPath(), $"miband-gui-check-{Guid.NewGuid():N}.toml");
        try {
            File.WriteAllText(configPath, "[window]\nwidth = 440\nheight = 220\n");
            ConfigurationManager.Initialize(configPath);
            var config = ConfigurationManager.Instance.CurrentConfig.LogWindow;
            Check.True(!config.IsOpen && config.Width == 520 && config.Height == 400 &&
                       config.Left == null && config.Top == null,
                "Missing log-window settings did not use the closed 520x400 defaults.");
            ConfigurationManager.Instance.SaveConfiguration();
            ConfigurationManager.Initialize(configPath);
            config = ConfigurationManager.Instance.CurrentConfig.LogWindow;
            Check.True(config.Left == null && config.Top == null,
                "Saving a configuration without a log-window position did not preserve its unset coordinates.");

            File.WriteAllText(configPath,
                "[window]\nwidth = 440\nheight = 220\n" +
                "[log_window]\nis_open = true\nwidth = 720\nheight = 540\n");
            ConfigurationManager.Initialize(configPath);
            config = ConfigurationManager.Instance.CurrentConfig.LogWindow;
            Check.True(config.IsOpen && config.Width == 720 && config.Height == 540 &&
                       config.Left == null && config.Top == null,
                "Legacy log-window settings without position did not retain defaults.");

            config.IsOpen = true;
            config.Width = 860;
            config.Height = 620;
            config.Left = -1280;
            config.Top = 64;
            ConfigurationManager.Instance.SaveConfiguration();
            ConfigurationManager.Initialize(configPath);
            config = ConfigurationManager.Instance.CurrentConfig.LogWindow;
            Check.True(config.IsOpen && config.Width == 860 && config.Height == 620 &&
                       config.Left == -1280 && config.Top == 64,
                "Log-window settings did not round-trip through TOML.");
        } finally {
            if (File.Exists(configPath)) File.Delete(configPath);
            ConfigurationManager.Initialize(originalConfigPath);
        }

        return Task.CompletedTask;
    }

    private static Task LogWindowLifecycle() => Task.Run(() => {
        var config = ConfigurationManager.Instance.CurrentConfig;
        config.Device.TryConnectOnStartup = false;
        config.LogWindow.IsOpen = true;
        config.LogWindow.Width = 900;
        config.LogWindow.Height = 650;
        config.LogWindow.Left = 40;
        config.LogWindow.Top = 50;

        using var session = HeadlessUnitTestSession.StartNew(typeof(GuiChecksApplication));
        MainWindow? mainWindow = null;
        MainViewModel? viewModel = null;
        LogWindow? restoredWindow = null;
        session.Dispatch(() => {
            mainWindow = new MainWindow();
            viewModel = (MainViewModel)mainWindow.DataContext!;
            mainWindow.Show();
            restoredWindow = viewModel.CurrentLogWindow;
        }, CancellationToken.None).GetAwaiter().GetResult();

        var logWindow = restoredWindow ?? throw new InvalidOperationException(
            "Saved open state did not restore the log window on startup.");
        var restoredSize = session.Dispatch(() => (logWindow.Width, logWindow.Height),
            CancellationToken.None).GetAwaiter().GetResult();
        Check.True(restoredSize == (900d, 650d),
            "Saved log-window dimensions were not restored.");
        var restoredPosition = session.Dispatch(() => logWindow.Position,
            CancellationToken.None).GetAwaiter().GetResult();
        Check.True(restoredPosition == new PixelPoint(40, 50),
            "Saved log-window position was not restored.");

        session.Dispatch(() => viewModel!.CommandLog.Execute(null), CancellationToken.None)
            .GetAwaiter().GetResult();
        Check.True(ReferenceEquals(logWindow, viewModel!.CurrentLogWindow),
            "Opening the log twice created more than one log window.");

        var positionBeforeClose = session.Dispatch(() => {
            logWindow.Width = 740;
            logWindow.Height = 500;
            logWindow.Position = new PixelPoint(70, 80);
            var position = logWindow.Position;
            logWindow.Close();
            return position;
        }, CancellationToken.None).GetAwaiter().GetResult();
        Check.True(positionBeforeClose == new PixelPoint(70, 80),
            $"Headless window did not retain its moved position before close: {positionBeforeClose}.");
        Check.True(!config.LogWindow.IsOpen && config.LogWindow.Width == 740 && config.LogWindow.Height == 500 &&
                   config.LogWindow.Left == 70 && config.LogWindow.Top == 80,
            $"Manual close did not persist the closed state and final geometry: " +
            $"open={config.LogWindow.IsOpen}, size={config.LogWindow.Width}x{config.LogWindow.Height}, " +
            $"position={config.LogWindow.Left},{config.LogWindow.Top}.");

        session.Dispatch(() => viewModel!.CommandLog.Execute(null), CancellationToken.None)
            .GetAwaiter().GetResult();
        var reopenedWindow = viewModel.CurrentLogWindow ?? throw new InvalidOperationException(
            "The log window could not be reopened after closing.");
        var reopenedPosition = session.Dispatch(() => reopenedWindow.Position,
            CancellationToken.None).GetAwaiter().GetResult();
        Check.True(reopenedPosition == new PixelPoint(70, 80),
            "The saved log-window position was not restored after reopening.");
        session.Dispatch(() => reopenedWindow.Close(), CancellationToken.None).GetAwaiter().GetResult();

        config.LogWindow.Width = 100;
        config.LogWindow.Height = 100;
        config.LogWindow.Left = int.MaxValue;
        config.LogWindow.Top = int.MaxValue;
        session.Dispatch(() => viewModel!.CommandLog.Execute(null), CancellationToken.None)
            .GetAwaiter().GetResult();
        var clampedWindow = viewModel.CurrentLogWindow ?? throw new InvalidOperationException(
            "The log window could not be opened again after closing.");
        var clampedSize = session.Dispatch(() => (clampedWindow.Width, clampedWindow.Height),
            CancellationToken.None).GetAwaiter().GetResult();
        Check.True(clampedSize == (360d, 240d), "Saved dimensions below the minimum were not clamped.");
        var startupLocation = session.Dispatch(() => clampedWindow.WindowStartupLocation,
            CancellationToken.None).GetAwaiter().GetResult();
        Check.True(startupLocation == WindowStartupLocation.CenterOwner,
            "An off-screen saved position did not fall back to owner-centered placement.");
        session.Dispatch(async () => {
            clampedWindow.Position = new PixelPoint(90, 100);
            await viewModel!.ShutdownAsync();
            return true;
        }, CancellationToken.None).GetAwaiter().GetResult();
        Check.True(config.LogWindow.IsOpen,
            "Application shutdown cleared the saved open state instead of restoring it next launch.");
        Check.True(config.LogWindow.Width == 360 && config.LogWindow.Height == 240,
            "Shutdown did not save the clamped log-window dimensions.");
        Check.True(config.LogWindow.Left == 90 && config.LogWindow.Top == 100,
            "Shutdown did not save the final log-window position.");
        Check.True(viewModel.CurrentLogWindow == null,
            "Application shutdown did not close the log window.");
        var mainWindowClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Dispatch(async () => {
            mainWindow!.Closed += (_, _) => mainWindowClosed.TrySetResult();
            mainWindow.Close();
            await mainWindowClosed.Task;
            return true;
        }, CancellationToken.None).GetAwaiter().GetResult();
        var mainWindowIsVisible = session.Dispatch(() => mainWindow!.IsVisible, CancellationToken.None)
            .GetAwaiter().GetResult();
        Check.True(!mainWindowIsVisible, "The headless owner window did not close after the lifecycle check.");
    });

    private static async Task AsyncCommandReentryAndFailure() {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var failures = 0;
        var command = new AsyncRelayCommand(async (_, _) => {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            await release.Task;
            throw new InvalidOperationException("Expected command failure");
        });
        command.ExecutionFailed += _ => Interlocked.Increment(ref failures);

        var execution = command.ExecuteAsync();
        await started.Task;
        Check.True(command.IsExecuting && !command.CanExecute(null), "Command busy state was not published.");
        await command.ExecuteAsync();
        Check.True(calls == 1, "A second invocation re-entered the command.");
        release.TrySetResult();
        await execution;
        Check.True(!command.IsExecuting && command.CanExecute(null), "Busy state was not reset after failure.");
        Check.True(failures == 1, "Command exception was not observed exactly once.");

        var cancellationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellableCommand = new AsyncRelayCommand(async (_, token) => {
            cancellationStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        });
        var canceledExecution = cancellableCommand.ExecuteAsync();
        await cancellationStarted.Task;
        cancellableCommand.Cancel();
        await canceledExecution;
        Check.True(!cancellableCommand.IsExecuting, "Busy state was not reset after cancellation.");
    }

    private static async Task RetryAndDisconnect() {
        var viewModel = CreateViewModel();
        var device = new FakeDevice();
        await viewModel.ReplaceDeviceAsync(device);
        await Check.Until(() => device.ConnectCalls == 1 && device.HeartrateMonitorStarted,
            "Initial connection and monitor startup did not finish.");

        await device.DisconnectAsync();
        await Check.Until(() => device.ConnectCalls >= 2 && device.HeartrateMonitorStarted,
            "Unexpected loss did not trigger recovery.");
        Check.True(device.MaxConcurrentConnects == 1, "GUI started overlapping connection attempts.");

        await viewModel.CommandDisconnect.ExecuteAsync();
        var attemptsAfterDisconnect = device.ConnectCalls;
        await Task.Delay(80);
        Check.True(device.ConnectCalls == attemptsAfterDisconnect, "Explicit disconnect allowed a retry.");
        Check.True(device.DisposeCalls == 1, "Disconnect did not await device disposal.");
        Check.True(device.OutputDisposed == device.OutputCreated, "Output subscriptions were not all disposed.");
        await viewModel.ShutdownAsync();
    }

    private static async Task ReplacementWaitsForTeardown() {
        var viewModel = CreateViewModel();
        var blockedConnect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new FakeDevice { ConnectGate = blockedConnect };
        var second = new FakeDevice();
        await viewModel.ReplaceDeviceAsync(first);
        await Check.Until(() => first.ConnectCalls == 1, "First connection did not start.");

        var replacing = viewModel.ReplaceDeviceAsync(second);
        await Check.Pending(replacing, "Replacement did not wait for non-cancellable connection work.");
        Check.True(second.ConnectCalls == 0, "Replacement started before old connection teardown.");
        blockedConnect.TrySetResult();
        await replacing;
        await Check.Until(() => second.ConnectCalls == 1 && second.HeartrateMonitorStarted,
            "Replacement connection did not start.");
        Check.True(first.DisposeCalls == 1, "Previous device was not disposed before replacement.");
        await viewModel.ShutdownAsync();
    }

    private static async Task StartupFailureDoesNotCreateOutputs() {
        var viewModel = CreateViewModel();
        var device = new FakeDevice { StartFailures = 1 };
        await viewModel.ReplaceDeviceAsync(device);
        await Check.Until(() => device.OutputCreated == 1 && device.HeartrateMonitorStarted,
            "Recovery did not reach successful monitoring startup.");
        Check.True(device.StartCalls >= 2, "Injected startup failure was not retried.");
        Check.True(device.OutputCreated == 1, "Outputs were created before monitoring succeeded.");
        await viewModel.ShutdownAsync();
        Check.True(device.OutputDisposed == device.OutputCreated, "Output was not disposed at shutdown.");
    }

    private static async Task ShutdownWaitsForMonitorStartup() {
        var viewModel = CreateViewModel();
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var device = new FakeDevice { StartGate = startGate };
        await viewModel.ReplaceDeviceAsync(device);
        await Check.Until(() => device.StartCalls == 1, "Monitoring startup did not begin.");

        var shutdown = viewModel.ShutdownAsync();
        await Check.Pending(shutdown, "Shutdown returned before pending monitor startup was cleaned up.");
        startGate.TrySetResult();
        await shutdown;
        Check.True(device.DisposeCalls == 1, "Shutdown did not dispose the connecting device.");
        Check.True(viewModel.Device == null, "Shutdown left a device attached.");
    }

    private static async Task AutoConnectAcceptsOneMatch() {
        ConfigureAutoConnect();
        var scanner = new FakeScanner(emitMatches: true);
        var createdDevices = 0;
        var expectedDevice = new FakeDevice();
        var viewModel = CreateViewModel(scanner, (_, _, _) => {
            Interlocked.Increment(ref createdDevices);
            return expectedDevice;
        });

        await viewModel.CommandAutoConnect.ExecuteAsync();
        await Check.Until(() => expectedDevice.ConnectCalls == 1, "Discovered device did not connect.");
        Check.True(ReferenceEquals(viewModel.Device, expectedDevice), "Unexpected device was attached.");
        Check.True(createdDevices == 1, "Multiple matching advertisements created duplicate devices.");
        Check.True(scanner.SubscriptionCount == 0, "Discovery handler remained attached after selection.");
        await viewModel.ShutdownAsync();
    }

    private static async Task LateDiscoveryIsIgnored() {
        ConfigureAutoConnect();
        var scanGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner = new FakeScanner(emitMatches: false, scanGate);
        var createdDevices = 0;
        var viewModel = CreateViewModel(scanner, (_, _, _) => {
            Interlocked.Increment(ref createdDevices);
            return new FakeDevice();
        });

        var scan = viewModel.CommandAutoConnect.ExecuteAsync();
        await Check.Until(() => scanner.ScanCalls == 1, "Auto scan did not start.");
        var disconnect = viewModel.CommandDisconnect.ExecuteAsync();
        await Check.Pending(disconnect, "Disconnect did not await the outstanding native scan.");
        scanner.EmitLateMatchingDevice();
        scanGate.TrySetResult();
        await Task.WhenAll(scan, disconnect);
        Check.True(viewModel.Device == null && createdDevices == 0,
            "A late discovery attached a device after explicit disconnect.");
        Check.True(scanner.SubscriptionCount == 0, "Canceled scan left its discovery handler attached.");
        await viewModel.ShutdownAsync();
    }

    private static MainViewModel CreateViewModel(FakeScanner? scanner = null,
        Func<IBluetoothDevice, string, string, Device>? autoDeviceFactory = null) {
        return new MainViewModel(
            () => scanner ?? new FakeScanner(emitMatches: false),
            device => {
                if (device is FakeDevice fake) Interlocked.Increment(ref fake.OutputCreated);
                return Task.FromResult<IAsyncDisposable[]>(device is FakeDevice outputDevice
                    ? [new FakeOutput(outputDevice)]
                    : []);
            },
            () => false,
            TimeSpan.FromMilliseconds(20),
            autoDeviceFactory);
    }

    private static void ConfigureAutoConnect() {
        var config = ConfigurationManager.Instance.CurrentConfig.Device;
        config.AutoConnectDeviceName = "Test Band";
        config.AutoConnectDeviceVersion = "3";
    }
}
