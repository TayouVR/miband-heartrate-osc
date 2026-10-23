using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
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

    private BLE? _autoConnectBluetooth;
    private bool _guard;
    private LogWindow? _logWindow;

    private DeviceHeartrateFileOutput? _fileOutput;
    private DeviceHeartrateCsvOutput? _csvOutput;
    private DeviceHeartrateOscOutput? _oscOutput;

    public MainViewModel() {
        CommandConnect = new RelayCommand(_ => OpenConnectionWindow(),
            _ => Device == null || Device.Status == DeviceStatus.Offline);
        CommandAutoConnect = new RelayCommand(_ => StartAutoConnect(),
            _ => Device == null || Device.Status == DeviceStatus.Offline);
        CommandDisconnect = new RelayCommand(_ => Disconnect(),
            _ => Device != null && Device.Status != DeviceStatus.Offline);
        CommandLog = new RelayCommand(_ => OpenLogWindow());
        CommandClearLog = new RelayCommand(_ => LogEntries.Clear());
        CommandSettings = new RelayCommand(_ => OpenSettingsWindow());
        CommandStart = new RelayCommand(_ => StartMonitoring(),
            _ => Device is { Status: DeviceStatus.OnlineAuth, HeartrateMonitorStarted: false });
        CommandStop = new RelayCommand(_ => StopMonitoring(),
            _ => Device is { HeartrateMonitorStarted: true });

        // Mirror the Bluetooth layer's log output into the on-screen log panel.
        BluetoothLog.EntryLogged += OnBluetoothLogEntry;
    }

    /// <summary>The window this view model belongs to (set by the view).</summary>
    public MainWindow? View { get; set; }

    /// <summary>Live Bluetooth activity log shown in the side panel.</summary>
    public ObservableCollection<LogEntryViewModel> LogEntries { get; } = new();

    /// <summary>Raised whenever a new log entry is added (used to auto-scroll).</summary>
    public event Action? LogEntryAdded;

    // Cap the log so a long retry loop does not grow memory unbounded.
    private const int MaxLogEntries = 500;

    private void OnBluetoothLogEntry(BluetoothLogEntry entry) {
        // Log events may arrive on background threads; marshal to the UI thread.
        Dispatcher.UIThread.Post(() => {
            LogEntries.Add(new LogEntryViewModel(entry));
            while (LogEntries.Count > MaxLogEntries) {
                LogEntries.RemoveAt(0);
            }

            LogEntryAdded?.Invoke();
        });
    }

    public Device? Device {
        get;
        set {
            if (field != null) {
                field.PropertyChanged -= OnDevicePropertyChanged;
                field.Dispose();
            }

            field = value;

            if (field != null) {
                field.PropertyChanged += OnDevicePropertyChanged;
            }

            DeviceUpdate();
            OnPropertyChanged(nameof(Device));
        }
    }

    public bool IsConnected {
        get;
        set {
            field = value;
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
    } = "No device connected";

    /// <summary>True while no device is connected (the manual/auto connect buttons are visible).</summary>
    public bool ShowConnectButtons => !IsConnected;

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

    public RelayCommand CommandConnect { get; }
    public RelayCommand CommandAutoConnect { get; }
    public RelayCommand CommandDisconnect { get; }
    public RelayCommand CommandLog { get; }
    public RelayCommand CommandClearLog { get; }
    public RelayCommand CommandSettings { get; }
    public RelayCommand CommandStart { get; }
    public RelayCommand CommandStop { get; }

    public void RefreshSettings() {
        OnPropertyChanged(nameof(UseAutoConnect));
        OnPropertyChanged(nameof(TryConnectOnStartup));
        OnPropertyChanged(nameof(SelectedBluetoothBackend));
        OnPropertyChanged(nameof(ContinuousMode));
        OnPropertyChanged(nameof(EnableFileOutput));
        OnPropertyChanged(nameof(EnableCsvOutput));
        OnPropertyChanged(nameof(EnableOscOutput));
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

    private async void OpenConnectionWindow() {
        if (View == null) return;

        var dialog = new ConnectionWindow();
        var device = await dialog.ShowDialog<Device?>(View);

        if (device != null) {
            device.Connect();
            Device = device;
        }
    }

    private void StartAutoConnect() {
        _autoConnectBluetooth = new BLE();

        var defaultDeviceVersion = ConfigurationManager.Instance.CurrentConfig.Device.AutoConnectDeviceVersion;
        var defaultDeviceName = ConfigurationManager.Instance.CurrentConfig.Device.AutoConnectDeviceName;
        var defaultDeviceAuthKey = ConfigurationManager.Instance.CurrentConfig.Device.AutoConnectDeviceAuthKey;

        void OnBluetoothAdded(object? sender, IBluetoothDevice args) {
            if (args.Name != defaultDeviceName) return;

            Device device;
            switch (defaultDeviceVersion) {
                case "2":
                case "3":
                    device = new MiBand2_3_Device(args);
                    break;
                case "4":
                case "5":
                    device = new MiBand4_Device(args, defaultDeviceAuthKey);
                    break;
                default:
                    return;
            }

            if (_autoConnectBluetooth != null) {
                _autoConnectBluetooth.DeviceFound -= OnBluetoothAdded;
            }

            device.Connect();

            // Property changes must happen on the UI thread.
            Dispatcher.UIThread.Post(() => Device = device);
        }

        _autoConnectBluetooth.DeviceFound += OnBluetoothAdded;
        _ = _autoConnectBluetooth.ScanForDevicesAsync();
    }

    private void Disconnect() {
        if (Device != null) {
            _guard = false;
            Device.Disconnect();
            Device = null;
        }
    }

    private void StartMonitoring() {
        if (Device == null) return;

        Device.StartHeartrateMonitor(ContinuousMode);

        if (EnableFileOutput) {
            _fileOutput = new DeviceHeartrateFileOutput("heartrate.txt", Device);
        }

        if (EnableCsvOutput) {
            _csvOutput = new DeviceHeartrateCsvOutput("heartrate.csv", Device);
        }

        if (EnableOscOutput) {
            _oscOutput = new DeviceHeartrateOscOutput(Device);
        }
    }

    private void StopMonitoring() {
        Device?.StopHeartrateMonitor();

        _fileOutput = null;
        _csvOutput = null;
        _oscOutput = null;
    }

    // ------------------------------------------------------------------
    // Device state handling
    // ------------------------------------------------------------------

    private void UpdateStatusText() {
        if (Device != null) {
            StatusText = Device.Status switch {
                DeviceStatus.Offline => "No device connected",
                DeviceStatus.OnlineUnauth => $"Connected to {Device.Name} | Not auth",
                DeviceStatus.OnlineAuth => $"Connected to {Device.Name} | Auth",
                _ => StatusText
            };
        }
        else {
            StatusText = "No device connected";
        }
    }

    private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(Device.Status)) {
            // Device callbacks arrive on background threads; marshal to the UI thread.
            Dispatcher.UIThread.Post(DeviceUpdate);

            if (Device != null) {
                // Connection lost, we try to re-connect
                if (Device.Status == DeviceStatus.Offline && _guard) {
                    _guard = false;
                    Device.Connect();
                }
                else if (Device.Status != DeviceStatus.Offline) {
                    _guard = true;
                }

                // Start monitoring as soon as the handshake completes. Mi Band
                // firmware idles out and disconnects a few seconds after auth if
                // the host doesn't subscribe to heart rate notifications promptly.
                if (Device.Status == DeviceStatus.OnlineAuth && !Device.HeartrateMonitorStarted) {
                    Dispatcher.UIThread.Post(StartMonitoring);
                }
            }
        }
        else if (e.PropertyName == nameof(Device.HeartrateMonitorStarted)) {
            Dispatcher.UIThread.Post(RaiseCommandStates);
        }
    }

    private void DeviceUpdate() {
        // Must be reset even when Device is null, otherwise the connect buttons
        // stay hidden (ShowConnectButtons == !IsConnected) and the UI is stuck
        // after a disconnect.
        IsConnected = Device != null && Device.Status != DeviceStatus.Offline;

        UpdateStatusText();
        RaiseCommandStates();
    }

    private void RaiseCommandStates() {
        CommandConnect.RaiseCanExecuteChanged();
        CommandAutoConnect.RaiseCanExecuteChanged();
        CommandDisconnect.RaiseCanExecuteChanged();
        CommandStart.RaiseCanExecuteChanged();
        CommandStop.RaiseCanExecuteChanged();
    }
}
