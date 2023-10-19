using System;
using System.ComponentModel;
using System.Configuration;
using System.Windows.Input;
using Windows.Devices.Enumeration;
using MiBand_Heartrate.Devices;
using MiBand_Heartrate.Extras;
using MiBand_Heartrate.Views;

namespace MiBand_Heartrate.ViewModels
{
    public class MainWindowViewModel : ViewModel {
        public MainWindow View { get; set; }

        private Device? _device;

        public Device? Device
        {
            get => _device;
            set {
                if (_device != null) {
                    _device.PropertyChanged -= OnDevicePropertyChanged;
                    _device.Dispose();
                }

                _device = value;

                if (_device != null) {
                    _device.PropertyChanged += OnDevicePropertyChanged;
                }

                DeviceUpdate();

                InvokePropertyChanged("Device");
            }
        }

        private bool _isConnected;

        public bool UseAutoConnect => UseAutoConnectSetting.ToLower() == "true";

        public bool IsConnected {
            get => _isConnected;
            set {
                _isConnected = value;
                InvokePropertyChanged("IsConnected");
            }
        }

        private string _statusText = "No device connected";

        public string StatusText {
            get => _statusText;
            set {
                _statusText = value;
                InvokePropertyChanged("StatusText");
            }
        }

        private string UseAutoConnectSetting => ConfigurationManager.AppSettings["useAutoConnect"] ?? string.Empty;
        private string DefaultDeviceVersionSetting => ConfigurationManager.AppSettings["autoConnectDeviceVersion"] ?? string.Empty;
        
        private string DefaultDeviceName => ConfigurationManager.AppSettings["autoConnectDeviceName"] ?? string.Empty;
        
        private string DefaultDeviceAuthKey => ConfigurationManager.AppSettings["autoConnectDeviceAuthKey"] ?? string.Empty;

        private bool _continuousMode = true;

        public bool ContinuousMode {
            get => _continuousMode;
            set {
                _continuousMode = value;

                Setting.Set("ContinuousMode", _continuousMode);

                InvokePropertyChanged("ContinuousMode");
            }
        }

        private bool _enableFileOutput;

        public bool EnableFileOutput {
            get => _enableFileOutput;
            set {
                _enableFileOutput = value;

                Setting.Set("FileOutput", _enableFileOutput);

                InvokePropertyChanged("EnableFileOutput");
            }
        }

        private bool _enableCSVOutput;

        public bool EnableCSVOutput {
            get => _enableCSVOutput;
            set {
                _enableCSVOutput = value;

                Setting.Set("CSVOutput", _enableCSVOutput);

                InvokePropertyChanged("EnableCSVOutput");
            }
        }

        private bool _enableOscOutput = true;

        public bool EnableOscOutput {
            get => _enableOscOutput;
            set {
                _enableOscOutput = value;

                Setting.Set("OscOutput", _enableOscOutput);

                InvokePropertyChanged("EnableOscOutput");
            }
        }

        private bool _guard;

        // --------------------------------------

        public MainWindowViewModel() {
            ContinuousMode = Setting.Get("ContinuousMode", true);
            EnableFileOutput = Setting.Get("FileOutput", false);
            EnableCSVOutput = Setting.Get("CSVOutput", false);
            EnableOscOutput = Setting.Get("OscOutput", true);
            
            
        }

        ~MainWindowViewModel() {
            Device = null;
        }

        private void UpdateStatusText() {
            if (Device != null) {
                StatusText = Device.Status switch {
                    DeviceStatus.OFFLINE => "No device connected",
                    DeviceStatus.ONLINE_UNAUTH => $"Connected to {Device.Name} | Not auth",
                    DeviceStatus.ONLINE_AUTH => $"Connected to {Device.Name} | Auth",
                    _ => StatusText
                };
            } else {
                StatusText = "No device connected";
            }
        }

        private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e) {
            switch (e.PropertyName) {
                case "Status": {
                    System.Windows.Application.Current.Dispatcher.Invoke((Action)DeviceUpdate);

                    if (Device != null) {
                        // Connection lost, we try to re-connect
                        if (Device.Status == DeviceStatus.OFFLINE && _guard) {
                            _guard = false;
                            Device.Connect();
                        } else if (Device.Status != DeviceStatus.OFFLINE) {
                            _guard = true;
                        }
                    }

                    break;
                }
                case "HeartrateMonitorStarted":
                    CommandManager.InvalidateRequerySuggested();
                    break;
            }
        }

