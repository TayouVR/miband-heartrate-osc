using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using MiBandHR.Core;
using MiBandHR.Core.Bluetooth;
using MiBandHR.Core.Devices;

namespace MiBand_Heartrate
{
    public class ConnectionWindowViewModel : ViewModel
    {
        ObservableCollection<IBluetoothDevice> _devices = new ObservableCollection<IBluetoothDevice>();

        public ObservableCollection<IBluetoothDevice> Devices
        {
            get { return _devices; }
            set
            {
                _devices = value;
                InvokePropertyChanged("Devices");
            }
        }

        IBluetoothDevice _selectedDevice;

        public IBluetoothDevice SelectedDevice
        {
            get { return _selectedDevice; }
            set
            {
                _selectedDevice = value;
                InvokePropertyChanged("SelectedDevice");
            }
        }

        DeviceModel _deviceModel = DeviceModel.MIBAND_2_3;

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
            
            _bluetooth = new BLE();

            _bluetooth.DeviceFound += OnBluetoothAdded;
            _ = _bluetooth.ScanForDevicesAsync();
        }

        ~ConnectionWindowViewModel()
        {
            if (_bluetooth != null)
            {
                _bluetooth.DeviceFound -= OnBluetoothAdded;
            }
        }

        private async void OnBluetoothAdded(object sender, IBluetoothDevice args)
        {
            if (Devices.Any(d => d.Id == args.Id)) return;

            try
            {
                var services = await args.GetPrimaryServicesAsync();
                if (services.Any(srv => srv.Uuid == new System.Guid("0000180d-0000-1000-8000-00805f9b34fb")))
                {
                    Devices.Add(args);
                }
            }
            catch
            {
                // Ignore
            }
        }

        // --------------------------------------

        ICommand _command_connect;

        public ICommand Command_Connect
        {
            get
            {
                if (_command_connect == null)
                {
                    _command_connect = new RelayCommand<object>("connection.connect", "Connect to selected device", o =>
                    {
                        Device device = null;

                        switch (DeviceModel)
                        {
                            case DeviceModel.DUMMY:
                                device = new Dummy_Device();
                                break;
                            case DeviceModel.MIBAND_2_3:
                                device = new MiBand2_3_Device(SelectedDevice);
                                break;
                            case DeviceModel.MIBAND_4:
                                var authWindow = new AuthenticationKeyWindow();

                                if ((bool)authWindow.ShowDialog())
                                {
                                    device = new MiBand4_Device(SelectedDevice, authWindow.AuthenticationKeyResult);
                                }

                                break;
                        }

                        if (device != null)
                        {
                            device.Connect();
                            
                            if (Main != null)
                            {
                                Main.Device = device;
                            }

                            if (View != null)
                            {
                                View.DialogResult = true;
                                View.Close();
                            }
                        }

                    }, o => {
                        return SelectedDevice != null;
                    });
                }

                return _command_connect;
            }
        }

        ICommand _command_cancel;

        public ICommand Command_Cancel
        {
            get
            {
                if (_command_cancel == null)
                {
                    _command_cancel = new RelayCommand<object>("connection.cancel", "Cancel connection", o =>
                    {
                        if (View != null)
                        {
                            View.DialogResult = false;
                            View.Close();
                        }
                    });
                }

                return _command_cancel;
            }
        }
    }
}
