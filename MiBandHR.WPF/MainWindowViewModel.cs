using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using MiBand_Heartrate.Extras;
using MiBandHR.Core;
using MiBandHR.Core.Configuration;
using MiBandHR.Core.Devices;
using InTheHand.Bluetooth;

namespace MiBand_Heartrate
{
    public class MainWindowViewModel : ViewModel
    {
        Device _device;

        BLE _autoConnectBluetooth;

        public Device Device {
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

        bool _isConnected;

        public bool IsConnected {
            get => _isConnected;
            set {
                _isConnected = value;
                InvokePropertyChanged("IsConnected");
            }
        }

        string _statusText = "No device connected";

        public string StatusText {
            get => _statusText;
            set {
                _statusText = value;
                InvokePropertyChanged("StatusText");
            }
        }
        
        public bool UseAutoConnect {
            get => ConfigurationManager.Instance.CurrentConfig.Device.UseAutoConnect;
            set {
                ConfigurationManager.Instance.CurrentConfig.Device.UseAutoConnect = value;
                ConfigurationManager.Instance.SaveConfiguration();

                InvokePropertyChanged("UseAutoConnect");
            }
        }
        
        private string DefaultDeviceVersionSetting => ConfigurationManager.Instance.CurrentConfig.Device.AutoConnectDeviceVersion;
        
        private string DefaultDeviceName => ConfigurationManager.Instance.CurrentConfig.Device.AutoConnectDeviceName;
        
        private string DefaultDeviceAuthKey => ConfigurationManager.Instance.CurrentConfig.Device.AutoConnectDeviceAuthKey;


        public bool ContinuousMode {
            get => ConfigurationManager.Instance.CurrentConfig.Monitoring.ContinuousMode;
            set {
                ConfigurationManager.Instance.CurrentConfig.Monitoring.ContinuousMode = value;
                ConfigurationManager.Instance.SaveConfiguration();

                InvokePropertyChanged("ContinuousMode");
            }
        }

        public bool EnableFileOutput {
            get => ConfigurationManager.Instance.CurrentConfig.Output.FileEnabled;
            set {
                ConfigurationManager.Instance.CurrentConfig.Output.FileEnabled = value;
                ConfigurationManager.Instance.SaveConfiguration();

                InvokePropertyChanged("EnableFileOutput");
            }
        }

        public bool EnableCSVOutput {
            get => ConfigurationManager.Instance.CurrentConfig.Output.CsvEnabled;
            set {
                ConfigurationManager.Instance.CurrentConfig.Output.CsvEnabled = value;
                ConfigurationManager.Instance.SaveConfiguration();

                InvokePropertyChanged("EnableCSVOutput");
            }
        }

        public bool EnableOscOutput {
            get => ConfigurationManager.Instance.CurrentConfig.Output.OscEnabled;
            set {
                ConfigurationManager.Instance.CurrentConfig.Output.OscEnabled = value;
                ConfigurationManager.Instance.SaveConfiguration();

                InvokePropertyChanged("EnableOscOutput");
            }
        }

        bool _guard;

        DeviceHeartrateFileOutput _fileOutput;

        DeviceHeartrateCSVOutput _csvOutput;

        DeviceHeartrateOscOutput _oscOutput;

        // --------------------------------------

        ~MainWindowViewModel()
        {
            Device = null;
        }

        void UpdateStatusText() {
            if (Device != null) {
                switch (Device.Status) {
                    case DeviceStatus.OFFLINE:
                        StatusText = "No device connected";
                        break;
                    case DeviceStatus.ONLINE_UNAUTH:
                        StatusText = string.Format("Connected to {0} | Not auth", Device.Name);
                        break;
                    case DeviceStatus.ONLINE_AUTH:
                        StatusText = string.Format("Connected to {0} | Auth", Device.Name);
                        break;
                }
            }
            else {
                StatusText = "No device connected";
            }
        }

        private void OnDevicePropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == "Status") {
                Application.Current.Dispatcher.Invoke(delegate {
                    DeviceUpdate();
                });

                if (Device != null) {
                    // Connection lost, we try to re-connect
                    if (Device.Status == DeviceStatus.OFFLINE && _guard) {
                        _guard = false;
                        Device.Connect();
                    }
                    else if (Device.Status != DeviceStatus.OFFLINE) {
                        _guard = true;
                    }
                }
            }
            else if (e.PropertyName == "HeartrateMonitorStarted") {
                CommandManager.InvalidateRequerySuggested();
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
        
        ICommand _command_auto_connect;

        public ICommand Command_Auto_Connect {
            get {
                return _command_auto_connect ??= new RelayCommand<object>("connect",
                    "Connect to a device", o => {
                        _autoConnectBluetooth = new BLE();

                        var defaultDeviceVersion = DefaultDeviceVersionSetting;
                        var defaultDeviceName = DefaultDeviceName;
                        var defaultDeviceAuthKey = DefaultDeviceAuthKey;

                        async void OnBluetoothAdded(object sender, BluetoothDevice args) {
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

                            if (_autoConnectBluetooth != null) {
                                _autoConnectBluetooth.DeviceFound -= OnBluetoothAdded;
                            }
                        }

                        _autoConnectBluetooth.DeviceFound += OnBluetoothAdded;
                        _ = _autoConnectBluetooth.ScanForDevicesAsync();
                    }, o => Device == null || Device.Status == DeviceStatus.OFFLINE);
            }
        }

        ICommand _command_connect;

        public ICommand Command_Connect {
            get {
                return _command_connect ??= new RelayCommand<object>("connect",
                    "Connect to a device", o => {
                        var dialog = new ConnectionWindow(this);
                        dialog.ShowDialog();
                    }, o => Device == null || Device.Status == DeviceStatus.OFFLINE);
            }
        }

        ICommand _command_disconnect;

        public ICommand Command_Disconnect {
            get {
                return _command_disconnect ??= new RelayCommand<object>("disconnect",
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

        ICommand _command_start;

        public ICommand Command_Start {
            get {
                return _command_start ??= new RelayCommand<object>("device.start",
                    "Start heartrate monitoring", o => {
                        Device.StartHeartrateMonitor(ContinuousMode);

                        if (EnableFileOutput) {
                            _fileOutput = new DeviceHeartrateFileOutput("heartrate.txt", Device);
                        }

                        if (EnableCSVOutput) {
                            _csvOutput = new DeviceHeartrateCSVOutput("heartrate.csv", Device);
                        }

                        if (EnableOscOutput) {
                            _oscOutput = new DeviceHeartrateOscOutput(Device);
                        }
                    },
                    o => Device is {Status: DeviceStatus.ONLINE_AUTH, HeartrateMonitorStarted: false});
            }
        }

        ICommand _command_stop;

        public ICommand Command_Stop {
            get {
                return _command_stop ??= new RelayCommand<object>("device.stop",
                    "Stop heartrate monitoring", o => {
                        Device.StopHeartrateMonitor();

                        _fileOutput = null;
                        _csvOutput = null;
                        _oscOutput = null;
                    }, o => Device is {HeartrateMonitorStarted: true});
            }
        }
    }
}
