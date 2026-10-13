namespace MiBandHR.Core.Bluetooth;

/// <summary>
/// Selectable Bluetooth backends.
///
/// <list type="bullet">
///   <item><description><see cref="Auto"/> – let the factory pick the platform
///   default.</description></item>
///   <item><description><see cref="Windows"/> – original
///   <c>Windows.Devices.Bluetooth</c> (WinRT) implementation, Windows only.</description></item>
///   <item><description><see cref="InTheHand"/> – InTheHand.BluetoothLE, all platforms.</description></item>
///   <item><description><see cref="BlueZ"/> – native BlueZ via Linux.Bluetooth, Linux only.</description></item>
///   <item><description><see cref="Salar"/> – Salar.BluetoothLE, Linux only.</description></item>
/// </list>
/// </summary>
public enum BluetoothBackend
{
    Auto,
    Windows,
    InTheHand,
    Salar,
    BlueZ
}