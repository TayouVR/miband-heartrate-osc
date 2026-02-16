using System;
using System.ComponentModel;
using System.IO;
using MiBandHR.Core.Configuration;
using MiBandHR.Core.Devices;

namespace MiBand_Heartrate.Extras
{
    public class DeviceHeartrateCSVOutput
    {
        Device _device;

        string _filename;

        public DeviceHeartrateCSVOutput(string filename, Device device)
        {
            _filename = filename;

            _device = device;

            if (_device != null)
            {
                _device.PropertyChanged += OnDeviceChanged;
            }
        }

        ~DeviceHeartrateCSVOutput()
        {
            _device.PropertyChanged -= OnDeviceChanged;
        }

        private void OnDeviceChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Heartrate" && ConfigurationManager.Instance.CurrentConfig.Output.CsvEnabled)
            {
                try
                {
                    if (!File.Exists(_filename))
                    {
                        using (StreamWriter f = new StreamWriter(_filename))
                        {
                            f.WriteLine("At,Heartrate");
                        }
                    }

                    using (StreamWriter f = new StreamWriter(_filename, true))
                    {
                        f.WriteLine($"{DateTime.Now},{_device.Heartrate}");
                    }
                }
                catch (Exception err)
                {
                    Console.WriteLine(err.ToString());
                }
            }
        }
    }
}