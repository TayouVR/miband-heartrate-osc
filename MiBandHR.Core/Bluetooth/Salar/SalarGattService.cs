using Salar.BluetoothLE.Core.Interfaces;

namespace MiBandHR.Core.Bluetooth.Salar;

/// <summary>
/// <see cref="IGattService"/> backed by a Salar.BluetoothLE <see cref="IBleService"/>.
/// </summary>
public sealed class SalarGattService : IGattService
{
    private readonly IBleService _service;

    public SalarGattService(IBleService service)
    {
        _service = service;
    }

    /// <summary>The wrapped Salar service.</summary>
    public IBleService NativeService => _service;

    public Guid Uuid => _service.Uuid;

    public async Task<IGattCharacteristic?> GetCharacteristicAsync(Guid uuid)
    {
        var characteristic = await _service.GetCharacteristicAsync(uuid);
        return characteristic == null ? null : new SalarGattCharacteristic(characteristic);
    }
}
