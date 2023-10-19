using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Data;
using System.Windows.Input;
using Windows.Devices.Enumeration;
using MiBand_Heartrate.Devices;
using MiBand_Heartrate.ViewModels;
using MiBand_Heartrate.Views;

namespace MiBand_Heartrate; 

public class ConnectionWindowViewModel : ViewModel {
    ObservableCollection<DeviceInformation> _devices = new();

    public ObservableCollection<DeviceInformation> Devices
    {
        get => _devices;
        set
        {
            _devices = value;
            InvokePropertyChanged("Devices");
        }
    }

    DeviceInformation _selectedDevice;

    public DeviceInformation SelectedDevice
    {
        get => _selectedDevice ?? Devices[0];
        set
        {
            _selectedDevice = value;
            InvokePropertyChanged("SelectedDevice");
        }
    }

    DeviceModel _deviceModel = DeviceModel.MIBAND_2_3;

    public DeviceModel DeviceModel
    {
        get => _deviceModel;
        set
        {
            _deviceModel = value;
            InvokePropertyChanged("DeviceModel");
        }
    }
    
    public IEnumerable<string> DeviceModelsDescriptions
    {
        get
        {
            return Enum.GetValues(typeof(DeviceModel))
                .Cast<DeviceModel>()
                .Select(dm => dm.GetDescription());
        }
    }
    
    public IEnumerable<DeviceModel> DeviceModels => Enum.GetValues<DeviceModel>();

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

    private void OnBluetoothRemoved(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        DeviceInformation[] pending = Devices.Where(x => x.Id == args.Id).ToArray();
        for (int i = 0; i < pending.Length; ++i) {
            Devices.Remove(pending[i]);
        }
    }

    private void OnBluetoothUpdated(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        foreach (DeviceInformation d in Devices)
        {
            if (d.Id == args.Id) {
                d.Update(args);
                break;
            }
        }
    }

    private void OnBluetoothAdded(DeviceWatcher sender, DeviceInformation args)
    {
        Devices.Add(args);
    }

    // --------------------------------------

    ICommand? _commandConnect;
    
    public ICommand CommandConnect
    {
        get {
            return _commandConnect ?? (_commandConnect = new RelayCommand<object>("connection.connect",
                "Connect to selected device", o => {
                    Device device = null;

                    switch (DeviceModel) {
                        case DeviceModel.DUMMY:
                            device = new Dummy_Device();
                            break;
                        case DeviceModel.MIBAND_2_3:
                            device = new MiBand2_3_Device(SelectedDevice);
                            break;
                        case DeviceModel.MIBAND_4:
                            var authWindow = new AuthenticationKeyWindow();
                            var authWindowTask = authWindow.ShowDialog(View);

                            if ((bool) authWindowTask.IsCompleted) {
                                device = new MiBand4_Device(SelectedDevice, authWindow.AuthenticationKeyResult);
                            }

                            break;
                    }

                    if (device != null) {
                        device.Connect();

                        if (Main != null) {
                            Main.Device = device;
                        }

                        if (View != null) {
                            //View.DialogResult = true;
                            View.Close();
                        }
                    }
                }, o => { return SelectedDevice != null; }));
        }
    }

    ICommand? _commandCancel;
    public ICommand CommandCancel
    {
        get {
            return _commandCancel ?? (_commandCancel = new RelayCommand<object>("connection.cancel",
                "Cancel connection", o => {
                    if (View != null) {
                        //View.DialogResult = false;
                        View.Close();
                    }
                }));
        }
    }
}