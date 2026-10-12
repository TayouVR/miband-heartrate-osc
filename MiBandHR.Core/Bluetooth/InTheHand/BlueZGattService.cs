namespace MiBandHR.Core.Bluetooth.InTheHand;

/// <summary>
/// Linux (BlueZ) <see cref="IGattService"/> backed by a native
/// <c>IGattService1</c> D-Bus proxy.
/// </summary>
public sealed class BlueZGattService : IGattService
{
    private readonly object _nativeService;

    public BlueZGattService(object nativeService, Guid uuid)
    {
        _nativeService = nativeService;
        Uuid = uuid;
    }

    public Guid Uuid { get; }

    public async Task<IGattCharacteristic?> GetCharacteristicAsync(Guid uuid)
    {
        var characteristic = await BlueZNativeInterop.GetCharacteristicAsync(_nativeService, uuid.ToString());
        return characteristic == null ? null : new BlueZGattCharacteristic(characteristic, uuid);
    }
}
