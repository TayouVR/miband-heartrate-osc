namespace MiBandHR.Core.Bluetooth;

/// <summary>
/// Severity of a <see cref="BluetoothLogEntry"/>.
/// </summary>
public enum BluetoothLogLevel
{
    Info,
    Retry,
    Success,
    Error
}

/// <summary>
/// A single log line emitted by the Bluetooth layer.
/// </summary>
public sealed class BluetoothLogEntry
{
    public BluetoothLogEntry(DateTime timestamp, BluetoothLogLevel level, string message)
    {
        Timestamp = timestamp;
        Level = level;
        Message = message;
    }

    public DateTime Timestamp { get; }
    public BluetoothLogLevel Level { get; }
    public string Message { get; }
}

/// <summary>
/// Lightweight, UI-agnostic logging bus for Bluetooth activity.
///
/// The Core BLE layer (scanning, connecting, notification setup, retry loops)
/// and the device protocol emit entries here; frontends subscribe to
/// <see cref="EntryLogged"/> and display them. This deliberately avoids any
/// dependency on a concrete logging framework or UI toolkit.
/// </summary>
public static class BluetoothLog
{
    /// <summary>Raised for every emitted log entry (may be on a background thread).</summary>
    public static event Action<BluetoothLogEntry>? EntryLogged;

    public static void Info(string message) => Emit(BluetoothLogLevel.Info, message);

    /// <summary>Used for retry/loop attempts, so UIs can highlight repetitive behaviour.</summary>
    public static void Retry(string message) => Emit(BluetoothLogLevel.Retry, message);

    public static void Success(string message) => Emit(BluetoothLogLevel.Success, message);

    public static void Error(string message) => Emit(BluetoothLogLevel.Error, message);

    private static void Emit(BluetoothLogLevel level, string message)
    {
        var entry = new BluetoothLogEntry(DateTime.Now, level, message);
        EntryLogged?.Invoke(entry);
        // Also mirror to the console so CLI users see it too.
        Console.WriteLine($"[bt:{level.ToString().ToLowerInvariant()}] {message}");
    }
}
