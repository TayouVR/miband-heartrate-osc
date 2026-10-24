using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using MiBandHR.Avalonia.Controls;
using MiBandHR.Avalonia.Views;
using MiBandHR.Core;
using MiBandHR.Core.Bluetooth;
using MiBandHR.Core.Configuration;
using MiBandHR.Core.Devices;
using MiBandHR.Core.Extras;

namespace MiBandHR.Avalonia.ViewModels;

public class MainViewModel : ViewModelBase {
    /// <summary>Display wrapper for a selectable Bluetooth backend.</summary>
    public sealed record BluetoothBackendOption(BluetoothBackend Value, string Label) {
        public override string ToString() => Label;
    }

    private readonly Func<IBluetoothScanner> _scannerFactory;
    private readonly Func<IBluetoothDevice, string, string, Device> _autoDeviceFactory;
    private readonly Func<Device, Task<IAsyncDisposable[]>> _outputFactory;
    private readonly Func<bool> _continuousMode;
    private readonly TimeSpan _retryDelay;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _outputGate = new(1, 1);
    private readonly object _workflowSignalLock = new();
    private IBluetoothScanner? _autoConnectBluetooth;
    private CancellationTokenSource? _scanCts;
    private Task? _autoConnectTask;
    private bool _isAutoConnecting;
    private bool _retryEnabled;
    private bool _isShuttingDown;
    private int _replacementGeneration;
    private LogWindow? _logWindow;
    private ConnectionWindow? _connectionWindow;
    private Task? _shutdownTask;
    private Task? _connectionTask;
    private CancellationTokenSource? _connectionCts;
    private Device? _monitoringDevice;
    private TaskCompletionSource<bool>? _connectionLost;
    private Device? _outputsDevice;
    private IAsyncDisposable[] _outputs = [];

    public MainViewModel() {
        _scannerFactory = CreateBluetoothScanner;
        _autoDeviceFactory = CreateAutoDevice;
        _outputFactory = CreateOutputsAsync;
        _continuousMode = () => ConfigurationManager.Instance.CurrentConfig.Monitoring.ContinuousMode;
        _retryDelay = TimeSpan.FromSeconds(2);

        CommandConnect = new AsyncRelayCommand((_, token) => OpenConnectionWindowAsync(token),
            _ => !_isShuttingDown && !IsAutoConnecting && (Device == null || Device.Status == DeviceStatus.Offline));
        CommandAutoConnect = new AsyncRelayCommand((_, token) => StartAutoConnectAsync(token),
            _ => !_isShuttingDown && !IsAutoConnecting && (Device == null || Device.Status == DeviceStatus.Offline));
        CommandDisconnect = new AsyncRelayCommand((_, token) => DisconnectAsync(token),
            _ => !_isShuttingDown && (IsAutoConnecting || Device != null));
        CommandLog = new RelayCommand(_ => OpenLogWindow());
        CommandClearLog = new RelayCommand(_ => LogEntries.Clear());
        CommandSettings = new RelayCommand(_ => OpenSettingsWindow());
        CommandStart = new AsyncRelayCommand((_, token) => StartMonitoringAsync(token),
            _ => !_isShuttingDown && Device is { Status: DeviceStatus.OnlineAuth, HeartrateMonitorStarted: false });
        CommandStop = new AsyncRelayCommand((_, token) => StopMonitoringAsync(token),
            _ => !_isShuttingDown && Device is { HeartrateMonitorStarted: true });
        CommandToggleMonitoring = new AsyncRelayCommand(async (_, token) => {
            if (Device is { HeartrateMonitorStarted: true }) await StopMonitoringAsync(token);
            else await StartMonitoringAsync(token);
        }, _ => CommandStart.CanExecute(null) || CommandStop.CanExecute(null));

        ObserveCommandErrors(CommandConnect, "Unable to open connection dialog");
        ObserveCommandErrors(CommandAutoConnect, "Automatic connection failed");
        ObserveCommandErrors(CommandDisconnect, "Unable to disconnect device");
        ObserveCommandErrors(CommandStart, "Unable to start monitoring");
        ObserveCommandErrors(CommandStop, "Unable to stop monitoring");
        ObserveCommandErrors(CommandToggleMonitoring, "Unable to toggle monitoring");

        // Mirror the Bluetooth layer's log output into the on-screen log panel.
        BluetoothLog.EntryLogged += OnBluetoothLogEntry;
    }

