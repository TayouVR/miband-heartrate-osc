using System.Diagnostics;

namespace MiBandHR.Core.Devices
{
    public class Dummy_Device : Device
    {
        bool _running;

        Thread _worker;

        public Dummy_Device() {
            Name = "Dummy";
        }

        public override void Dispose()
        {
            Disconnect();
        }

        public override void Authenticate()
        {
            if (Status == MiBandHR.Core.Devices.DeviceStatus.ONLINE_UNAUTH)
            {
                Status = MiBandHR.Core.Devices.DeviceStatus.ONLINE_AUTH;
            }
        }

        public override void Connect()
        {
            if (Status == MiBandHR.Core.Devices.DeviceStatus.OFFLINE)
            {
                Status = MiBandHR.Core.Devices.DeviceStatus.ONLINE_UNAUTH;
            }
        }

        public override void Disconnect()
        {
            StopHeartrateMonitor();

            if (Status != MiBandHR.Core.Devices.DeviceStatus.OFFLINE)
            {
                Status = MiBandHR.Core.Devices.DeviceStatus.OFFLINE;
            }
        }

        public override void StartHeartrateMonitor(bool continuous = false)
        {
            if ( ! _running && _worker == null)
            {
                _running = true;

                _worker = new Thread(FakeHeartrateValueWorker);
                _worker.Start();

                HeartrateMonitorStarted = true;
            }
        }

        public override void StopHeartrateMonitor()
        {
            if (_running || _worker != null)
            {
                _running = false;
                _worker = null;
            }
        }

        void FakeHeartrateValueWorker()
        {
            var rnd = new Random();
            
            try {
                while (_running)
                {
                    Heartrate = (ushort)rnd.Next(55, 180);
                    Thread.Sleep(3000);
                }
            } catch (ThreadInterruptedException) {
                Debug.WriteLine("Dummy Device Alive Thread Killed");
            }

            HeartrateMonitorStarted = false;
        }
    }
}
