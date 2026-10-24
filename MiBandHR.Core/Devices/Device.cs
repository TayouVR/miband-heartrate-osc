using System.ComponentModel;
using MiBandHR.Core.Configuration;

namespace MiBandHR.Core.Devices {
    public enum DeviceStatus {
        Offline,
        Connecting,
        Authenticating,
        OnlineUnauth,
        OnlineAuth
    }

    public enum DeviceModel {
        [Description("Hidden")] Dummy,

        // ReSharper disable once InconsistentNaming
        [Description("Mi Band 2/3")] Miband2_3,

        // ReSharper disable once InconsistentNaming
        [Description("Mi Band 4/5")] Miband4_5
    }

    public abstract class Device : INotifyPropertyChanged, IAsyncDisposable {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected Device() {
            try {
                _minHeartrate = ConfigurationManager.Instance.CurrentConfig.Monitoring.MinHeartrate;
                _maxHeartrate = ConfigurationManager.Instance.CurrentConfig.Monitoring.MaxHeartrate;
            } catch (InvalidOperationException) {
                // Configuration not initialized (e.g. design-time data).
            }
        }


        public string Name {
            get;
            protected init {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        } = "";

        public DeviceStatus Status {
            get;
            internal set {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            }
        } = DeviceStatus.Offline;


        public DeviceModel Model { get; internal set; }


        public ushort Heartrate {
            get;
            internal set {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Heartrate)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MinHeartrate)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MaxHeartrate)));
            }
        }


        ushort _minHeartrate;

        public ushort? MinHeartrate {
            get {
                if (Heartrate != 0 && (_minHeartrate == 0 || Heartrate < _minHeartrate)) {
                    _minHeartrate = Heartrate;
                    SaveMinMaxHeartrate();
                }

                return _minHeartrate == 0 ? (ushort?)null : _minHeartrate;
            }
        }


        ushort _maxHeartrate;

        public ushort? MaxHeartrate {
            get {
                if (Heartrate > _maxHeartrate) {
                    _maxHeartrate = Heartrate;
                    SaveMinMaxHeartrate();
                }

                return _maxHeartrate == 0 ? (ushort?)null : _maxHeartrate;
            }
        }

        private void SaveMinMaxHeartrate() {
            try {
                if (MinHeartrate == null || MaxHeartrate == null) return;

                var monitoring = ConfigurationManager.Instance.CurrentConfig.Monitoring;
                monitoring.MinHeartrate = MinHeartrate ?? 0; // guarded above
                monitoring.MaxHeartrate = MaxHeartrate ?? 0; // guarded above
                ConfigurationManager.Instance.SaveConfiguration();
            } catch (InvalidOperationException) {
                // Configuration not initialized.
            }
        }


        public bool HeartrateMonitorStarted {
            get;
            internal set {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("HeartrateMonitorStarted"));
            }
        }

        protected string DeviceId { get; init; } = "";

        // --------------------------------------

        public abstract Task ConnectAsync(CancellationToken cancellationToken = default);

        public abstract Task AuthenticateAsync(CancellationToken cancellationToken = default);

        public abstract Task DisconnectAsync(CancellationToken cancellationToken = default);

        public abstract Task StartHeartrateMonitorAsync(bool continuous = false,
            CancellationToken cancellationToken = default);

        public abstract Task StopHeartrateMonitorAsync(CancellationToken cancellationToken = default);

        public abstract ValueTask DisposeAsync();
    }
}
