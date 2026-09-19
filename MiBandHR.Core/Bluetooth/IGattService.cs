namespace MiBandHR.Core.Bluetooth;

/// <summary>
/// Platform-neutral GATT service abstraction.
/// </summary>
public interface IGattService
{
    /// <summary>The 128-bit UUID of this service.</summary>
    Guid Uuid { get; }

    /// <summary>Resolves a single characteristic of this service by UUID.</summary>
    Task<IGattCharacteristic?> GetCharacteristicAsync(Guid uuid);
}
