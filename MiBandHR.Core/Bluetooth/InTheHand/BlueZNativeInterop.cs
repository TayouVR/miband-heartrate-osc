using System.Reflection;
using InTheHand.Bluetooth;
using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;
using Tmds.DBus;
using GattCharacteristic = Linux.Bluetooth.GattCharacteristic;

namespace MiBandHR.Core.Bluetooth.InTheHand;

/// <summary>
/// Reflection bridge to the native BlueZ (<c>Linux.Bluetooth</c>) D-Bus
/// proxies. All native objects are handled as <see cref="object"/> so this
/// assembly keeps no compile-time dependency on the Linux-only
/// <c>Linux.Bluetooth</c> package; the types are resolved at runtime, where
/// they are guaranteed to be present (InTheHand.BluetoothLE already ships the
/// BlueZ backend on Linux).
/// </summary>
internal static class BlueZNativeInterop {
    private static readonly TimeSpan PropertyWaitTimeout = TimeSpan.FromSeconds(15);

    // ------------------------------------------------------------------
    // Device
    // ------------------------------------------------------------------

    /// <summary>True when the BlueZ device reports Connected = true.</summary>
    public static async Task<bool> GetConnectedAsync(Device nativeDevice) {
        // Linux.Bluetooth 5.x changed GetAsync<T> to a generic method that can't
        // be invoked via reflection without knowing T at compile time.
        // Use GetAllAsync() instead, which returns a properties DTO with typed
        // properties we can read via reflection.
        var props = await nativeDevice.GetAllAsync();
        var connectedProp = props.GetType().GetProperty("Connected");
        return connectedProp?.GetValue(props) is bool b && b;
    }

    /// <summary>
    /// Connects to the device, then waits for Connected and ServicesResolved.
    ///
    /// No BlueZ-level pairing is performed: the Mi Band authentication is done
    /// at the application layer (AES challenge/response), so forcing BlueZ to
    /// pair would only trigger an extra "confirm pairing" prompt on the band
    /// without adding anything. For already-bonded devices BlueZ establishes the
    /// encrypted link lazily when <c>StartNotify</c> is called on an
    /// <c>encrypt-notify</c> characteristic.
    /// </summary>
    public static async Task ConnectNoPairingAsync(Device nativeDevice, TimeSpan timeout) {
        await nativeDevice.ConnectAsync();
        await WaitForPropertyPollAsync(nativeDevice, "Connected", true, timeout);
        await WaitForPropertyPollAsync(nativeDevice, "ServicesResolved", true, timeout);
    }

    /// <summary>Disconnects the device (best effort).</summary>
    public static async Task Disconnect(Device nativeDevice) {
        try {
            await nativeDevice.DisconnectAsync();
        }
        catch {
            // Device may already be gone.
        }
    }

    /// <summary>Writes a value to the characteristic with response (reliable write).</summary>
    public static Task WriteValueAsync(GattCharacteristic nativeCharacteristic, byte[] data) {
        // WriteValueAsync(byte[] Value, IDictionary<string, object> Options)
        // BlueZ D-Bus: type="request" = reliable write (write with response)
        var options = new Dictionary<string, object> { ["type"] = "request" };

        return nativeCharacteristic.WriteValueAsync(data, options);
    }

    /// <summary>
    /// Writes a value to the characteristic without waiting for a response.
    /// The Mi Band protocol requires this write type for all command frames.
    ///
    /// BlueZ D-Bus: When the type option is omitted, BlueZ auto-selects the
    /// write procedure based on the characteristic's Properties flags.
    /// This is the same behavior as btleplug/bluez-async on Linux, and it
    /// works for characteristics that support "write" but not "write-without-response".
    ///
    /// Retries on transient <c>org.bluez.Error.InProgress</c> errors, just like
    /// the bleak Python library does.
    /// </summary>
    public static async Task WriteValueWithoutResponseAsync(GattCharacteristic nativeCharacteristic, byte[] data) {
        // Omit the "type" option so BlueZ auto-selects the write procedure
        // based on the characteristic's Properties. This matches the behavior
        // of btleplug/bluez-async and tinygo-org/bluetooth on Linux.
        // If the characteristic has the "write-without-response" flag, BlueZ
        // will use "command"; otherwise it falls back to "request".
        var options = new Dictionary<string, object>();

        int attempt = 0;
        while (true) {
            attempt++;
            try {
                await nativeCharacteristic.WriteValueAsync(data, options);
                return;
            }
            catch (Exception ex) when (BlueZErrors.IsInProgress(ex) && attempt < 5) {
                await Task.Delay(50);
            }
        }
    }

    /// <summary>
    /// Enumerates and reads the GATT descriptors of a characteristic.
    ///
    /// This is a workaround for a BlueZ quirk: some peripherals (notably the Mi
    /// Band) do not start delivering notifications after <c>StartNotify</c>
    /// alone — forcing BlueZ to enumerate the descriptors (a GetManagedObjects
    /// round-trip) makes the notification delivery "wake up". The CCCD is not
    /// written here; BlueZ manages it via <c>StartNotify</c>.
    /// </summary>
    public static async Task PrimeNotificationsAsync(GattCharacteristic nativeCharacteristic) {

        var getProxiesMethod =
            typeof(BlueZManager).GetMethod("GetProxiesAsync", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BlueZManager.GetProxiesAsync not found.");
        var getProxiesGeneric = getProxiesMethod.MakeGenericMethod(typeof(IGattDescriptor1));

        var task = (Task?)getProxiesGeneric.Invoke(null,
            ["org.bluez.GattDescriptor1", nativeCharacteristic]);
        if (task == null) return;
        await task;

        if (task.GetType().GetProperty("Result")?.GetValue(task) is not IReadOnlyList<IGattDescriptor1> descriptors) {
            return;
        }

        foreach (var descriptor in descriptors) {
            var uuid = await descriptor.GetAllAsync();
            // BluetoothLog.Info($"  Descriptor: {uuid}");
        }
    }

    /// <summary>
    /// Reads a single property from the properties DTO returned by GetAllAsync.
    /// </summary>
    private static async Task<object?> GetPropertyAsync(Device nativeProxy, string propertyName) {
        var props = await nativeProxy.GetAllAsync();
        var prop = props.GetType().GetProperty(propertyName);
        return prop?.GetValue(props);
    }

    /// <summary>
    /// Polls a device property until it reaches the desired value or the timeout
    /// elapses. Replaces the old <c>WaitForPropertyValueAsync</c> which relied on
    /// a generic extension method that can't be invoked via reflection in .NET 10.
    /// </summary>
    private static async Task WaitForPropertyPollAsync(Device nativeProxy, string propertyName, object targetValue,
        TimeSpan timeout) {
        var deadline = DateTimeOffset.Now + timeout;
        while (DateTimeOffset.Now < deadline) {
            var currentValue = await GetPropertyAsync(nativeProxy, propertyName);
            if (Equals(currentValue, targetValue)) {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"Timed out waiting for property '{propertyName}' to become '{targetValue}'.");
    }
}
