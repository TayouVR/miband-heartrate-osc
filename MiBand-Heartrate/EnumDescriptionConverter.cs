using System;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using MiBand_Heartrate.Devices;

namespace MiBand_Heartrate; 

public class EnumDescriptionConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value is DeviceModel dm) {
            return dm.GetDescription();
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value is string description) {
            return Enum.GetValues(typeof(DeviceModel))
                .Cast<DeviceModel>()
                .FirstOrDefault(dm => dm.GetDescription().Equals(description));
        }

        return DeviceModel.DUMMY;
    }
}