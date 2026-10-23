using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MiBandHR.Avalonia.Views;
using MiBandHR.Core.Bluetooth;
using MiBandHR.Core.Configuration;

namespace MiBandHR.Avalonia.ViewModels;

public class AuthKeyEntryViewModel : ViewModelBase {
    private readonly Action? _onChanged;
    private string _deviceId;
    private string _authKey;

    public AuthKeyEntryViewModel(string deviceId, string authKey, Action<AuthKeyEntryViewModel>? onRemove = null, Action? onChanged = null) {
        _deviceId = deviceId;
        _authKey = authKey;
        _onChanged = onChanged;
        CommandRemove = new RelayCommand(_ => onRemove?.Invoke(this));
    }

    public string DeviceId {
        get => _deviceId;
        set {
            if (_deviceId != value) {
                _deviceId = value;
                OnPropertyChanged(nameof(DeviceId));
                _onChanged?.Invoke();
            }
        }
    }

    public string AuthKey {
        get => _authKey;
        set {
            if (_authKey != value) {
                _authKey = value;
                OnPropertyChanged(nameof(AuthKey));
                _onChanged?.Invoke();
            }
        }
    }

    public RelayCommand CommandRemove { get; }
}

public class SettingsViewModel : ViewModelBase {
    private readonly Action? _onSettingsChanged;
    private string _newDeviceId = "";
    private string _newAuthKey = "";
    private string _addErrorText = "";

    public SettingsViewModel() : this(null) { }

    public SettingsViewModel(Action? onSettingsChanged) {
        _onSettingsChanged = onSettingsChanged;
        CommandClose = new RelayCommand(_ => View?.Close());
        CommandAddDevice = new RelayCommand(_ => AddDevice(), _ => !string.IsNullOrWhiteSpace(NewDeviceId) && !string.IsNullOrWhiteSpace(NewAuthKey));
        CommandRemoveDevice = new RelayCommand(param => {
            if (param is AuthKeyEntryViewModel entry) {
                RemoveDevice(entry);
            }
        });

        LoadAuthKeys();
    }

    /// <summary>The window this view model belongs to (set by the view).</summary>
    public SettingsWindow? View { get; set; }

    public RelayCommand CommandClose { get; }
    public RelayCommand CommandAddDevice { get; }
    public RelayCommand CommandRemoveDevice { get; }

    public ObservableCollection<AuthKeyEntryViewModel> AuthKeys { get; } = new();

    public string NewDeviceId {
        get => _newDeviceId;
        set {
            _newDeviceId = value;
            OnPropertyChanged(nameof(NewDeviceId));
            CommandAddDevice.RaiseCanExecuteChanged();
        }
    }

    public string NewAuthKey {
        get => _newAuthKey;
        set {
            _newAuthKey = value;
            OnPropertyChanged(nameof(NewAuthKey));
            CommandAddDevice.RaiseCanExecuteChanged();
        }
    }

    public string AddErrorText {
        get => _addErrorText;
        set {
            _addErrorText = value;
            OnPropertyChanged(nameof(AddErrorText));
            OnPropertyChanged(nameof(HasAddError));
        }
    }

    public bool HasAddError => !string.IsNullOrEmpty(AddErrorText);

    private void LoadAuthKeys() {
        AuthKeys.Clear();
        foreach (var (device, key) in ConfigurationManager.Instance.CurrentConfig.Device.AuthKeys) {
            AuthKeys.Add(new AuthKeyEntryViewModel(device, key, RemoveDevice, SaveAuthKeys));
        }
    }

    private void SaveAuthKeys() {
        var dict = new Dictionary<string, string>();
        foreach (var item in AuthKeys) {
            if (!string.IsNullOrWhiteSpace(item.DeviceId)) {
                dict[item.DeviceId.Trim()] = item.AuthKey.Trim();
            }
        }
        ConfigurationManager.Instance.CurrentConfig.Device.AuthKeys = dict;
        ConfigurationManager.Instance.SaveConfiguration();
        _onSettingsChanged?.Invoke();
    }

    private void AddDevice() {
        AddErrorText = "";
        var deviceId = NewDeviceId.Trim();
        var authKey = NewAuthKey.Trim();

        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(authKey)) {
            AddErrorText = "Both device address/ID and authentication key are required.";
            return;
        }

