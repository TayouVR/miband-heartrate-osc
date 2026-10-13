using Linux.Bluetooth;
using Tmds.DBus;

namespace MiBandHR.Core.Bluetooth.InTheHand;

/// <summary>
/// Reflection-based bridge to the native BlueZ (<c>Linux.Bluetooth</c>)
/// characteristic proxy on Linux.
///
/// The native object is an <c>IGattCharacteristic1</c> D-Bus proxy. It is
/// referenced through <see cref="object"/> (and invoked via reflection) so
/// that this assembly does not need a hard reference to the Linux-only
/// <c>Linux.Bluetooth</c> package — that package is only present at runtime
/// when InTheHand.BluetoothLE selects its BlueZ backend.
///
/// This centralizes:
/// <list type="bullet">
///   <item><description>Subscribing to characteristic value changes
///   (<c>WatchPropertiesAsync</c>).</description></item>
///   <item><description>Starting notifications (<c>StartNotifyAsync</c>) with
///   retry on the transient <c>org.bluez.Error.InProgress</c> D-Bus error.
///   </description></item>
/// </list>
/// </summary>
internal static class LinuxBlueZInterop {
    private const int MaxStartNotifyAttempts = 5;
    private static readonly TimeSpan StartNotifyRetryDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Starts BlueZ notifications, retrying on the transient
    /// <c>org.bluez.Error.InProgress</c> error which BlueZ returns when a
    /// previous operation on the device has not fully settled yet.
    ///
    /// For characteristics with the <c>encrypt-notify</c> flag, BlueZ
    /// automatically triggers the security procedure (encryption key exchange)
    /// when StartNotify is called. This is transparent to the caller but may
    /// add a brief delay.
    /// </summary>
    public static async Task StartNotifyWithRetryAsync(GattCharacteristic nativeCharacteristic) {
        int attempt = 0;
        while (true) {
            attempt++;
            try {
                await nativeCharacteristic.StartNotifyAsync();

                if (attempt > 1) {
                    BluetoothLog.Success($"StartNotify succeeded after {attempt} attempts.");
                }

                return;
            }
            catch (Exception ex) when (BlueZErrors.IsInProgress(ex) && attempt < MaxStartNotifyAttempts) {
                BluetoothLog.Retry(
                    $"StartNotify busy (org.bluez.Error.InProgress), retrying ({attempt}/{MaxStartNotifyAttempts})...");
                await Task.Delay(StartNotifyRetryDelay);
            }
            catch (Exception ex) when (BlueZErrors.IsInProgress(ex)) {
                BluetoothLog.Error($"StartNotify still busy after {MaxStartNotifyAttempts} attempts, giving up.");
                throw;
            }
            catch (Exception ex) {
                BluetoothLog.Error($"StartNotify failed (attempt {attempt}): {ex.Message}");
                throw;
            }
        }
    }

    /// <summary>Stops BlueZ notifications.</summary>
    public static async Task StopNotifyAsync(GattCharacteristic nativeCharacteristic) {
        try {
            await nativeCharacteristic.StopNotifyAsync();
        } catch {
            // Stopping notify is best-effort; the connection may already be gone.
        }
    }
}