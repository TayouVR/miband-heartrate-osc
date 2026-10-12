using System.ComponentModel;
using MiBandHR.Core.Configuration;

namespace MiBandHR.Core.Devices
{
    public enum DeviceStatus { OFFLINE, ONLINE_UNAUTH, ONLINE_AUTH }

    public enum DeviceModel {
        [Description("Hidden")]
        DUMMY,

        [Description("Mi Band 2/3")]
        MIBAND_2_3,

        [Description("Mi Band 4/5")]
        MIBAND_4
    }

    public abstract class Device : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected Device()
        {
            try
            {
                _minHeartrate = ConfigurationManager.Instance.CurrentConfig.Monitoring.MinHeartrate;
                _maxHeartrate = ConfigurationManager.Instance.CurrentConfig.Monitoring.MaxHeartrate;
            }
            catch (InvalidOperationException)
            {
                // Configuration not initialized (e.g. design-time data).
            }
        }


        string _name = "";

        public string Name
        {
            get { return _name; }
            set
            {
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Name"));
            }
        }

        DeviceStatus _status = Devices.DeviceStatus.OFFLINE;

        public DeviceStatus Status
        {
            get { return _status; }
            internal set
            {
                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Status"));
            }
        }


        public DeviceModel Model { get; internal set; }


        ushort _heartrate;

        public ushort Heartrate {
            get { return _heartrate; }
            internal set {
                _heartrate = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Heartrate"));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("MinHeartrate"));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("MaxHeartrate"));
            }
        }


        ushort _minHeartrate;

        public ushort? MinHeartrate {
            get { 
                if (_heartrate != 0 && (_minHeartrate == 0 || _heartrate < _minHeartrate)) {
                    _minHeartrate = _heartrate;
                    SaveMinMaxHeartrate();
                }
                return _minHeartrate == 0 ? (ushort?)null : _minHeartrate;
            }
        }


        ushort _maxHeartrate;

        public ushort? MaxHeartrate {
            get { 
                if (_heartrate > _maxHeartrate) {
                    _maxHeartrate = _heartrate;
                    SaveMinMaxHeartrate();
                }
                return _maxHeartrate == 0 ? (ushort?)null : _maxHeartrate;
            }
        }

        private void SaveMinMaxHeartrate()
        {
            try
            {
                var monitoring = ConfigurationManager.Instance.CurrentConfig.Monitoring;
                monitoring.MinHeartrate = _minHeartrate;
                monitoring.MaxHeartrate = _maxHeartrate;
                ConfigurationManager.Instance.SaveConfiguration();
            }
            catch (InvalidOperationException)
            {
                // Configuration not initialized.
            }
        }


        bool _heartrateMonitorStarted;

        public bool HeartrateMonitorStarted
        {
            get { return _heartrateMonitorStarted; }
            internal set
            {
                _heartrateMonitorStarted = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("HeartrateMonitorStarted"));
            }
        }

        public string DeviceId { get; internal set; } = "";

        // --------------------------------------

        public abstract void Dispose();

        public abstract void Connect();

        public abstract void Disconnect();

        public abstract void Authenticate();

        public abstract void StartHeartrateMonitor(bool continuous = false);

        public abstract void StopHeartrateMonitor();
    }
}