        private void DeviceUpdate() {
            if (Device != null) {
                IsConnected = Device.Status != DeviceStatus.OFFLINE;
            }

            UpdateStatusText();
            CommandManager.InvalidateRequerySuggested();
        }

        // --------------------------------------
        
        // ref : https://github.com/hai-vr/miband-heartrate-osc/commit/754aac408b03a145560a619a26e851ff60cf0293

        private ICommand? _commandAutoConnect;

        public ICommand CommandAutoConnect {
            get {
                return _commandAutoConnect ??= new RelayCommand<object>("connect",
                    "Connect to a device", o => {
                        var bluetooth = new BLE(new[]
                            {"System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected"});

                        var defaultDeviceVersion = DefaultDeviceVersionSetting;
                        var defaultDeviceName = DefaultDeviceName;
                        var defaultDeviceAuthKey = DefaultDeviceAuthKey;

                        void OnBluetoothAdded(DeviceWatcher sender, DeviceInformation args) {
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
                                    throw new ArgumentOutOfRangeException();
                            }

                            device.Connect();

                            Device = device;

                            bluetooth.Watcher.Added -= OnBluetoothAdded;

                            bluetooth.StopWatcher();
                        }

                        bluetooth.Watcher.Added += OnBluetoothAdded;

                        bluetooth.StartWatcher();
                    }, o => Device == null || Device.Status == DeviceStatus.OFFLINE);
            }
        }

        private ICommand? _commandConnect;

        public void OnConnectClicked() {
            var dialog = new ConnectionWindow(this);
            dialog.ShowDialog(View);
        }

        private bool CanConnect(object msg) {
            return Device == null || Device.Status == DeviceStatus.OFFLINE;
        }
        
        public ICommand CommandConnect {
            get {
                if (_commandConnect == null) {
                    _commandConnect = new RelayCommand<object>("connect", "Connect to a device", o =>
                    {
                        var dialog = new ConnectionWindow(this);
                        dialog.ShowDialog(View ?? MainWindow.Current);
                    }, o => Device == null || Device.Status == DeviceStatus.OFFLINE);
                }

                return _commandConnect;
            }
        }

        private ICommand? _commandDisconnect;

        public ICommand CommandDisconnect {
            get {
                return _commandDisconnect ??= new RelayCommand<object>("disconnect",
                    "Disconnect form connect device", o => {
                        if (Device != null) {
                            _guard = false;
                            Device.Disconnect();
                            Device = null;
                        }

                        Device = null;
                    }, o => Device != null && Device.Status != DeviceStatus.OFFLINE);
            }
        }

        private ICommand? _commandStart;

        public ICommand CommandStart {
            get {
                return _commandStart ??= new RelayCommand<object>("device.start",
                    "Start heartrate monitoring", o => {
                        Device?.StartHeartrateMonitor(ContinuousMode);

                        if (_enableFileOutput) {
                            new DeviceHeartrateFileOutput("heartrate.txt", Device);
                        }

                        if (_enableCSVOutput) {
                            new DeviceHeartrateCSVOutput("heartrate.csv", Device);
                        }

                        if (_enableOscOutput) {
                            new DeviceHeartrateOscOutput(Device);
                        }
                    },
                    o => Device != null && Device.Status == DeviceStatus.ONLINE_AUTH &&
                         !Device.HeartrateMonitorStarted);
            }
        }

        private ICommand? _commandStop;

        public ICommand CommandStop {
            get {
                return _commandStop ??= new RelayCommand<object>("device.stop",
                    "Stop heartrate monitoring", o => {
                        Device?.StopHeartrateMonitor();
                    }, o => Device != null && Device.HeartrateMonitorStarted);
            }
        }

        public static int MaxHeartrate {
            get => App.Current.MaxHeartrate;
            set => App.Current.MaxHeartrate = value;
        }

        public static int MinHeartrate {
            get => App.Current.MinHeartrate;
            set => App.Current.MaxHeartrate = value;
        }
    }
}