    internal MainViewModel(Func<IBluetoothScanner> scannerFactory, Func<Device, Task<IAsyncDisposable[]>> outputFactory,
        Func<bool> continuousMode, TimeSpan retryDelay,
        Func<IBluetoothDevice, string, string, Device>? autoDeviceFactory = null) : this() {
        _scannerFactory = scannerFactory;
        _outputFactory = outputFactory;
        _continuousMode = continuousMode;
        _retryDelay = retryDelay;
        _autoDeviceFactory = autoDeviceFactory ?? CreateAutoDevice;
    }

    private static IBluetoothScanner CreateBluetoothScanner() {
        try {
            return BluetoothScannerFactory.Create(ConfigurationManager.Instance.CurrentConfig.Device.BluetoothBackend);
        } catch (InvalidOperationException) {
            return BluetoothScannerFactory.CreateDefault();
        }
    }

    private static Device CreateAutoDevice(IBluetoothDevice bluetoothDevice, string version, string authKey) => version switch {
        "2" or "3" => new MiBand2_3_Device(bluetoothDevice),
        "4" or "5" => new MiBand4_Device(bluetoothDevice, authKey),
        _ => throw new InvalidOperationException($"Unsupported Mi Band version '{version}'.")
    };

    private static async Task<IAsyncDisposable[]> CreateOutputsAsync(Device device) {
        var outputs = new List<IAsyncDisposable>();
        try {
            if (ConfigurationManager.Instance.CurrentConfig.Output.FileEnabled) {
                outputs.Add(new DeviceHeartrateFileOutput("heartrate.txt", device));
            }

            if (ConfigurationManager.Instance.CurrentConfig.Output.CsvEnabled) {
                outputs.Add(new DeviceHeartrateCsvOutput("heartrate.csv", device));
            }

            if (ConfigurationManager.Instance.CurrentConfig.Output.OscEnabled) {
                outputs.Add(new DeviceHeartrateOscOutput(device));
            }

            return outputs.ToArray();
        } catch {
            foreach (var output in outputs.AsEnumerable().Reverse()) {
                await output.DisposeAsync();
            }

            throw;
        }
    }

    private static void ObserveCommandErrors(AsyncRelayCommand command, string action) {
        command.ExecutionFailed += exception => BluetoothLog.Error($"{action}: {exception.Message}");
    }


    /// <summary>The window this view model belongs to (set by the view).</summary>
    public MainWindow? View { get; set; }

    /// <summary>Heart rate history data points for sparkline visualization.</summary>
    public ObservableCollection<HeartratePoint> HeartrateHistory { get; } = new();

    /// <summary>Live Bluetooth activity log shown in the side panel.</summary>
    public ObservableCollection<LogEntryViewModel> LogEntries { get; } = new();

    /// <summary>Raised whenever a new log entry is added (used to auto-scroll).</summary>
    public event Action? LogEntryAdded;

    // Cap the log so a long retry loop does not grow memory unbounded.
    private const int MaxLogEntries = 500;

    private void OnBluetoothLogEntry(BluetoothLogEntry entry) {
        // Log events may arrive on background threads; marshal to the UI thread.
        Dispatcher.UIThread.Post(() => {
            if (_isShuttingDown) return;
            LogEntries.Add(new LogEntryViewModel(entry));
            while (LogEntries.Count > MaxLogEntries) {
                LogEntries.RemoveAt(0);
            }

            LogEntryAdded?.Invoke();
        });
    }

    public Device? Device { get; private set; }


