using System.Reflection;

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
internal static class LinuxBlueZInterop
{
    private const int MaxStartNotifyAttempts = 5;
    private static readonly TimeSpan StartNotifyRetryDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Subscribes to the characteristic's <c>Value</c> D-Bus property changes.
    /// Returns a disposable watcher handle.
    /// </summary>
    /// <param name="nativeCharacteristic">The native <c>IGattCharacteristic1</c> proxy.</param>
    /// <param name="onValueChanged">Callback invoked with the new value.</param>
    public static async Task<IDisposable> WatchValueAsync(object nativeCharacteristic, Action<byte[]> onValueChanged)
    {
        // Signature: Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler)
        var method = nativeCharacteristic.GetType().GetMethod("WatchPropertiesAsync")
            ?? throw new InvalidOperationException("Native BlueZ characteristic has no WatchPropertiesAsync method.");

        // The handler parameter is Action<PropertyChanges>, where
        // PropertyChanges is a Tmds.DBus struct. Build a delegate of that exact
        // type via a generic adapter class.
        var propertyChangesType = method.GetParameters()[0].ParameterType.GetGenericArguments()[0];
        var handler = PropertyChangesHandlerFactory.Create(propertyChangesType, onValueChanged);

        var task = (Task?)method.Invoke(nativeCharacteristic, new object[] { handler });
        if (task == null)
        {
            throw new InvalidOperationException("WatchPropertiesAsync returned null.");
        }

        await task;

        var watcher = task.GetType().GetProperty("Result")?.GetValue(task) as IDisposable;
        return watcher ?? throw new InvalidOperationException("WatchPropertiesAsync did not return a watcher handle.");
    }

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
    public static async Task StartNotifyWithRetryAsync(object nativeCharacteristic)
    {
        var method = nativeCharacteristic.GetType().GetMethod("StartNotifyAsync")
            ?? throw new InvalidOperationException("Native BlueZ characteristic has no StartNotifyAsync method.");

        int attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                var task = (Task?)method.Invoke(nativeCharacteristic, null);
                if (task != null)
                {
                    await task;
                }

                if (attempt > 1)
                {
                    BluetoothLog.Success($"StartNotify succeeded after {attempt} attempts.");
                }
                return;
            }
            catch (Exception ex) when (IsBlueZInProgress(ex) && attempt < MaxStartNotifyAttempts)
            {
                BluetoothLog.Retry($"StartNotify busy (org.bluez.Error.InProgress), retrying ({attempt}/{MaxStartNotifyAttempts})...");
                await Task.Delay(StartNotifyRetryDelay);
            }
            catch (Exception ex) when (IsBlueZInProgress(ex))
            {
                BluetoothLog.Error($"StartNotify still busy after {MaxStartNotifyAttempts} attempts, giving up.");
                throw;
            }
            catch (Exception ex)
            {
                // Log any other errors (e.g., org.bluez.Error.Failed for
                // encrypt-notify without encryption) so they are visible.
                var errorName = ex.GetType().GetProperty("ErrorName")?.GetValue(ex) as string;
                BluetoothLog.Error($"StartNotify failed (attempt {attempt}): {errorName ?? ex.GetType().Name} - {ex.Message}");
                throw;
            }
        }
    }

    /// <summary>Stops BlueZ notifications.</summary>
    public static async Task StopNotifyAsync(object nativeCharacteristic)
    {
        var method = nativeCharacteristic.GetType().GetMethod("StopNotifyAsync");
        if (method == null) return;

        try
        {
            var task = (Task?)method.Invoke(nativeCharacteristic, null);
            if (task != null)
            {
                await task;
            }
        }
        catch
        {
            // Stopping notify is best-effort; the connection may already be gone.
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
            // Tmds.DBus.DBusException exposes ErrorName; match on it, but also
            // fall back to the message in case of wrapping.
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

    /// <summary>
    /// Creates a typed <c>Action&lt;PropertyChanges&gt;</c> delegate that
    /// forwards the "Value" entry to a managed <see cref="Action{Byte}"/>.
    /// </summary>
    private static class PropertyChangesHandlerFactory
    {
        public static Delegate Create(Type propertyChangesType, Action<byte[]> onValueChanged)
        {
            // Instantiate Handler<PropertyChanges> and bind its Invoke method to
            // an Action<PropertyChanges> delegate.
            var handlerType = typeof(Handler<>).MakeGenericType(propertyChangesType);
            var instance = Activator.CreateInstance(handlerType, onValueChanged)
                ?? throw new InvalidOperationException("Could not create PropertyChanges handler.");

            var invokeMethod = handlerType.GetMethod(nameof(Handler<object>.Invoke))
                ?? throw new InvalidOperationException("Handler is missing Invoke.");

            var actionType = typeof(Action<>).MakeGenericType(propertyChangesType);
            return Delegate.CreateDelegate(actionType, instance, invokeMethod);
        }

        /// <summary>
        /// Generic adapter whose <see cref="Invoke"/> matches the signature of
        /// <c>Action&lt;T&gt;</c> and forwards the "Value" entry of
        /// <c>Tmds.DBus.PropertyChanges</c> to a managed callback.
        /// </summary>
        private sealed class Handler<T>
        {
            private readonly Action<byte[]> _onValueChanged;

            public Handler(Action<byte[]> onValueChanged)
            {
                _onValueChanged = onValueChanged;
            }

            public void Invoke(T changes)
            {
                LogChangedKeys(changes);

                var value = ExtractValue(changes);
                if (value != null)
                {
                    _onValueChanged(value);
                }
            }

            /// <summary>
            /// Logs which properties changed in this <c>PropertiesChanged</c>
            /// signal, so it is visible whether the peripheral is actually
            /// notifying a given characteristic.
            /// </summary>
            private static void LogChangedKeys(T changes)
            {
                var changed = typeof(T).GetProperty("Changed")?.GetValue(changes);
                if (changed is not System.Collections.IEnumerable enumerable)
                {
                    return;
                }

                var keys = new List<string>();
                foreach (var item in enumerable)
                {
                    var key = item.GetType().GetProperty("Key")?.GetValue(item) as string;
                    keys.Add(key ?? "<null>");
                }

                BluetoothLog.Info($"PropertiesChanged: [{string.Join(", ", keys)}]");
            }

            /// <summary>
            /// Reads the "Value" entry out of <c>PropertyChanges.Changed</c>
            /// (a <c>KeyValuePair&lt;string, object&gt;[]</c>) via reflection.
            /// </summary>
            private static byte[]? ExtractValue(T changes)
            {
                var changed = typeof(T).GetProperty("Changed")?.GetValue(changes);
                if (changed is System.Collections.IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        var key = item.GetType().GetProperty("Key")?.GetValue(item) as string;
                        if (key != "Value") continue;

                        if (item.GetType().GetProperty("Value")?.GetValue(item) is byte[] value)
                        {
                            return value;
                        }
                    }
                }
                return null;
            }
        }
    }
}
