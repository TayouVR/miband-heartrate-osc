using System.ComponentModel;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MiBandHR.Core.Configuration;
using MiBandHR.Core.Devices;
using OscCore;

namespace MiBand_Heartrate.Extras; 

public class DeviceHeartrateOscOutput {
    Device _device;

    private UdpClient _udpClient;
    private Socket _socket;

    private CancellationTokenSource _cancellationTokenSource;

    private static class Addresses {
        private const string ParametersAddress = "/avatar/parameters";
            
        private const string ApplicationPrefix = "HR";

        /// <summary>
        /// Heart rate par min [0, 255]
        /// </summary>
        public const string HeartRateInt = $"{ParametersAddress}/HeartRateInt";
        public const string HeartRate3 = $"{ParametersAddress}/HeartRate3";
        public const string HRInt = $"{ParametersAddress}/{ApplicationPrefix}/Int";

        /// <summary>
        /// Normalized Heart rate ([0, 255] -> [-1, 1])
        /// </summary>
        public const string HeartRateFloat = $"{ParametersAddress}/HeartRateFloat";
        public const string HeartRate = $"{ParametersAddress}/HeartRate";
        public const string HeartRateFloatHR = $"{ParametersAddress}/floatHR";
        public const string HRFloat = $"{ParametersAddress}/{ApplicationPrefix}/Float";

        /// <summary>
        /// Normalized Heart rate ([0, 255] -> [0, 1])
        /// </summary>
        public const string HeartRateFloat01 = $"{ParametersAddress}/HeartRateFloat01";
        public const string HeartRate2 = $"{ParametersAddress}/HeartRate2";
        public const string HRFloatHalf = $"{ParametersAddress}/{ApplicationPrefix}/HalfFloat";

        /// <summary>
        /// 1 : QRS Interval (Temporarily set it to 1/5 of the RR interval)
        /// 0 : Other times
        /// </summary>
        public const string HeartBeatInt = $"{ParametersAddress}/HeartBeatInt";

        /// <summary>
        /// True : QRS Interval (Temporarily set it to 1/5 of the RR interval)
        /// False : Other times
        /// </summary>
        public const string HeartBeatPulse = $"{ParametersAddress}/HeartBeatPulse";
        public const string HRBeat = $"{ParametersAddress}/{ApplicationPrefix}/Beat";

        /// <summary>
        /// Reverses with each heartbeat
        /// </summary>
        public const string HeartBeatToggle = $"{ParametersAddress}/HeartBeatToggle";
        public const string HRBeatToggle = $"{ParametersAddress}/{ApplicationPrefix}/BeatToggle";

        /// <summary>
        /// Is Device connected and sending data
        /// </summary>
        public const string DeviceConnected = $"{ParametersAddress}/isHRConnected";
        public const string HRConnected = $"{ParametersAddress}/{ApplicationPrefix}/Connected";

        /// <summary>
        /// Min and Max Heartrate in session
        /// </summary>
        public const string MinHeartRate = $"{ParametersAddress}/{ApplicationPrefix}/Min";
        public const string MaxHeartRate = $"{ParametersAddress}/{ApplicationPrefix}/Max";
    }


    private bool _currentBeatToggle;
    private bool _hrConnected;

    public DeviceHeartrateOscOutput(Device device) {
        // Choose an unused port at random
        _udpClient = new UdpClient();
        _udpClient.Connect("localhost", 9000);

        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.Connect("localhost", 9000);
            
        _device = device;

        if (_device != null) {
            _device.PropertyChanged += OnDeviceChanged;
        }
    }

    ~DeviceHeartrateOscOutput() {
        _device.PropertyChanged -= OnDeviceChanged;
        _udpClient.Close();
        Cancel();
    }

    private void Cancel() {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    private void OnDeviceChanged(object sender, PropertyChangedEventArgs e) {
        var device = sender as Device;
        if(device == null) return;
            
        switch (e.PropertyName) {
            case "HeartrateMonitorStarted":
                if (device.HeartrateMonitorStarted) {
                    OnHeartrateMonitorStarted();
                } else {
                    OnHeartrateMonitorStopped();
                }
                break;
            case "Heartrate":
                OnChangeHeartrate();
                break;
        }
            
        // this might send a bit often, should check if thats an issue
        SendOSCMessages(new[]{Addresses.DeviceConnected, Addresses.HRConnected}, _hrConnected);
    }

    private void OnChangeHeartrate() {
        var heartRateInt = (int)_device.Heartrate;
        var heartRateFloat = _device.Heartrate / 127f - 1f;
        var heartRateFloat01 = _device.Heartrate / 255f;

        SendOSCMessages(new[]{
            Addresses.HeartRateInt, 
            Addresses.HeartRate3, 
            Addresses.HRInt
        }, heartRateInt);

        SendOSCMessages(new[] {
            Addresses.HeartRateFloat, 
            Addresses.HeartRate, 
            Addresses.HeartRateFloatHR, 
            Addresses.HRFloat
        }, heartRateFloat);

        SendOSCMessages(new[] {
            Addresses.HeartRateFloat01, 
            Addresses.HeartRate2, 
            Addresses.HRFloatHalf
        }, heartRateFloat01);
    }

    private void OnHeartrateMonitorStarted() {
        _hrConnected = true;
        SendOSCMessages(new[]{Addresses.DeviceConnected, Addresses.HRConnected}, _hrConnected);
            
        _cancellationTokenSource = new CancellationTokenSource();
        var _ = SendLoop(_cancellationTokenSource.Token);
    }

    private void OnHeartrateMonitorStopped() {
        _hrConnected = false;
        SendOSCMessages(new[]{Addresses.DeviceConnected, Addresses.HRConnected}, _hrConnected);
            
        Cancel();
    }

    private async Task SendLoop(CancellationToken cancellationToken) {
        while (true) {
            if (_device.Heartrate == 0) {
                await Task.Delay(100, cancellationToken);
                continue;
            }
            var span = (int)(60.0f / _device.Heartrate * 1000);
            await Task.Delay(span, cancellationToken);
            var _ = Send(span, cancellationToken);
                
            if (cancellationToken.IsCancellationRequested) {
                break;
            }
        }
    }

    private async Task Send(int span, CancellationToken cancellationToken) {
        SendOSCMessages(new[]{Addresses.HeartBeatInt}, 1);
        SendOSCMessages(new[]{Addresses.HeartBeatPulse, Addresses.HRBeat}, true);
        SendOSCMessages(new[]{Addresses.HeartBeatToggle, Addresses.HRBeatToggle}, _currentBeatToggle);
            
        // Wait for QRS interval
        await Task.Delay(span / 5, cancellationToken);

        SendOSCMessages(new[]{Addresses.HeartBeatInt}, 0);
        SendOSCMessages(new[]{Addresses.HeartBeatPulse, Addresses.HRBeat}, false);
            
        _currentBeatToggle = !_currentBeatToggle;
    }

    private void SendOSCMessages(string[] addresses, params object[] args) {
        if (!ConfigurationManager.Instance.CurrentConfig.Output.OscEnabled) return;
        foreach (var address in addresses) {
            _socket.Send(new OscMessage(address, args).ToByteArray());
        }
    }
}