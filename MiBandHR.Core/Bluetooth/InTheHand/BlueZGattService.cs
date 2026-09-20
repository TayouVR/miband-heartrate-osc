namespace MiBandHR.Core.Bluetooth.InTheHand;

/// <summary>
/// Linux (BlueZ) <see cref="IGattService"/> backed by a native
/// <c>IGattService1</c> D-Bus proxy.
/// </summary>
public sealed class BlueZGattService : IGattService
{
    private readonly object _nativeService;
    private Guid? _uuid;

    public BlueZGattService(object nativeService)
    {
        _nativeService = nativeService;
    }

    public Guid Uuid
    {
        get
        {
            if (_uuid == null)
            {
                var uuidString = BlueZNativeInterop.GetServiceUuidAsync(_nativeService).GetAwaiter().GetResult();
                _uuid = Guid.Parse(uuidString);
            }
            return _uuid.Value;
        }
    }

    public async Task<IGattCharacteristic?> GetCharacteristicAsync(Guid uuid)
    {
        var characteristic = await BlueZNativeInterop.GetCharacteristicAsync(_nativeService, uuid.ToString());
        return characteristic == null ? null : new BlueZGattCharacteristic(characteristic);
    }
}
