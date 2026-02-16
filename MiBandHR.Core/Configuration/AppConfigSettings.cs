// Configuration/AppConfig.cs
namespace MiBandHR.Core.Configuration;

public class AppConfig
{
    public DeviceConfig Device { get; set; } = new();
    public MonitoringConfig Monitoring { get; set; } = new();
    public OutputConfig Output { get; set; } = new();
}

public class DeviceConfig
{
    public bool UseAutoConnect { get; set; }
    public string AutoConnectDeviceVersion { get; set; } = "3";
    public string AutoConnectDeviceName { get; set; } = "Mi Band 3";
    public string AutoConnectDeviceAuthKey { get; set; } = "0123456789abcdef0123456789abcdef";
    public bool TryConnectOnStartup { get; set; }
}

public class MonitoringConfig
{
    public bool ContinuousMode { get; set; } = true;
}

public class OutputConfig
{
    public bool FileEnabled { get; set; }
    public bool CsvEnabled { get; set; }
    public bool OscEnabled { get; set; } = true;
}