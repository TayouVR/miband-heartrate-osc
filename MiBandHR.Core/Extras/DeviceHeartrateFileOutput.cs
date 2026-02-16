using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using MiBandHR.Core.Configuration;
using MiBandHR.Core.Devices;

namespace MiBand_Heartrate.Extras
{
    public class DeviceHeartrateFileOutput
    {
        Device _device;

        string _filename;

        public DeviceHeartrateFileOutput(string filename, Device device)
        {
            _filename = filename;

            _device = device;

            if (_device != null)
            {
                _device.PropertyChanged += OnDeviceChanged;
            }
        }

        ~DeviceHeartrateFileOutput()
        {
            _device.PropertyChanged -= OnDeviceChanged;
        }

        private void OnDeviceChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Heartrate" && ConfigurationManager.Instance.CurrentConfig.Output.FileEnabled)
            {
                try
                {
                    using (var f = File.Open(_filename, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        byte[] data = Encoding.UTF8.GetBytes(_device.Heartrate.ToString());
                        f.Write(data, 0, data.Length);
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