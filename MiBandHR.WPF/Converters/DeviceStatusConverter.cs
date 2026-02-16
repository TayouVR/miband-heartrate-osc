using System;
using System.Globalization;
using System.Windows.Data;
using MiBandHR.Core.Devices;

namespace MiBandHR.WPF.Converters
{
    public class DeviceStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string r = "No device connected";

            if (value is Device)
            {
                Device device = (Device)value;

                switch (device.Status)
                {
                    case DeviceStatus.ONLINE_UNAUTH:
                        r = string.Format("Connected to {0} | Not auth", device.Name);
                        break;
                    case DeviceStatus.ONLINE_AUTH:
                        r = string.Format("Connected to {0} | Auth", device.Name);
                        break;
                }
            }

            return r;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
