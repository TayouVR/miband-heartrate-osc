using System.Reflection;
using InTheHand.Bluetooth;

namespace MiBandHR.Core.Bluetooth.InTheHand;

/// <summary>
/// Reflection bridge to the native BlueZ (<c>Linux.Bluetooth</c>) D-Bus
/// proxies. All native objects are handled as <see cref="object"/> so this
/// assembly keeps no compile-time dependency on the Linux-only
/// <c>Linux.Bluetooth</c> package; the types are resolved at runtime, where
/// they are guaranteed to be present (InTheHand.BluetoothLE already ships the
/// BlueZ backend on Linux).
/// </summary>
internal static class BlueZNativeInterop
{
    private static readonly TimeSpan PropertyWaitTimeout = TimeSpan.FromSeconds(15);

    // ------------------------------------------------------------------
    // Device
    // ------------------------------------------------------------------

    /// <summary>True when the BlueZ device reports Connected = true.</summary>
    public static async Task<bool> GetConnectedAsync(object nativeDevice)
    {
        // Linux.Bluetooth 5.x changed GetAsync<T> to a generic method that can't
        // be invoked via reflection without knowing T at compile time.
        // Use GetAllAsync() instead, which returns a properties DTO with typed
        // properties we can read via reflection.
        var props = await GetAllPropertiesAsync(nativeDevice);
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
    public static async Task ConnectNoPairingAsync(object nativeDevice, TimeSpan timeout)
    {
        await InvokeTask(nativeDevice, "ConnectAsync");
        await WaitForPropertyPollAsync(nativeDevice, "Connected", true, timeout);
        await WaitForPropertyPollAsync(nativeDevice, "ServicesResolved", true, timeout);
    }

    /// <summary>Disconnects the device (best effort).</summary>
    public static void Disconnect(object nativeDevice)
    {
        try
        {
            InvokeTask(nativeDevice, "DisconnectAsync").GetAwaiter().GetResult();
        }
        catch
        {
            // Device may already be gone.
        }
    }

    // ------------------------------------------------------------------
    // Services
    // ------------------------------------------------------------------

    /// <summary>Resolves a primary GATT service by UUID string, or null.</summary>
    public static async Task<object?> GetServiceAsync(object nativeDevice, string uuid)
    {
        var extensionType = GetExtensionType("Linux.Bluetooth.Extensions.Extensions");
        var method = extensionType.GetMethod("GetServiceAsync")
            ?? throw new InvalidOperationException("Linux.Bluetooth.Extensions.Extensions.GetServiceAsync not found.");

        // GetServiceAsync(IDevice1, string) — static extension method.
        var task = (Task?)method.Invoke(null, new[] { nativeDevice, uuid });
        if (task == null) return null;
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    /// <summary>Lists all GATT services of the device.</summary>
    public static async Task<IReadOnlyList<object>> GetServicesAsync(object nativeDevice)
    {
        var extensionType = GetExtensionType("Linux.Bluetooth.Extensions.Extensions");
        var method = extensionType.GetMethod("GetServicesAsync")
            ?? throw new InvalidOperationException("Linux.Bluetooth.Extensions.Extensions.GetServicesAsync not found.");

        var task = (Task?)method.Invoke(null, new[] { nativeDevice });
        if (task == null) return Array.Empty<object>();
        await task;

        if (task.GetType().GetProperty("Result")?.GetValue(task) is System.Collections.IEnumerable enumerable)
        {
            return enumerable.Cast<object>().ToList();
        }
        return Array.Empty<object>();
    }

    /// <summary>Reads a service's UUID (lowercase, no dashes).</summary>
    public static async Task<string> GetServiceUuidAsync(object nativeService)
    {
        var ext = GetExtensionType("Linux.Bluetooth.GattService1Extensions");
        var method = ext.GetMethod("GetUUIDAsync")
            ?? throw new InvalidOperationException("GattService1Extensions.GetUUIDAsync not found.");
        var task = (Task?)method.Invoke(null, new[] { nativeService });
        if (task == null) return "";
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task) as string ?? "";
    }

    // ------------------------------------------------------------------
    // Characteristics
    // ------------------------------------------------------------------

    /// <summary>Resolves a characteristic of a service by UUID string, or null.</summary>
    public static async Task<object?> GetCharacteristicAsync(object nativeService, string uuid)
    {
        var extensionType = GetExtensionType("Linux.Bluetooth.Extensions.Extensions");
        var method = extensionType.GetMethods()
            .FirstOrDefault(m => m.Name == "GetCharacteristicAsync" && m.GetParameters().Length == 2)
            ?? throw new InvalidOperationException("Extensions.GetCharacteristicAsync(service, uuid) not found.");

        var task = (Task?)method.Invoke(null, new[] { nativeService, uuid });
        if (task == null) return null;
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    /// <summary>Reads a characteristic's UUID (lowercase, no dashes).</summary>
    public static async Task<string> GetCharacteristicUuidAsync(object nativeCharacteristic)
    {
        var ext = GetExtensionType("Linux.Bluetooth.GattCharacteristic1Extensions");
        var method = ext.GetMethod("GetUUIDAsync")
            ?? throw new InvalidOperationException("GattCharacteristic1Extensions.GetUUIDAsync not found.");
        var task = (Task?)method.Invoke(null, new[] { nativeCharacteristic });
        if (task == null) return "";
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task) as string ?? "";
    }

    /// <summary>Writes a value to the characteristic with response (reliable write).</summary>
    public static async Task WriteValueAsync(object nativeCharacteristic, byte[] data)
    {
        // WriteValueAsync(byte[] Value, IDictionary<string, object> Options)
        // BlueZ D-Bus: type="request" = reliable write (write with response)
        var options = new Dictionary<string, object> { ["type"] = "request" };
        var method = nativeCharacteristic.GetType().GetMethod("WriteValueAsync")
            ?? throw new InvalidOperationException("Characteristic has no WriteValueAsync.");
        var task = (Task?)method.Invoke(nativeCharacteristic, new object[] { data, options });
        if (task != null) await task;
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
    public static async Task WriteValueWithoutResponseAsync(object nativeCharacteristic, byte[] data)
    {
        // Omit the "type" option so BlueZ auto-selects the write procedure
        // based on the characteristic's Properties. This matches the behavior
        // of btleplug/bluez-async and tinygo-org/bluetooth on Linux.
        // If the characteristic has the "write-without-response" flag, BlueZ
        // will use "command"; otherwise it falls back to "request".
        var options = new Dictionary<string, object>();
        var method = nativeCharacteristic.GetType().GetMethod("WriteValueAsync")
            ?? throw new InvalidOperationException("Characteristic has no WriteValueAsync.");

        int attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                var task = (Task?)method.Invoke(nativeCharacteristic, new object[] { data, options });
                if (task != null) await task;
                return;
            }
            catch (Exception ex) when (IsBlueZInProgress(ex) && attempt < 5)
            {
                await Task.Delay(50);
            }
        }
    }

