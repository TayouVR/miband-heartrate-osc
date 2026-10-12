using System.Reflection;
using InTheHand.Bluetooth;

namespace MiBandHR.Core.Bluetooth.InTheHand;

/// <summary>
/// Wraps an <see cref="InTheHand.Bluetooth.GattCharacteristic"/> into the
/// platform-neutral <see cref="IGattCharacteristic"/> abstraction.
///
/// On Windows and macOS this simply forwards to the InTheHand API.
/// On Linux (BlueZ) it takes over notification handling itself, because
/// InTheHand's Linux backend has two problems for the Mi Band use case:
///
/// <list type="bullet">
///   <item><description>BlueZ reports the Mi Band auth characteristic with the
///   flag <c>encrypt-notify</c> instead of plain <c>notify</c>, which
///   InTheHand does not map into its <c>Properties</c>, so its
///   <c>StartNotificationsAsync</c> throws
///   "Characteristic does not support Notify or Indicate".</description></item>
///   <item><description>Starting notifications needs the correct ordering
///   (subscribe to value changes first, then call BlueZ <c>StartNotify</c>)
///   and must tolerate the transient <c>org.bluez.Error.InProgress</c> race.
///   </description></item>
/// </list>
///
/// The Linux path extracts the native <c>IGattCharacteristic1</c> D-Bus proxy
/// (stored in InTheHand's private backing field) and drives BlueZ directly.
/// </summary>
public sealed class InTheHandGattCharacteristic : IGattCharacteristic
{
    private readonly GattCharacteristic _characteristic;

    // ---- Linux (BlueZ) specific state ----
    private object? _linuxNativeCharacteristic; // IGattCharacteristic1, typed as object to avoid a hard Linux.Bluetooth dependency on other platforms
    private IDisposable? _linuxPropertyWatcher;

    public InTheHandGattCharacteristic(GattCharacteristic characteristic)
    {
        _characteristic = characteristic;
    }

    public Guid Uuid => _characteristic.Uuid;

    public event EventHandler<GattCharacteristicValueChangedEventArgs>? CharacteristicValueChanged;

    /// <summary>
    /// Wires up the local value-changed event handler.
    /// On Linux the actual D-Bus subscription is set up in
    /// <see cref="StartNotificationsAsync"/> (right before starting notify),
    /// so this only subscribes to InTheHand's in-process event.
    /// </summary>
    public void SubscribeToValueChanges()
    {
        if (!OperatingSystem.IsLinux())
        {
            _characteristic.CharacteristicValueChanged += OnInTheHandValueChanged;
        }
        // On Linux the forwarding is wired up in StartNotificationsAsync.
    }

    private void OnInTheHandValueChanged(object? sender, global::InTheHand.Bluetooth.GattCharacteristicValueChangedEventArgs args)
    {
        CharacteristicValueChanged?.Invoke(this, new GattCharacteristicValueChangedEventArgs(args.Value));
    }

    public Task WriteValueWithResponseAsync(byte[] data)
    {
        return OperatingSystem.IsLinux()
            ? BlueZNativeInterop.WriteValueAsync(GetLinuxNativeCharacteristic(), data)
            : _characteristic.WriteValueWithResponseAsync(data);
    }

    public Task WriteValueWithoutResponseAsync(byte[] data)
    {
        return OperatingSystem.IsLinux()
            ? BlueZNativeInterop.WriteValueWithoutResponseAsync(GetLinuxNativeCharacteristic(), data)
            : _characteristic.WriteValueWithoutResponseAsync(data);
    }

    public Task StartNotificationsAsync()
    {
        return OperatingSystem.IsLinux()
            ? StartNotificationsOnLinuxAsync()
            : _characteristic.StartNotificationsAsync();
    }

    public Task StopNotificationsAsync()
    {
        return OperatingSystem.IsLinux()
            ? StopNotificationsOnLinuxAsync()
            : _characteristic.StopNotificationsAsync();
    }

    // ------------------------------------------------------------------
    // Linux (BlueZ) implementation
    // ------------------------------------------------------------------

    private async Task StartNotificationsOnLinuxAsync()
    {
        var native = GetLinuxNativeCharacteristic();

        // 1. Subscribe to Value property changes FIRST, so no notification is
        //    lost between enabling notify on the device and wiring the handler.
        if (_linuxPropertyWatcher == null)
        {
            _linuxPropertyWatcher = await LinuxBlueZInterop.WatchValueAsync(native, RaiseLinuxValueChanged);
        }

        // 2. Then ask BlueZ to start notifications. BlueZ writes the CCCD on
        //    our behalf. Tolerate the transient InProgress race with retries.
        await LinuxBlueZInterop.StartNotifyWithRetryAsync(native);
    }

    private async Task StopNotificationsOnLinuxAsync()
    {
        var native = GetLinuxNativeCharacteristic();

        try
        {
            await LinuxBlueZInterop.StopNotifyAsync(native);
        }
        finally
        {
            _linuxPropertyWatcher?.Dispose();
            _linuxPropertyWatcher = null;
        }
    }

    private void RaiseLinuxValueChanged(byte[] value)
    {
        CharacteristicValueChanged?.Invoke(this, new GattCharacteristicValueChangedEventArgs(value));
    }

    /// <summary>
    /// Gets the native <c>IGattCharacteristic1</c> D-Bus proxy that InTheHand
    /// stores in its private <c>_characteristic</c> field on Linux.
    /// Cached after the first lookup.
    /// </summary>
    private object GetLinuxNativeCharacteristic()
    {
        if (_linuxNativeCharacteristic != null)
        {
            return _linuxNativeCharacteristic;
        }

        var field = typeof(GattCharacteristic).GetField(
            "_characteristic", BindingFlags.NonPublic | BindingFlags.Instance);

        _linuxNativeCharacteristic = field?.GetValue(_characteristic)
            ?? throw new InvalidOperationException(
                "Could not access the native BlueZ characteristic on Linux.");

        return _linuxNativeCharacteristic;
    }
}
