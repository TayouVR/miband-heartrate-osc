using System.ComponentModel;
using MiBandHR.Core.Configuration;
using MiBandHR.Core.Devices;

namespace MiBandHR.Core.Extras {
    public class DeviceHeartrateCsvOutput {
        private readonly Device _device;

        private readonly string _filename;

        public DeviceHeartrateCsvOutput(string filename, Device device) {
            _filename = filename;

            _device = device;

            _device.PropertyChanged += OnDeviceChanged;
        }

        ~DeviceHeartrateCsvOutput() {
            _device.PropertyChanged -= OnDeviceChanged;
        }

        private void OnDeviceChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == "Heartrate" && ConfigurationManager.Instance.CurrentConfig.Output.CsvEnabled) {
                try {
                    if (!File.Exists(_filename)) {
                        using var f = new StreamWriter(_filename);
                        f.WriteLine("At,Heartrate");
                    }

                    using (var f = new StreamWriter(_filename, true)) {
                        f.WriteLine($"{DateTime.Now},{_device.Heartrate}");
                    }
                } catch (Exception err) {
                    Console.WriteLine(err.ToString());
                }
            }
        }
    }
}
