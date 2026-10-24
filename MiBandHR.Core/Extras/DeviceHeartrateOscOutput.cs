using System.ComponentModel;
using System.Net.Sockets;
using MiBandHR.Core.Bluetooth;
using MiBandHR.Core.Configuration;
using MiBandHR.Core.Devices;
using OscCore;

namespace MiBandHR.Core.Extras;

public class DeviceHeartrateOscOutput : IAsyncDisposable {
    private readonly Device _device;

    private readonly Socket _socket;

    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _sendLoopTask;
    private bool _disposed;

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
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.Connect( ConfigurationManager.Instance.CurrentConfig.Osc.Host, ConfigurationManager.Instance.CurrentConfig.Osc.Port);

        _device = device;

        _device.PropertyChanged += OnDeviceChanged;
        if (_device.HeartrateMonitorStarted) OnHeartrateMonitorStarted();
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) return;
        _disposed = true;
        _device.PropertyChanged -= OnDeviceChanged;
        Cancel();
        if (_sendLoopTask != null) {
            try {
                await _sendLoopTask.ConfigureAwait(false);
            } catch (OperationCanceledException) {
                // Expected while stopping the OSC heartbeat worker.
            } catch (Exception exception) {
                BluetoothLog.Error($"OSC heartbeat worker failed: {exception.Message}");
            } finally {
                _socket.Dispose();
            }
        } else {
            _socket.Dispose();
        }
    }

    private void Cancel() {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    private void OnDeviceChanged(object? sender, PropertyChangedEventArgs e) {
        if (_disposed) return;
        var device = sender as Device;
        if (device == null) return;

        switch (e.PropertyName) {
            case "HeartrateMonitorStarted":
                if (device.HeartrateMonitorStarted) {
                    OnHeartrateMonitorStarted();
                }
                else {
                    OnHeartrateMonitorStopped();
                }

                break;
            case "Heartrate":
                OnChangeHeartrate();
                break;
        }

        // this might send a bit often, should check if thats an issue
        SendOSCMessages([Addresses.DeviceConnected, Addresses.HRConnected], _hrConnected);
    }

    private void OnChangeHeartrate() {
        var heartRateInt = (int)_device.Heartrate;
        var heartRateFloat = _device.Heartrate / 127f - 1f;
        var heartRateFloat01 = _device.Heartrate / 255f;
        var minHeartRate = (int)(_device.MinHeartrate ?? 0);
        var maxHeartRate = (int)(_device.MaxHeartrate ?? 0);

        SendOSCMessages([
            Addresses.HeartRateInt,
            Addresses.HeartRate3,
            Addresses.HRInt
        ], heartRateInt);

        SendOSCMessages([
            Addresses.HeartRateFloat,
            Addresses.HeartRate,
            Addresses.HeartRateFloatHR,
            Addresses.HRFloat
        ], heartRateFloat);

        SendOSCMessages([
            Addresses.HeartRateFloat01,
            Addresses.HeartRate2,
            Addresses.HRFloatHalf
        ], heartRateFloat01);

        SendOSCMessages([
            Addresses.MinHeartRate
        ], minHeartRate);

        SendOSCMessages([
            Addresses.MaxHeartRate
        ], maxHeartRate);
    }

    private void OnHeartrateMonitorStarted() {
        if (_disposed || _sendLoopTask is { IsCompleted: false }) return;
        _hrConnected = true;
        SendOSCMessages([Addresses.DeviceConnected, Addresses.HRConnected], _hrConnected);

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        _sendLoopTask = SendLoop(_cancellationTokenSource.Token);
    }

    private void OnHeartrateMonitorStopped() {
        _hrConnected = false;
        SendOSCMessages([Addresses.DeviceConnected, Addresses.HRConnected], _hrConnected);

        Cancel();
    }

    private async Task SendLoop(CancellationToken cancellationToken) {
        try {
            while (true) {
                if (_device.Heartrate == 0) {
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                var span = (int)(60.0f / _device.Heartrate * 1000);
                await Task.Delay(span, cancellationToken);
                await Send(span, cancellationToken);

                if (cancellationToken.IsCancellationRequested) {
                    break;
                }
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Expected when monitoring stops or the output is disposed.
        }
    }

    private async Task Send(int span, CancellationToken cancellationToken) {
        SendOSCMessages([Addresses.HeartBeatInt], 1);
        SendOSCMessages([Addresses.HeartBeatPulse, Addresses.HRBeat], true);
        SendOSCMessages([Addresses.HeartBeatToggle, Addresses.HRBeatToggle], _currentBeatToggle);

        // Wait for QRS interval
        await Task.Delay(span / 5, cancellationToken);

        SendOSCMessages([Addresses.HeartBeatInt], 0);
        SendOSCMessages([Addresses.HeartBeatPulse, Addresses.HRBeat], false);

        _currentBeatToggle = !_currentBeatToggle;
    }

    private void SendOSCMessages(string[] addresses, params object[] args) {
        if (!ConfigurationManager.Instance.CurrentConfig.Output.OscEnabled) return;
        foreach (var address in addresses) {
            try {
                _socket.Send(new OscMessage(address, args).ToByteArray());
            } catch (SocketException) {
                // The OSC receiver (e.g. VRChat) is not listening. UDP is
                // best-effort, so drop the packet rather than crashing the app.
            } catch (ObjectDisposedException) {
                // Socket already closed during shutdown.
            }
        }
    }
}
