using MiBandHR.Core.Bluetooth.InTheHand;
using MiBandHR.Core.Bluetooth.Salar;

namespace MiBandHR.Core.Bluetooth;

/// <summary>
/// Selects the platform-appropriate <see cref="IBluetoothScanner"/> backend.
///
/// On Linux the default is <see cref="SalarBluetoothScanner"/> (Salar.BluetoothLE),
/// whose BlueZ backend connects to devices <b>without pairing</b> — the
/// behaviour the Mi Band 3 requires. Everywhere else (Windows, macOS) the
/// <see cref="InTheHandBluetoothScanner"/> (InTheHand.BluetoothLE) is used.
/// </summary>
public static class BluetoothScannerFactory
{
    /// <summary>Creates the default scanner for the current platform.</summary>
    public static IBluetoothScanner CreateDefault()
    {
        return OperatingSystem.IsLinux()
            ? new SalarBluetoothScanner()
            : new InTheHandBluetoothScanner();
    }
}
