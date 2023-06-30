using System.ComponentModel;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MiBand_Heartrate.Devices;
using OscCore;

namespace MiBand_Heartrate.Extras
{
    public class DeviceHeartrateOscOutput
    {
        Device _device;

        private UdpClient _udpClient;

        private CancellationTokenSource _cancellationTokenSource;

        private static class Addresses
        {
            private const string ParametersAddress = "/avatar/parameters";

            /// <summary>
            /// Heart rate par min [0, 255]
            /// </summary>
            public static readonly string HeartRateInt = $"{ParametersAddress}/HeartRateInt";
            public static readonly string HeartRate3 = $"{ParametersAddress}/HeartRate3";

            /// <summary>
            /// Normalized Heart rate ([0, 255] -> [-1, 1])
            /// </summary>
            public static readonly string HeartRateFloat = $"{ParametersAddress}/HeartRateFloat";
            public static readonly string HeartRate = $"{ParametersAddress}/HeartRate";

            /// <summary>
            /// Normalized Heart rate ([0, 255] -> [0, 1])
            /// </summary>
            public static readonly string HeartRateFloat01 = $"{ParametersAddress}/HeartRateFloat01";
            public static readonly string HeartRate2 = $"{ParametersAddress}/HeartRate2";

            /// <summary>
            /// 1 : QRS Interval (Temporarily set it to 1/5 of the RR interval)
            /// 0 : Other times
            /// </summary>
            public static readonly string HeartBeatInt = $"{ParametersAddress}/HeartBeatInt";

            /// <summary>
            /// True : QRS Interval (Temporarily set it to 1/5 of the RR interval)
            /// False : Other times
            /// </summary>
            public static readonly string HeartBeatPulse = $"{ParametersAddress}/HeartBeatPulse";

            /// <summary>
            /// Reverses with each heartbeat
            /// </summary>
            public static readonly string HeartBeatToggle = $"{ParametersAddress}/HeartBeatToggle";
        }


        private bool _currentBeatToggle;

        public DeviceHeartrateOscOutput(Device device)
        {
            // Choose an unused port at random
            _udpClient = new UdpClient();
            _udpClient.Connect("localhost", 9000);
            
            _device = device;

            if (_device != null)
            {
                _device.PropertyChanged += OnDeviceChanged;
            }
        }

        ~DeviceHeartrateOscOutput()
        {
            _device.PropertyChanged -= OnDeviceChanged;
            _udpClient.Close();
            Cancel();
        }

        private void Cancel()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        private void OnDeviceChanged(object sender, PropertyChangedEventArgs e)
        {
            var device = sender as Device;
            if(device == null) return;
            
            switch (e.PropertyName)
            {
                case "HeartrateMonitorStarted":
                    if (device.HeartrateMonitorStarted)
                    {
                        OnHeartrateMonitorStarted();
                    }
                    else
                    {
                        OnHeartrateMonitorStopped();
                    }
                    break;
                case "Heartrate":
                    OnChangeHeartrate();
                    break;
            }
        }

        private void OnChangeHeartrate()
        {
            var heartRateInt = (int)_device.Heartrate;
            var heartRateFloat = _device.Heartrate / 127f - 1f;
            var heartRateFloat01 = _device.Heartrate / 255f;

            _udpClient.Send(new OscMessage(Addresses.HeartRateInt, heartRateInt).ToByteArray());
            _udpClient.Send(new OscMessage(Addresses.HeartRate3, heartRateInt).ToByteArray());

            _udpClient.Send(new OscMessage(Addresses.HeartRateFloat, heartRateFloat).ToByteArray());
            _udpClient.Send(new OscMessage(Addresses.HeartRate, heartRateFloat).ToByteArray());

            _udpClient.Send(new OscMessage(Addresses.HeartRateFloat01, heartRateFloat01).ToByteArray());
            _udpClient.Send(new OscMessage(Addresses.HeartRate2, heartRateFloat01).ToByteArray());
        }

        private void OnHeartrateMonitorStarted()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            var _ = SendLoop(_cancellationTokenSource.Token);
        }

        private void OnHeartrateMonitorStopped()
        {
            Cancel();
        }

        private async Task SendLoop(CancellationToken cancellationToken)
        {
            while (true)
            {
                if (_device.Heartrate == 0)
                {
                    await Task.Delay(100, cancellationToken);
                    continue;
                }
                var span = (int)(60.0f / _device.Heartrate * 1000);
                await Task.Delay(span, cancellationToken);
                var _ = Send(span, cancellationToken);
                
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task Send(int span, CancellationToken cancellationToken)
        {
            _udpClient.Send(new OscMessage(Addresses.HeartBeatInt, 1).ToByteArray());
            _udpClient.Send(new OscMessage(Addresses.HeartBeatPulse, true).ToByteArray());
            _udpClient.Send(new OscMessage(Addresses.HeartBeatToggle, _currentBeatToggle).ToByteArray());
            
            // Wait for QRS interval
            await Task.Delay(span / 5, cancellationToken);

            _udpClient.Send(new OscMessage(Addresses.HeartBeatInt, 0).ToByteArray());
            _udpClient.Send(new OscMessage(Addresses.HeartBeatPulse, false).ToByteArray());
            
            _currentBeatToggle = !_currentBeatToggle;
        }
    }
}