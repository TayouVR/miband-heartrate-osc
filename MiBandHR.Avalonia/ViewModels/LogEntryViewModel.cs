using MiBandHR.Core.Bluetooth;

namespace MiBandHR.Avalonia.ViewModels;

/// <summary>
/// View-model wrapper around a <see cref="BluetoothLogEntry"/> that exposes
/// display-friendly properties (formatted text and a colour per severity).
/// </summary>
public sealed class LogEntryViewModel
{
    public LogEntryViewModel(BluetoothLogEntry entry)
    {
        Text = $"[{entry.Timestamp:HH:mm:ss}] {entry.Message}";
        Level = entry.Level;
    }

    public string Text { get; }

    public BluetoothLogLevel Level { get; }

    /// <summary>Foreground brush used to colour the entry by severity.</summary>
    public string Foreground => Level switch
    {
        BluetoothLogLevel.Success => "#4CAF50", // green
        BluetoothLogLevel.Retry => "#FFC107",   // amber
        BluetoothLogLevel.Error => "#F44336",   // red
        _ => "#B0B0B0"                          // grey for info
    };
}
