using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using MiBandHR.Avalonia.Views;
using MiBandHR.Core;
using MiBandHR.Core.Bluetooth;
using MiBandHR.Core.Devices;

namespace MiBandHR.Avalonia.ViewModels;

/// <summary>
/// Wraps a <see cref="DeviceModel"/> value with the display text from its
/// <see cref="DescriptionAttribute"/> for the model picker.
/// </summary>
public record DeviceModelOption(DeviceModel Value, string Description)
{
    public override string ToString() => Description;
}

public class ConnectionViewModel : ViewModelBase
{
    private readonly BLE _bluetooth;
    private IBluetoothDevice? _selectedDevice;
    private DeviceModelOption _selectedModel;
    private CancellationTokenSource? _scanCts;
    private bool _isScanning;
    private string _scanStatus = "Scanning...";

    public ConnectionViewModel()
    {
        _selectedModel = DeviceModels[0];

        _bluetooth = new BLE();
        _bluetooth.DeviceFound += OnBluetoothAdded;

        CommandConnect = new RelayCommand(_ => Connect(), _ => SelectedDevice != null || SelectedModel.Value == DeviceModel.DUMMY);
        CommandCancel = new RelayCommand(_ => View?.Close(null));
        CommandRefresh = new RelayCommand(_ => StartScan());

        StartScan();
    }

    /// <summary>The window this view model belongs to (set by the view).</summary>
    public ConnectionWindow? View { get; set; }

    public ObservableCollection<IBluetoothDevice> Devices { get; } = new();

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            _isScanning = value;
            OnPropertyChanged(nameof(IsScanning));
        }
    }

    /// <summary>Human-readable scan progress / result / error text.</summary>
    public string ScanStatus
    {
        get => _scanStatus;
        private set
        {
            _scanStatus = value;
            OnPropertyChanged(nameof(ScanStatus));
        }
    }

    /// <summary>
    /// Device models selectable in the UI (DUMMY is hidden, like in the WPF app).
    /// </summary>
    public static DeviceModelOption[] DeviceModels { get; } =
    {
        new(DeviceModel.MIBAND_2_3, GetDescription(DeviceModel.MIBAND_2_3)),
        new(DeviceModel.MIBAND_4, GetDescription(DeviceModel.MIBAND_4))
    };

    public IBluetoothDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            _selectedDevice = value;
            OnPropertyChanged(nameof(SelectedDevice));
            CommandConnect.RaiseCanExecuteChanged();
        }
    }

    public DeviceModelOption SelectedModel
    {
        get => _selectedModel;
        set
        {
            _selectedModel = value;
            OnPropertyChanged(nameof(SelectedModel));
            CommandConnect.RaiseCanExecuteChanged();
        }
    }

    private static string GetDescription(DeviceModel model)
    {
        var field = typeof(DeviceModel).GetField(model.ToString());
        var attribute = field?
            .GetCustomAttributes(typeof(DescriptionAttribute), false)
            .FirstOrDefault() as DescriptionAttribute;
        return attribute?.Description ?? model.ToString();
    }

    public RelayCommand CommandConnect { get; }
    public RelayCommand CommandCancel { get; }
    public RelayCommand CommandRefresh { get; }

    private void StartScan()
    {
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        ScanStatus = "Scanning...";

        _ = ScanAsync(_scanCts.Token);
    }

    private async global::System.Threading.Tasks.Task ScanAsync(CancellationToken token)
    {
        try
        {
            await _bluetooth.ScanForDevicesAsync(token);
            if (!token.IsCancellationRequested)
            {
                ScanStatus = Devices.Count == 0
                    ? "No devices found."
                    : $"{Devices.Count} device(s) found.";
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the user; nothing to report.
        }
        catch (Exception e)
        {
            // Surface the failure to the user instead of silently showing an
            // empty list (e.g. no adapter, D-Bus/BlueZ unavailable).
            BluetoothLog.Error($"Scan failed: {e.Message}");
            if (!token.IsCancellationRequested)
            {
                ScanStatus = $"Scan failed: {e.Message}";
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsScanning = false;
            }
        }
    }

    private void OnBluetoothAdded(object? sender, IBluetoothDevice args)
    {
        // ObservableCollection must be touched from the UI thread.
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Devices.All(d => d.Id != args.Id))
            {
                Devices.Add(args);
            }
        });
    }

    private async void Connect()
    {
        _scanCts?.Cancel();
        _bluetooth.DeviceFound -= OnBluetoothAdded;

        Device? device = null;

        switch (SelectedModel.Value)
        {
            case DeviceModel.DUMMY:
                device = new Dummy_Device();
                break;
            case DeviceModel.MIBAND_2_3:
                if (SelectedDevice == null) return;
                device = new MiBand2_3_Device(SelectedDevice);
                break;
            case DeviceModel.MIBAND_4:
            {
                if (SelectedDevice == null || View == null) return;

                var authWindow = new AuthenticationKeyWindow();
                var key = await authWindow.ShowDialog<string?>(View);

                if (string.IsNullOrEmpty(key)) return;

                device = new MiBand4_Device(SelectedDevice, key);
                break;
            }
        }

        // Return the created device to the caller (MainWindow) via the dialog result.
        View?.Close(device);
    }
}