    /// <summary>
    /// True when the exception (or any inner exception) is the BlueZ
    /// <c>org.bluez.Error.InProgress</c> D-Bus error.
    /// </summary>
    private static bool IsBlueZInProgress(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            var errorName = current.GetType().GetProperty("ErrorName")?.GetValue(current) as string;
            if (errorName == "org.bluez.Error.InProgress")
            {
                return true;
            }
            if (current.Message?.Contains("org.bluez.Error.InProgress") == true)
            {
                return true;
            }
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Property helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Reads the BlueZ characteristic flags (e.g. "read", "write",
    /// "write-without-response", "notify", "indicate", "encrypt-notify") for
    /// diagnostic purposes.
    /// </summary>
    public static async Task<string> GetCharacteristicFlagsAsync(object nativeCharacteristic)
    {
        var props = await GetAllPropertiesAsync(nativeCharacteristic);
        var flagsProp = props.GetType().GetProperty("Flags");
        var flags = flagsProp?.GetValue(props);
        if (flags is string[] arr) return string.Join(",", arr);
        if (flags is System.Collections.IEnumerable enumerable)
            return string.Join(",", enumerable.Cast<object>());
        return flags?.ToString() ?? "<none>";
    }

    /// <summary>
    /// Reads the BlueZ "Notifying" property (true once notifications have been
    /// enabled on the peripheral), for diagnostic purposes.
    /// </summary>
    public static async Task<bool> GetNotifyingAsync(object nativeCharacteristic)
    {
        var props = await GetAllPropertiesAsync(nativeCharacteristic);
        var prop = props.GetType().GetProperty("Notifying");
        return prop?.GetValue(props) is bool b && b;
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
    public static async Task PrimeNotificationsAsync(object nativeCharacteristic)
    {
        var assembly = GetLinuxBluetoothAssembly();
        var descriptor1Type = assembly.GetType("Linux.Bluetooth.IGattDescriptor1")
            ?? throw new InvalidOperationException("IGattDescriptor1 not found.");
        var blueZManagerType = assembly.GetType("Linux.Bluetooth.BlueZManager")
            ?? throw new InvalidOperationException("BlueZManager not found.");

        var getProxiesMethod = blueZManagerType.GetMethod("GetProxiesAsync", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BlueZManager.GetProxiesAsync not found.");
        var getProxiesGeneric = getProxiesMethod.MakeGenericMethod(descriptor1Type);

        var task = (Task?)getProxiesGeneric.Invoke(null, new object[] { "org.bluez.GattDescriptor1", nativeCharacteristic });
        if (task == null) return;
        await task;

        if (task.GetType().GetProperty("Result")?.GetValue(task) is not System.Collections.IEnumerable descriptors)
        {
            return;
        }

        foreach (var descriptor in descriptors)
        {
            var uuid = await ReadDescriptorUuidAsync(descriptor);
            BluetoothLog.Info($"  Descriptor: {uuid}");
        }
    }

    private static async Task<string?> ReadDescriptorUuidAsync(object descriptor)
    {
        var getAllMethod = descriptor.GetType().GetMethod("GetAllAsync");
        if (getAllMethod == null) return null;

        var task = (Task?)getAllMethod.Invoke(descriptor, null);
        if (task == null) return null;
        await task;

        var props = task.GetType().GetProperty("Result")?.GetValue(task);
        return props?.GetType().GetProperty("UUID")?.GetValue(props) as string;
    }

    private static System.Reflection.Assembly GetLinuxBluetoothAssembly()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Linux.Bluetooth")
            ?? System.Reflection.Assembly.Load("Linux.Bluetooth");
    }

    /// <summary>
    /// Reads all properties of a native BlueZ proxy via <c>GetAllAsync()</c>,
    /// which returns a typed properties DTO (e.g., <c>Device1Properties</c>).
    /// This avoids the generic <c>GetAsync&lt;T&gt;</c> which can't be invoked
    /// via reflection in .NET 10.
    /// </summary>
    private static async Task<object> GetAllPropertiesAsync(object nativeProxy)
    {
        var method = nativeProxy.GetType().GetMethod("GetAllAsync")
            ?? throw new InvalidOperationException($"{nativeProxy.GetType().Name} has no GetAllAsync.");
        var task = (Task?)method.Invoke(nativeProxy, null);
        if (task == null)
            throw new InvalidOperationException("GetAllAsync returned null.");
        await task;
        var result = task.GetType().GetProperty("Result")?.GetValue(task);
        if (result == null)
            throw new InvalidOperationException("GetAllAsync returned null result.");
        return result;
    }

    /// <summary>
    /// Reads a single property from the properties DTO returned by GetAllAsync.
    /// </summary>
    private static async Task<object?> GetPropertyAsync(object nativeProxy, string propertyName)
    {
        var props = await GetAllPropertiesAsync(nativeProxy);
        var prop = props.GetType().GetProperty(propertyName);
        return prop?.GetValue(props);
    }

    /// <summary>
    /// Polls a device property until it reaches the desired value or the timeout
    /// elapses. Replaces the old <c>WaitForPropertyValueAsync</c> which relied on
    /// a generic extension method that can't be invoked via reflection in .NET 10.
    /// </summary>
    private static async Task WaitForPropertyPollAsync(object nativeProxy, string propertyName, object targetValue, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.Now + timeout;
        while (DateTimeOffset.Now < deadline)
        {
            var currentValue = await GetPropertyAsync(nativeProxy, propertyName);
            if (Equals(currentValue, targetValue))
            {
                return;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"Timed out waiting for property '{propertyName}' to become '{targetValue}'.");
    }

    private static async Task InvokeTask(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName)
            ?? throw new InvalidOperationException($"{target.GetType().Name} has no {methodName}.");
        var task = (Task?)method.Invoke(target, null);
        if (task != null) await task;
    }

    /// <summary>
    /// Resolves a Linux.Bluetooth type from the <c>Linux.Bluetooth</c>
    /// assembly, loading it first if necessary (InTheHand.BluetoothLE loads it
    /// lazily on Linux).
    /// </summary>
    private static Type GetExtensionType(string fullName)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Linux.Bluetooth")
            ?? Assembly.Load("Linux.Bluetooth");

        return assembly.GetType(fullName)
            ?? throw new InvalidOperationException($"Type {fullName} not found in Linux.Bluetooth.");
    }
}
