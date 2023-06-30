using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Windows.Data;
using System.Windows.Input;
using Windows.Devices.Enumeration;
using MiBand_Heartrate.Devices;

namespace MiBand_Heartrate
{
    public class ConnectionWindowViewModel : ViewModel
    {
        private ObservableCollection<DeviceInformation> _devices = new ObservableCollection<DeviceInformation>();

        public ObservableCollection<DeviceInformation> Devices
        {
            get => _devices;
            set
            {
                _devices = value;
                InvokePropertyChanged("Devices");
            }
        }

        private DeviceInformation _selectedDevice;

        public DeviceInformation SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                _selectedDevice = value;
                InvokePropertyChanged("SelectedDevice");
            }
        }

        private DeviceModel _deviceModel = DeviceModel.MIBAND_2_3;

        public DeviceModel DeviceModel
        {
            get { return _deviceModel; }
            set
            {
                _deviceModel = value;
                InvokePropertyChanged("DeviceModel");
            }
        }


        public ConnectionWindow View { get; set; }

        public MainWindowViewModel Main { get; set; }


        BLE _bluetooth;

        // --------------------------------------

        public ConnectionWindowViewModel()
        {
            // Enables a CollectionView object to participate in synchronized access to a collection that is used on multiple threads.
            BindingOperations.EnableCollectionSynchronization(Devices, new object());
            
            _bluetooth = new BLE(new string[] {"System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected"});

            _bluetooth.Watcher.Added += OnBluetoothAdded;
            _bluetooth.Watcher.Updated += OnBluetoothUpdated;
            _bluetooth.Watcher.Removed += OnBluetoothRemoved;

            _bluetooth.StartWatcher();
        }

        ~ConnectionWindowViewModel()
        {
            if (_bluetooth.Watcher != null)
            {
                _bluetooth.Watcher.Added -= OnBluetoothAdded;
                _bluetooth.Watcher.Updated -= OnBluetoothUpdated;
                _bluetooth.Watcher.Removed -= OnBluetoothRemoved;
            }
            
            _bluetooth.StopWatcher();
        }

        private void OnBluetoothRemoved(DeviceWatcher sender, DeviceInformationUpdate args) {
            DeviceInformation[] pending = Devices.Where(x => x.Id == args.Id).ToArray();
            foreach (var deviceInfo in pending) {
                Devices.Remove(deviceInfo);
            }
        }

        private void OnBluetoothUpdated(DeviceWatcher sender, DeviceInformationUpdate args)
        {
            foreach (DeviceInformation d in Devices) {
                if (d.Id != args.Id) continue;
                d.Update(args);
                break;
            }
        }

        private void OnBluetoothAdded(DeviceWatcher sender, DeviceInformation args)
        {
            Devices.Add(args);
        }

        // --------------------------------------

        private ICommand _commandConnect;

        public ICommand CommandConnect
        {
            get {
                return _commandConnect ?? (_commandConnect = new RelayCommand<object>("connection.connect",
                    "Connect to selected device", o => {
                        Device device = null;

                        switch (DeviceModel) {
                            case DeviceModel.DUMMY:
                            default:
                                device = new Dummy_Device();
                                break;
                            case DeviceModel.MIBAND_2_3:
                                device = new MiBand2_3_Device(SelectedDevice);
                                break;
                            case DeviceModel.MIBAND_4:
                                var authWindow = new AuthenticationKeyWindow();

                                if ((bool) authWindow.ShowDialog()) {
                                    device = new MiBand4_Device(SelectedDevice, authWindow.AuthenticationKeyResult);
                                }

                                break;
                        }

                        if (device == null) return;
                        device.Connect();

                        if (Main != null) {
                            Main.Device = device;
                        }

                        if (View == null) return;
                        View.DialogResult = true;
                        View.Close();
                    }, o => SelectedDevice != null));
            }
        }

        private ICommand _commandCancel;

        public ICommand CommandCancel
        {
            get {
                return _commandCancel ??= new RelayCommand<object>("connection.cancel",
                    "Cancel connection", o => {
                        if (View == null) return;
                        View.DialogResult = false;
                        View.Close();
                    });
            }
        }
    }
}
