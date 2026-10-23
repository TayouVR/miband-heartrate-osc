using System;
using System.Collections.Generic;
using System.Linq;
using MiBandHR.Avalonia.Views;
using MiBandHR.Core.Bluetooth;
using MiBandHR.Core.Configuration;

namespace MiBandHR.Avalonia.ViewModels;

public class SettingsViewModel : ViewModelBase {
    private readonly Action? _onSettingsChanged;

    public SettingsViewModel() : this(null) { }

    public SettingsViewModel(Action? onSettingsChanged) {
        _onSettingsChanged = onSettingsChanged;
        CommandClose = new RelayCommand(_ => View?.Close());
    }

    /// <summary>The window this view model belongs to (set by the view).</summary>
    public SettingsWindow? View { get; set; }

    public RelayCommand CommandClose { get; }

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
}
