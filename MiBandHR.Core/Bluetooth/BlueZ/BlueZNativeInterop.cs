#if !WINDOWS
using Linux.Bluetooth;
using Tmds.DBus;
using GattCharacteristic = Linux.Bluetooth.GattCharacteristic;

namespace MiBandHR.Core.Bluetooth.BlueZ;

/// <summary>
/// BlueZ (<c>Linux.Bluetooth</c>) helpers for device connection, GATT writes
/// and descriptor enumeration.
/// </summary>
internal static class BlueZNativeInterop {
    private static readonly TimeSpan PropertyWaitTimeout = TimeSpan.FromSeconds(15);

    // ------------------------------------------------------------------
    // Device
    // ------------------------------------------------------------------

    /// <summary>True when the BlueZ device reports Connected = true.</summary>
    public static async Task<bool> GetConnectedAsync(Device nativeDevice) {
        return (await nativeDevice.GetAllAsync()).Connected;
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
        await WaitForDevicePropertyAsync(nativeDevice, p => p.Connected, "Connected", timeout);
        await WaitForDevicePropertyAsync(nativeDevice, p => p.ServicesResolved, "ServicesResolved", timeout);
    }

    /// <summary>Disconnects the device (best effort).</summary>
    public static async Task Disconnect(Device nativeDevice) {
        try {
            await nativeDevice.DisconnectAsync();
        } catch {
            // Device may already be gone.
        }
    }

    // ------------------------------------------------------------------
    // Characteristics
    // ------------------------------------------------------------------

    /// <summary>Writes a value to the characteristic with response (reliable write).</summary>
    public static Task WriteValueAsync(GattCharacteristic nativeCharacteristic, byte[] data) {
        // BlueZ D-Bus: type="request" = reliable write (write with response).
        var options = new Dictionary<string, object> { ["type"] = "request" };
        return nativeCharacteristic.WriteValueAsync(data, options);
    }

    /// <summary>
    /// Writes a value to the characteristic without waiting for a response.
    /// The Mi Band protocol requires this write type for all command frames.
    ///
    /// BlueZ D-Bus: When the type option is omitted, BlueZ auto-selects the
    /// write procedure based on the characteristic's Properties flags. Retries
    /// on the transient <c>org.bluez.Error.InProgress</c> error.
    /// </summary>
    public static async Task WriteValueWithoutResponseAsync(GattCharacteristic nativeCharacteristic, byte[] data) {
        // Omit the "type" option so BlueZ auto-selects the write procedure.
        var options = new Dictionary<string, object>();

        int attempt = 0;
        while (true) {
            attempt++;
            try {
                await nativeCharacteristic.WriteValueAsync(data, options);
                return;
            } catch (Exception ex) when (BlueZErrors.IsInProgress(ex) && attempt < 5) {
                await Task.Delay(50);
            }
        }
    }

    /// <summary>
    /// Enumerates the GATT descriptors of a characteristic.
    ///
    /// This is a workaround for a BlueZ quirk: some peripherals (notably the Mi
    /// Band) do not start delivering notifications after <c>StartNotify</c>
    /// alone — forcing BlueZ to enumerate the descriptors (a GetManagedObjects
    /// round-trip) makes the notification delivery "wake up".
    /// </summary>
    public static async Task PrimeNotificationsAsync(GattCharacteristic nativeCharacteristic) {
        var objectManager = Connection.System.CreateProxy<IObjectManager>("org.bluez", "/");
        var objects = await objectManager.GetManagedObjectsAsync();

        foreach (var (path, interfaces) in objects) {
            if (!interfaces.ContainsKey("org.bluez.GattDescriptor1")) continue;
            if (!path.ToString().StartsWith(nativeCharacteristic.ObjectPath + "/")) continue;
            await Connection.System.CreateProxy<IGattDescriptor1>("org.bluez", path).GetAllAsync();
        }
    }

    // ------------------------------------------------------------------
    // Property polling
    // ------------------------------------------------------------------

    private static async Task WaitForDevicePropertyAsync(Device device, Func<Device1Properties, bool> predicate,
        string name, TimeSpan timeout) {
        var deadline = DateTimeOffset.Now + timeout;
        while (DateTimeOffset.Now < deadline) {
            if (predicate(await device.GetAllAsync())) return;
            await Task.Delay(100);
        }

        throw new TimeoutException($"Timed out waiting for {name}.");
    }
}
#endif
