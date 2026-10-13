namespace MiBandHR.Core.Bluetooth;

/// <summary>
/// Platform-neutral representation of a BLE peripheral.
/// </summary>
public interface IBluetoothDevice
{
    /// <summary>Platform specific identifier of the device (MAC address on Linux, GUID-ish id on Windows).</summary>
    string Id { get; }

    /// <summary>Advertised (or cached) name of the device.</summary>
    string Name { get; }

    /// <summary>True while a GATT connection to the device is established.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Establishes a GATT connection to the device (no-op when already connected).
    /// Must be called before resolving services/characteristics on platforms
    /// that do not connect implicitly (e.g. Linux/BlueZ).
    /// </summary>
    Task ConnectAsync();

    /// <summary>Resolves a primary GATT service by UUID (connecting first if needed).</summary>
    Task<IGattService?> GetPrimaryServiceAsync(Guid uuid);

    /// <summary>Resolves all primary GATT services of the device.</summary>
    Task<IReadOnlyList<IGattService>> GetPrimaryServicesAsync();

    /// <summary>Terminates the GATT connection.</summary>
    void Disconnect();
    
    /// <summary>Raised when the BLE link drops unexpectedly (not on an explicit <see cref="Disconnect"/>).</summary>
    event EventHandler? Disconnected;
}
