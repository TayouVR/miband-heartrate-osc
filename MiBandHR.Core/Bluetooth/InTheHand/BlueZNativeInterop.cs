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
    public static bool GetConnected(object nativeDevice)
    {
        // Read the cached D-Bus property synchronously via GetAsync("Connected").
        // The property is returned as Task<object>; block on it (it is local
        // and fast) — the connection state must be answerable synchronously.
        var value = GetPropertyAsync(nativeDevice, "Connected").GetAwaiter().GetResult();
        return value is bool b && b;
    }

    /// <summary>
    /// Connects to the device WITHOUT pairing, then waits for Connected and
    /// ServicesResolved. Mirrors Salar.BluetoothLE's Linux connect behaviour.
    /// </summary>
    public static async Task ConnectNoPairingAsync(object nativeDevice, TimeSpan timeout)
    {
        await InvokeTask(nativeDevice, "ConnectAsync");
        await WaitForPropertyValueAsync(nativeDevice, "Connected", true, timeout);
        await WaitForPropertyValueAsync(nativeDevice, "ServicesResolved", true, timeout);
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

    /// <summary>Writes a value to the characteristic with response.</summary>
    public static async Task WriteValueAsync(object nativeCharacteristic, byte[] data)
    {
        // WriteValueAsync(byte[] Value, IDictionary<string, object> Options)
        var options = new Dictionary<string, object> { ["type"] = "request" };
        var method = nativeCharacteristic.GetType().GetMethod("WriteValueAsync")
            ?? throw new InvalidOperationException("Characteristic has no WriteValueAsync.");
        var task = (Task?)method.Invoke(nativeCharacteristic, new object[] { data, options });
        if (task != null) await task;
    }

    // ------------------------------------------------------------------
    // Property helpers
    // ------------------------------------------------------------------

    private static async Task<object?> GetPropertyAsync(object nativeDevice, string propertyName)
    {
        var method = nativeDevice.GetType().GetMethod("GetAsync")
            ?? throw new InvalidOperationException("Native device has no GetAsync.");
        var task = (Task?)method.Invoke(nativeDevice, new object[] { propertyName });
        if (task == null) return null;
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    /// <summary>
    /// Waits until a device property reaches the given value (or timeout),
    /// using <c>Linux.Bluetooth.Extensions.Extensions.WaitForPropertyValueAsync</c>.
    /// </summary>
    private static async Task WaitForPropertyValueAsync(object nativeDevice, string propertyName, object value, TimeSpan timeout)
    {
        var extensionType = GetExtensionType("Linux.Bluetooth.Extensions.Extensions");
        var method = extensionType.GetMethods()
            .FirstOrDefault(m => m.Name == "WaitForPropertyValueAsync" && m.IsGenericMethod)
            ?? throw new InvalidOperationException("WaitForPropertyValueAsync not found.");

        var generic = method.MakeGenericMethod(value.GetType());
        var task = (Task?)generic.Invoke(null, new[] { nativeDevice, propertyName, value, timeout });
        if (task != null) await task;
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
