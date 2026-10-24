// Configuration/AppConfig.cs

using MiBandHR.Core.Bluetooth;

namespace MiBandHR.Core.Configuration;

public class AppConfig {
    public DeviceConfig Device { get; set; } = new();
    public MonitoringConfig Monitoring { get; set; } = new();
    public OutputConfig Output { get; set; } = new();
    public WindowConfig Window { get; set; } = new();
    public LogWindowConfig LogWindow { get; set; } = new();
    public OscConfig Osc { get; set; } = new();
}

public class DeviceConfig {
    public bool UseAutoConnect { get; set; }

    /// <summary>Selected Bluetooth backend. When unavailable on the current platform the app falls back without overwriting this value.</summary>
    public BluetoothBackend BluetoothBackend { get; set; } = BluetoothBackend.Auto;

    public string AutoConnectDeviceVersion { get; set; } = "3";
    public string AutoConnectDeviceName { get; set; } = "Mi Band 3";
    public string AutoConnectDeviceAuthKey { get; set; } = "0123456789abcdef0123456789abcdef";
    public bool TryConnectOnStartup { get; set; }

    /// <summary>
    /// Persisted Mi Band 2/3 authentication keys, keyed by device address (hex).
    /// Mi Band 2/3 generate a random key on first pairing and the band then
    /// stores it, so the host must reuse the exact same key on every reconnect.
    /// </summary>
    public Dictionary<string, string> AuthKeys { get; set; } = new();
}

public class MonitoringConfig {
    public bool ContinuousMode { get; set; } = true;

    /// <summary>Graph range in seconds (0 = full session).</summary>
    public int GraphRangeSeconds { get; set; } = 60;

    /// <summary>Last recorded minimum heart rate (0 = none recorded yet).</summary>
    public ushort MinHeartrate { get; set; }

    /// <summary>Last recorded maximum heart rate (0 = none recorded yet).</summary>
    public ushort MaxHeartrate { get; set; }
}

public class OutputConfig {
    public bool FileEnabled { get; set; }
    public bool CsvEnabled { get; set; }
    public bool OscEnabled { get; set; } = true;
}

/// <summary>
/// Last known window geometry of the GUI frontend. Persisted in the shared
/// TOML configuration so it works on every platform (the old WPF app stored
/// this in the Windows registry).
/// </summary>
public class WindowConfig {
    public int Left { get; set; } = -1;
    public int Top { get; set; } = -1;
    public int Width { get; set; } = 440;
    public int Height { get; set; } = 220;
}

public class LogWindowConfig {
    public bool IsOpen { get; set; }
    public int? Left { get; set; }
    public int? Top { get; set; }
    public int Width { get; set; } = 520;
    public int Height { get; set; } = 400;
}

public class OscConfig {
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 9000;
}