        var existing = AuthKeys.FirstOrDefault(k => string.Equals(k.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
        if (existing != null) {
            existing.AuthKey = authKey;
        }
        else {
            AuthKeys.Add(new AuthKeyEntryViewModel(deviceId, authKey, RemoveDevice, SaveAuthKeys));
        }

        SaveAuthKeys();
        NewDeviceId = "";
        NewAuthKey = "";
    }

    private void RemoveDevice(AuthKeyEntryViewModel entry) {
        AuthKeys.Remove(entry);
        SaveAuthKeys();
    }

    public string MiBand4AuthKey {
        get => ConfigurationManager.Instance.CurrentConfig.Device.AutoConnectDeviceAuthKey;
        set {
            ConfigurationManager.Instance.CurrentConfig.Device.AutoConnectDeviceAuthKey = value;
            ConfigurationManager.Instance.SaveConfiguration();
            OnPropertyChanged(nameof(MiBand4AuthKey));
            _onSettingsChanged?.Invoke();
        }
    }

    public bool TryConnectOnStartup {
        get => ConfigurationManager.Instance.CurrentConfig.Device.TryConnectOnStartup;
        set {
            ConfigurationManager.Instance.CurrentConfig.Device.TryConnectOnStartup = value;
            ConfigurationManager.Instance.SaveConfiguration();
            OnPropertyChanged(nameof(TryConnectOnStartup));
            _onSettingsChanged?.Invoke();
        }
    }

    public bool UseAutoConnect {
        get => ConfigurationManager.Instance.CurrentConfig.Device.UseAutoConnect;
        set {
            ConfigurationManager.Instance.CurrentConfig.Device.UseAutoConnect = value;
            ConfigurationManager.Instance.SaveConfiguration();
            OnPropertyChanged(nameof(UseAutoConnect));
            _onSettingsChanged?.Invoke();
        }
    }

    /// <summary>Backends selectable on the current platform.</summary>
    public IReadOnlyList<MainViewModel.BluetoothBackendOption> AvailableBackends { get; } =
        BluetoothScannerFactory.AvailableBackends()
            .Select(b => new MainViewModel.BluetoothBackendOption(b, DisplayName(b)))
            .ToList();

    public MainViewModel.BluetoothBackendOption? SelectedBluetoothBackend {
        get => AvailableBackends.FirstOrDefault(o =>
                   o.Value == ConfigurationManager.Instance.CurrentConfig.Device.BluetoothBackend)
               ?? AvailableBackends.First();
        set {
            if (value == null) return;
            ConfigurationManager.Instance.CurrentConfig.Device.BluetoothBackend = value.Value;
            ConfigurationManager.Instance.SaveConfiguration();
            OnPropertyChanged(nameof(SelectedBluetoothBackend));
            _onSettingsChanged?.Invoke();
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
            _onSettingsChanged?.Invoke();
        }
    }

    public int GraphRangeSeconds {
        get => ConfigurationManager.Instance.CurrentConfig.Monitoring.GraphRangeSeconds;
        set {
            var sanitized = Math.Max(0, value);
            ConfigurationManager.Instance.CurrentConfig.Monitoring.GraphRangeSeconds = sanitized;
            ConfigurationManager.Instance.SaveConfiguration();
            OnPropertyChanged(nameof(GraphRangeSeconds));
            _onSettingsChanged?.Invoke();
        }
    }

    public bool EnableFileOutput {
        get => ConfigurationManager.Instance.CurrentConfig.Output.FileEnabled;
        set {
            ConfigurationManager.Instance.CurrentConfig.Output.FileEnabled = value;
            ConfigurationManager.Instance.SaveConfiguration();
            OnPropertyChanged(nameof(EnableFileOutput));
            _onSettingsChanged?.Invoke();
        }
    }

    public bool EnableCsvOutput {
        get => ConfigurationManager.Instance.CurrentConfig.Output.CsvEnabled;
        set {
            ConfigurationManager.Instance.CurrentConfig.Output.CsvEnabled = value;
            ConfigurationManager.Instance.SaveConfiguration();
            OnPropertyChanged(nameof(EnableCsvOutput));
            _onSettingsChanged?.Invoke();
        }
    }

    public bool EnableOscOutput {
        get => ConfigurationManager.Instance.CurrentConfig.Output.OscEnabled;
        set {
            ConfigurationManager.Instance.CurrentConfig.Output.OscEnabled = value;
            ConfigurationManager.Instance.SaveConfiguration();
            OnPropertyChanged(nameof(EnableOscOutput));
            _onSettingsChanged?.Invoke();
        }
    }

    public string OscAddress {
        get => ConfigurationManager.Instance.CurrentConfig.Osc.Host;
        set {
            var sanitized = value ?? "";
            ConfigurationManager.Instance.CurrentConfig.Osc.Host = sanitized;
            ConfigurationManager.Instance.SaveConfiguration();
            OnPropertyChanged(nameof(OscAddress));
            _onSettingsChanged?.Invoke();
        }
    }

    public int OscPort {
        get => ConfigurationManager.Instance.CurrentConfig.Osc.Port;
        set {
            var sanitized = Math.Max(0, value);
            ConfigurationManager.Instance.CurrentConfig.Osc.Port = sanitized;
            ConfigurationManager.Instance.SaveConfiguration();
            OnPropertyChanged(nameof(OscPort));
            _onSettingsChanged?.Invoke();
        }
    }
}