    public bool IsAutoConnecting {
        get => _isAutoConnecting;
        private set {
            _isAutoConnecting = value;
            OnPropertyChanged(nameof(IsAutoConnecting));
            OnPropertyChanged(nameof(ShowConnectButtons));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(HasHeartrate));
            OnPropertyChanged(nameof(HeartrateStatusText));
            RaiseCommandStates();
        }
    }

    public bool HasHeartrate => Device is { Status: DeviceStatus.OnlineAuth, Heartrate: > 0 };

    public string HeartrateStatusText {
        get {
            if (IsAutoConnecting) {
                return "Connecting";
            }

            if (Device == null || Device.Status == DeviceStatus.Offline) {
                return "Disconnected";
            }

            return Device.Status switch {
                DeviceStatus.Connecting => "Connecting",
                DeviceStatus.Authenticating or DeviceStatus.OnlineUnauth => "Authenticating",
                DeviceStatus.OnlineAuth => Device.Heartrate > 0 ? $"{Device.Heartrate} bpm" : "Connected - waiting for data",
                _ => "Disconnected"
            };
        }
    }

    public bool IsConnected {
        get => (Device != null && Device.Status != DeviceStatus.Offline) || IsAutoConnecting;
        set {
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(ShowConnectButtons));
        }
    }

    public string StatusText {
        get;
        set {
            field = value;
            OnPropertyChanged(nameof(StatusText));
        }
    } = "Disconnected";

    /// <summary>True while no device is connected or connecting (the manual/auto connect buttons are visible).</summary>
    public bool ShowConnectButtons => !IsConnected && !IsAutoConnecting;

    // ------------------------------------------------------------------
    // Settings (persisted in the shared TOML configuration)
    // ------------------------------------------------------------------

    public bool UseAutoConnect {
        get => ConfigurationManager.Instance.CurrentConfig.Device.UseAutoConnect;
        set {
            ConfigurationManager.Instance.CurrentConfig.Device.UseAutoConnect = value;
            ConfigurationManager.Instance.SaveConfiguration();
            OnPropertyChanged(nameof(UseAutoConnect));
        }
    }

    public bool TryConnectOnStartup {
        get => ConfigurationManager.Instance.CurrentConfig.Device.TryConnectOnStartup;
        set {
            ConfigurationManager.Instance.CurrentConfig.Device.TryConnectOnStartup = value;
            ConfigurationManager.Instance.SaveConfiguration();
            OnPropertyChanged(nameof(TryConnectOnStartup));
        }
    }

    /// <summary>Backends selectable on the current platform.</summary>
    public IReadOnlyList<BluetoothBackendOption> AvailableBackends { get; } =
        global::MiBandHR.Core.Bluetooth.BluetoothScannerFactory.AvailableBackends()
            .Select(b => new BluetoothBackendOption(b, DisplayName(b)))
            .ToList();

    public BluetoothBackendOption? SelectedBluetoothBackend {
        get => AvailableBackends.FirstOrDefault(o =>
                   o.Value == ConfigurationManager.Instance.CurrentConfig.Device.BluetoothBackend)
               ?? AvailableBackends.First();
        set {
            if (value == null) return;
            ConfigurationManager.Instance.CurrentConfig.Device.BluetoothBackend = value.Value;
            ConfigurationManager.Instance.SaveConfiguration();
            OnPropertyChanged(nameof(SelectedBluetoothBackend));
        }
    }

    private static string DisplayName(BluetoothBackend backend) => backend switch {
        BluetoothBackend.Auto => "Auto",
        BluetoothBackend.Windows => "Windows (WinRT)",
        BluetoothBackend.InTheHand => "InTheHand",
        BluetoothBackend.BlueZ => "BlueZ (Linux)",
        BluetoothBackend.Salar => "Salar (Linux)",
        _ => backend.ToString()
    };

    public bool ContinuousMode {
        get => ConfigurationManager.Instance.CurrentConfig.Monitoring.ContinuousMode;
        set {
            ConfigurationManager.Instance.CurrentConfig.Monitoring.ContinuousMode = value;
            ConfigurationManager.Instance.SaveConfiguration();
            OnPropertyChanged(nameof(ContinuousMode));
        }
    }

    public bool EnableFileOutput {
        get => ConfigurationManager.Instance.CurrentConfig.Output.FileEnabled;
        set {
            ConfigurationManager.Instance.CurrentConfig.Output.FileEnabled = value;
            ConfigurationManager.Instance.SaveConfiguration();
            OnPropertyChanged(nameof(EnableFileOutput));
        }
    }

    public bool EnableCsvOutput {
        get => ConfigurationManager.Instance.CurrentConfig.Output.CsvEnabled;
        set {
            ConfigurationManager.Instance.CurrentConfig.Output.CsvEnabled = value;
            ConfigurationManager.Instance.SaveConfiguration();
            OnPropertyChanged(nameof(EnableCsvOutput));
        }
    }

    public bool EnableOscOutput {
        get => ConfigurationManager.Instance.CurrentConfig.Output.OscEnabled;
        set {
            ConfigurationManager.Instance.CurrentConfig.Output.OscEnabled = value;
            ConfigurationManager.Instance.SaveConfiguration();
            OnPropertyChanged(nameof(EnableOscOutput));
        }
    }

    // ------------------------------------------------------------------
    // Commands
    // ------------------------------------------------------------------

    public AsyncRelayCommand CommandConnect { get; }
    public AsyncRelayCommand CommandAutoConnect { get; }
    public AsyncRelayCommand CommandDisconnect { get; }
    public RelayCommand CommandLog { get; }
    public RelayCommand CommandClearLog { get; }
    public RelayCommand CommandSettings { get; }
    public AsyncRelayCommand CommandStart { get; }
    public AsyncRelayCommand CommandStop { get; }
    public AsyncRelayCommand CommandToggleMonitoring { get; }
    public string MonitoringButtonToolTip => Device is { HeartrateMonitorStarted: true } ? "Stop" : "Start";
    public FluentAvalonia.UI.Controls.FASymbol MonitoringButtonIcon => Device is { HeartrateMonitorStarted: true }
        ? FluentAvalonia.UI.Controls.FASymbol.Stop
        : FluentAvalonia.UI.Controls.FASymbol.Play;

    public void RefreshSettings() {
        OnPropertyChanged(nameof(UseAutoConnect));
        OnPropertyChanged(nameof(TryConnectOnStartup));
        OnPropertyChanged(nameof(SelectedBluetoothBackend));
        OnPropertyChanged(nameof(ContinuousMode));
        OnPropertyChanged(nameof(EnableFileOutput));
        OnPropertyChanged(nameof(EnableCsvOutput));
        OnPropertyChanged(nameof(EnableOscOutput));
        PruneHeartrateHistory(DateTime.UtcNow);
    }

    private void OpenLogWindow() {
        if (View == null) return;

        if (_logWindow != null && _logWindow.IsVisible) {
            _logWindow.Activate();
            return;
        }

        _logWindow = new LogWindow(this);
        _logWindow.Closed += (_, _) => _logWindow = null;
        _logWindow.Show(View);
    }

    public void CloseLogWindow() {
        _logWindow?.Close();
        _logWindow = null;
    }

    private async void OpenSettingsWindow() {
        if (View == null) return;

        var dialog = new SettingsWindow(RefreshSettings);
        await dialog.ShowDialog(View);
        RefreshSettings();
    }

    private async Task OpenConnectionWindowAsync(CancellationToken cancellationToken) {
        if (View == null) return;

        var dialog = new ConnectionWindow();
        _connectionWindow = dialog;
        Device? device;
        try {
            device = await dialog.ShowDialog<Device?>(View);
        } finally {
            if (ReferenceEquals(_connectionWindow, dialog)) _connectionWindow = null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (device != null) await ReplaceDeviceAsync(device, cancellationToken);
    }

    private Task StartAutoConnectAsync(CancellationToken cancellationToken) {
        if (_autoConnectTask is { IsCompleted: false }) return _autoConnectTask;
        var generation = Interlocked.Increment(ref _replacementGeneration);
        _autoConnectTask = RunAutoConnectAsync(cancellationToken, generation);
        return _autoConnectTask;
    }

    private async Task RunAutoConnectAsync(CancellationToken cancellationToken, int generation) {
        var scanner = _scannerFactory();
        var scanCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var match = new TaskCompletionSource<IBluetoothDevice>(TaskCreationOptions.RunContinuationsAsynchronously);
        var defaultDeviceName = ConfigurationManager.Instance.CurrentConfig.Device.AutoConnectDeviceName;
        var defaultDeviceVersion = ConfigurationManager.Instance.CurrentConfig.Device.AutoConnectDeviceVersion;
        var defaultDeviceAuthKey = ConfigurationManager.Instance.CurrentConfig.Device.AutoConnectDeviceAuthKey;
        Task? scanTask = null;
        var handlerAttached = false;

        void OnBluetoothAdded(object? sender, IBluetoothDevice args) {
            if (!string.Equals(args.Name, defaultDeviceName, StringComparison.Ordinal)) return;
            match.TrySetResult(args);
        }

        IsAutoConnecting = true;
        DeviceUpdate();
        _autoConnectBluetooth = scanner;
        _scanCts = scanCts;
        scanner.DeviceFound += OnBluetoothAdded;
        handlerAttached = true;

        try {
            scanTask = scanner.ScanForDevicesAsync(scanCts.Token);
            var completed = await Task.WhenAny(match.Task, scanTask);
            if (completed == scanTask && !match.Task.IsCompleted) {
                await AwaitScanCompletionAsync(scanTask);
                return;
            }

            var discovered = await match.Task;
            scanCts.Cancel();
            await AwaitScanCompletionAsync(scanTask);
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _replacementGeneration)) return;

            scanner.DeviceFound -= OnBluetoothAdded;
            handlerAttached = false;
            var device = _autoDeviceFactory(discovered, defaultDeviceVersion, defaultDeviceAuthKey);
            await ReplaceDeviceCoreAsync(device, cancellationToken, scanAlreadyStopped: true,
                generation: generation);
        } catch (OperationCanceledException) when (scanCts.IsCancellationRequested) {
            // The user disconnected, replaced the device, or is closing the window.
        } catch (Exception exception) {
            BluetoothLog.Error($"Automatic connection failed: {exception.Message}");
        } finally {
            scanCts.Cancel();
            if (scanTask != null) await AwaitScanCompletionAsync(scanTask);
            if (handlerAttached) scanner.DeviceFound -= OnBluetoothAdded;
            if (ReferenceEquals(_autoConnectBluetooth, scanner)) _autoConnectBluetooth = null;
            if (ReferenceEquals(_scanCts, scanCts)) _scanCts = null;
            scanCts.Dispose();
            IsAutoConnecting = false;
            DeviceUpdate();
        }
    }

    private static async Task AwaitScanCompletionAsync(Task scanTask) {
        try {
            await scanTask;
        } catch (OperationCanceledException) {
            // Expected after the matching discovery or explicit cancellation.
        } catch (Exception exception) {
            BluetoothLog.Error($"Bluetooth scan failed: {exception.Message}");
        }
    }

    public async Task ReplaceDeviceAsync(Device? replacement, CancellationToken cancellationToken = default) {
        var generation = Interlocked.Increment(ref _replacementGeneration);
        await ReplaceDeviceCoreAsync(replacement, cancellationToken, scanAlreadyStopped: false,
            generation: generation);
    }

    private async Task ReplaceDeviceCoreAsync(Device? replacement, CancellationToken cancellationToken,
        bool scanAlreadyStopped, int generation) {
        if (replacement != null && ReferenceEquals(Device, replacement)) return;

        _retryEnabled = false;
        _connectionCts?.Cancel();
        if (!scanAlreadyStopped) await CancelAndAwaitAutoConnectAsync();
        if (generation != Volatile.Read(ref _replacementGeneration)) return;

        await _lifecycleGate.WaitAsync();
        try {
            if (generation != Volatile.Read(ref _replacementGeneration)) return;

            var oldDevice = Device;
            if (_connectionTask != null) {
                try {
                    await _connectionTask;
                } catch (Exception exception) {
                    BluetoothLog.Error($"Connection workflow stopped with an error: {exception.Message}");
                }
            }

            await DisposeOutputsAsync();
            if (oldDevice != null) {
                oldDevice.PropertyChanged -= OnDevicePropertyChanged;
                Device = null;
                try {
                    await oldDevice.DisposeAsync();
                } catch (Exception exception) {
                    BluetoothLog.Error($"Unable to dispose device {oldDevice.Name}: {exception.Message}");
                }
            }

            if (generation != Volatile.Read(ref _replacementGeneration)) return;

            _connectionCts?.Dispose();
            _connectionCts = null;
            _connectionTask = null;

            if (replacement == null || _isShuttingDown || cancellationToken.IsCancellationRequested) {
                IsAutoConnecting = false;
                HeartrateHistory.Clear();
                DeviceUpdate();
                OnPropertyChanged(nameof(Device));
                return;
            }

            Device = replacement;
            replacement.PropertyChanged += OnDevicePropertyChanged;
            DeviceUpdate();
            OnPropertyChanged(nameof(Device));
            _retryEnabled = true;
            _connectionCts = new CancellationTokenSource();
            _connectionTask = RunConnectionWorkflowAsync(replacement, _connectionCts.Token);
        } finally {
            _lifecycleGate.Release();
        }
    }

    private async Task CancelAndAwaitAutoConnectAsync() {
        _scanCts?.Cancel();
        var autoConnectTask = _autoConnectTask;
        if (autoConnectTask != null) {
            try {
                await autoConnectTask;
            } catch (Exception exception) {
                BluetoothLog.Error($"Automatic scan stopped with an error: {exception.Message}");
            }
        }
    }

    private async Task DisconnectAsync(CancellationToken cancellationToken) {
        await ReplaceDeviceAsync(null, cancellationToken);
        HeartrateHistory.Clear();
        DeviceUpdate();
    }

    public Task ShutdownAsync() {
        if (_shutdownTask != null) return _shutdownTask;
        _isShuttingDown = true;
        RaiseCommandStates();
        CommandConnect.Cancel();
        CommandAutoConnect.Cancel();
        CommandDisconnect.Cancel();
        CommandStart.Cancel();
        CommandStop.Cancel();
        CommandToggleMonitoring.Cancel();
        _connectionWindow?.Close();
        _shutdownTask = ShutdownCoreAsync();
        return _shutdownTask;
    }

    private async Task ShutdownCoreAsync() {
        var commandTasks = new[] {
            CommandConnect.ExecutionTask,
            CommandAutoConnect.ExecutionTask,
            CommandDisconnect.ExecutionTask,
            CommandStart.ExecutionTask,
            CommandStop.ExecutionTask,
            CommandToggleMonitoring.ExecutionTask
        }.Where(task => task != null).Cast<Task>().ToArray();
        await Task.WhenAll(commandTasks);
        await ReplaceDeviceAsync(null);
        await CancelAndAwaitAutoConnectAsync();
        CloseLogWindow();
        BluetoothLog.EntryLogged -= OnBluetoothLogEntry;
    }

    private async Task StartMonitoringAsync(CancellationToken cancellationToken) {
        var device = Device;
        if (device == null) return;
        await device.StartHeartrateMonitorAsync(_continuousMode(), cancellationToken);
        if (ReferenceEquals(Device, device) && device.HeartrateMonitorStarted) {
            await EnsureOutputsAsync(device);
        }
    }

    private async Task StopMonitoringAsync(CancellationToken cancellationToken) {
        var device = Device;
        try {
            if (device != null) await device.StopHeartrateMonitorAsync(cancellationToken);
        } finally {
            await DisposeOutputsAsync();
        }
    }

    private async Task EnsureOutputsAsync(Device device) {
        await _outputGate.WaitAsync();
        try {
            if (!ReferenceEquals(Device, device) || !device.HeartrateMonitorStarted ||
                ReferenceEquals(_outputsDevice, device)) return;

            await DisposeOutputsCoreAsync();
            _outputs = await _outputFactory(device);
            _outputsDevice = device;
        } finally {
            _outputGate.Release();
        }
    }

    private async Task DisposeOutputsAsync() {
        await _outputGate.WaitAsync();
        try {
            await DisposeOutputsCoreAsync();
        } finally {
            _outputGate.Release();
        }
    }

    private async Task DisposeOutputsCoreAsync() {
        var outputs = _outputs;
        _outputs = [];
        _outputsDevice = null;
        foreach (var output in outputs.Reverse()) {
            try {
                await output.DisposeAsync();
            } catch (Exception exception) {
                BluetoothLog.Error($"Unable to dispose a heart rate output: {exception.Message}");
            }
        }
    }

    private async Task RunConnectionWorkflowAsync(Device device, CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested && _retryEnabled && ReferenceEquals(Device, device)) {
            var connectionLost = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try {
                await device.ConnectAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!ReferenceEquals(Device, device)) return;

                await device.StartHeartrateMonitorAsync(_continuousMode(), cancellationToken);
                await EnsureOutputsAsync(device);

                lock (_workflowSignalLock) {
                    _monitoringDevice = device;
                    _connectionLost = connectionLost;
                    if (device.Status == DeviceStatus.Offline) connectionLost.TrySetResult(true);
                }

                await connectionLost.Task.WaitAsync(cancellationToken);
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                break;
            } catch (Exception exception) {
                if (!cancellationToken.IsCancellationRequested) {
                    BluetoothLog.Error($"Connection to {device.Name} failed: {exception.Message}");
                }
            } finally {
                lock (_workflowSignalLock) {
                    if (ReferenceEquals(_monitoringDevice, device)) _monitoringDevice = null;
                    if (ReferenceEquals(_connectionLost, connectionLost)) _connectionLost = null;
                }

                await DisposeOutputsAsync();
                try {
                    await device.StopHeartrateMonitorAsync();
                } catch (Exception exception) {
                    if (!cancellationToken.IsCancellationRequested) {
                        BluetoothLog.Error($"Unable to stop heart rate monitoring: {exception.Message}");
                    }
                }

                if (!cancellationToken.IsCancellationRequested && device.Status != DeviceStatus.Offline) {
                    try {
                        await device.DisconnectAsync();
                    } catch (Exception exception) {
                        BluetoothLog.Error($"Unable to clean up {device.Name}: {exception.Message}");
                    }
                }
            }

            if (cancellationToken.IsCancellationRequested || !_retryEnabled || !ReferenceEquals(Device, device)) break;
            try {
                await Task.Delay(_retryDelay, cancellationToken);
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                break;
            }
        }
    }


    // ------------------------------------------------------------------
    // Device state handling
    // ------------------------------------------------------------------

    private void UpdateStatusText() {
        if (IsAutoConnecting) {
            StatusText = "Searching for device...";
        }
        else if (Device != null) {
            StatusText = Device.Status switch {
                DeviceStatus.Offline => "Disconnected",
                DeviceStatus.Connecting => $"Connecting to {Device.Name}...",
                DeviceStatus.Authenticating or DeviceStatus.OnlineUnauth => $"Authenticating with {Device.Name}...",
                DeviceStatus.OnlineAuth => $"Connected to {Device.Name}",
                _ => StatusText
            };
        }
        else {
            StatusText = "Disconnected";
        }
    }

    private void AddHeartratePoint(int bpm) {
        var now = DateTime.UtcNow;
        HeartrateHistory.Add(new HeartratePoint(now, bpm));
        PruneHeartrateHistory(now);
    }

    private void PruneHeartrateHistory(DateTime now) {
        var rangeSec = ConfigurationManager.Instance.CurrentConfig.Monitoring.GraphRangeSeconds;
        if (rangeSec <= 0) return;

        var cutoff = now.AddSeconds(-rangeSec);
        while (HeartrateHistory.Count > 2 && HeartrateHistory[0].Timestamp < cutoff && HeartrateHistory[1].Timestamp <= cutoff) {
            HeartrateHistory.RemoveAt(0);
        }
    }

    private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (sender is not Device source || !ReferenceEquals(Device, source) || _isShuttingDown) return;

        if (e.PropertyName == nameof(Device.Heartrate)) {
            var bpm = source.Heartrate;
            Dispatcher.UIThread.Post(() => {
                if (_isShuttingDown || !ReferenceEquals(Device, source)) return;
                if (bpm > 0) {
                    AddHeartratePoint(bpm);
                }
                OnPropertyChanged(nameof(HasHeartrate));
                OnPropertyChanged(nameof(HeartrateStatusText));
            });
        }
        else if (e.PropertyName == nameof(Device.Status)) {
            if (source.Status == DeviceStatus.Offline) {
                lock (_workflowSignalLock) {
                    if (ReferenceEquals(_monitoringDevice, source)) _connectionLost?.TrySetResult(true);
                }
            }

            Dispatcher.UIThread.Post(() => {
                if (!_isShuttingDown && ReferenceEquals(Device, source)) DeviceUpdate();
            });
        }
        else if (e.PropertyName == nameof(Device.HeartrateMonitorStarted)) {
            Dispatcher.UIThread.Post(() => {
                if (!_isShuttingDown && ReferenceEquals(Device, source)) RaiseCommandStates();
            });
        }
    }

    private void DeviceUpdate() {
        if (Device == null || Device.Status == DeviceStatus.Offline) {
            HeartrateHistory.Clear();
        }

        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ShowConnectButtons));
        OnPropertyChanged(nameof(HasHeartrate));
        OnPropertyChanged(nameof(HeartrateStatusText));

        UpdateStatusText();
        RaiseCommandStates();
    }

    private void RaiseCommandStates() {
        CommandConnect.RaiseCanExecuteChanged();
        CommandAutoConnect.RaiseCanExecuteChanged();
        CommandDisconnect.RaiseCanExecuteChanged();
        CommandStart.RaiseCanExecuteChanged();
        CommandStop.RaiseCanExecuteChanged();
        CommandToggleMonitoring.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(MonitoringButtonToolTip));
        OnPropertyChanged(nameof(MonitoringButtonIcon));
    }
}
