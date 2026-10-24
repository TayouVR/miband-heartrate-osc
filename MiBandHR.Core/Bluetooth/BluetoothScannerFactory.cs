using MiBandHR.Core.Bluetooth.InTheHand;
#if !WINDOWS
using MiBandHR.Core.Bluetooth.BlueZ;
using MiBandHR.Core.Bluetooth.Salar;
#endif

#if WINDOWS
using MiBandHR.Core.Bluetooth.Windows;
#endif

namespace MiBandHR.Core.Bluetooth;

/// <summary>
/// Selects the <see cref="IBluetoothScanner"/> backend.
///
/// The configured backend (see <see cref="BluetoothBackend"/>) is honoured when
/// it is actually available on the current platform. If it is not (e.g. the
/// configuration was written on a different platform and the file is shared
/// across machines), the factory silently falls back to the first available
/// backend <b>without</b> rewriting the saved setting.
/// </summary>
public static class BluetoothScannerFactory {
    /// <summary>
    /// Creates a scanner for the given <paramref name="backend"/>. When
    /// <see cref="BluetoothBackend.Auto"/> is passed, the platform default is
    /// used. If the requested backend is unavailable on this platform, the
    /// platform default is used instead.
    /// </summary>
    public static IBluetoothScanner Create(BluetoothBackend backend) {
        // Windows.Devices.Bluetooth (WinRT) is Windows-only.
        var windowsAvailable = OperatingSystem.IsWindows();

        // BlueZ and Salar.BluetoothLE's backends are Linux/BlueZ only.
        var linuxAvailable = OperatingSystem.IsLinux();

        // InTheHand.BluetoothLE works everywhere.
        var inTheHandAvailable = true;

        var effective = backend switch {
            BluetoothBackend.Windows when windowsAvailable => BluetoothBackend.Windows,
            BluetoothBackend.BlueZ when linuxAvailable => BluetoothBackend.BlueZ,
            BluetoothBackend.Salar when linuxAvailable => BluetoothBackend.Salar,
            BluetoothBackend.InTheHand when inTheHandAvailable => BluetoothBackend.InTheHand,
            BluetoothBackend.Auto => DefaultBackend(windowsAvailable, linuxAvailable),
            // Requested backend unavailable on this platform -> platform default.
            _ => DefaultBackend(windowsAvailable, linuxAvailable)
        };

        switch (effective) {
#if WINDOWS
            case BluetoothBackend.Windows:
                return new WindowsBluetoothScanner();
#endif
#if !WINDOWS
            case BluetoothBackend.BlueZ:
                return new BlueZBluetoothScanner();
            case BluetoothBackend.Salar:
                return new SalarBluetoothScanner();
#endif
            default:
                return new InTheHandBluetoothScanner();
        }
    }

    /// <summary>Creates the platform-default scanner.</summary>
    public static IBluetoothScanner CreateDefault() => Create(BluetoothBackend.Auto);

    /// <summary>Returns the backends available on the current platform, in preference order.</summary>
    public static IReadOnlyList<BluetoothBackend> AvailableBackends() {
        var list = new List<BluetoothBackend> { BluetoothBackend.Auto };
        if (OperatingSystem.IsWindows()) list.Add(BluetoothBackend.Windows);
        list.Add(BluetoothBackend.InTheHand);
        if (OperatingSystem.IsLinux()) {
            list.Add(BluetoothBackend.BlueZ);
            list.Add(BluetoothBackend.Salar);
        }

        return list;
    }

    private static BluetoothBackend DefaultBackend(bool windowsAvailable, bool linuxAvailable)
        => linuxAvailable
            ? BluetoothBackend.BlueZ
            : windowsAvailable
                ? BluetoothBackend.Windows
                : BluetoothBackend.InTheHand;
}
