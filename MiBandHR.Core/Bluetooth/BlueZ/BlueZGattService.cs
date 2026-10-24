#if !WINDOWS
using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;

namespace MiBandHR.Core.Bluetooth.BlueZ;

/// <summary>
/// Linux (BlueZ) <see cref="IGattService"/> backed by a native
/// <c>IGattService1</c> D-Bus proxy.
/// </summary>
public sealed class BlueZGattService : IGattService {
    private readonly IGattService1 _nativeService;

    public BlueZGattService(IGattService1 nativeService, Guid uuid) {
        _nativeService = nativeService;
        Uuid = uuid;
    }

    public Guid Uuid { get; }

    public async Task<IGattCharacteristic?> GetCharacteristicAsync(Guid uuid) {
        var characteristic = await _nativeService.GetCharacteristicAsync(uuid.ToString());
        return characteristic == null ? null : new BlueZGattCharacteristic(characteristic, uuid);
    }
}
#endif
