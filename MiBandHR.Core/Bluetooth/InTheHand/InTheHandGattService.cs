using InTheHand.Bluetooth;

namespace MiBandHR.Core.Bluetooth.InTheHand;

/// <summary>
/// Wraps an <see cref="InTheHand.Bluetooth.GattService"/> into the
/// platform-neutral <see cref="IGattService"/> abstraction.
/// </summary>
public sealed class InTheHandGattService : IGattService {
    private readonly GattService _service;

    public InTheHandGattService(GattService service) {
        _service = service;
    }

    public Guid Uuid => _service.Uuid;

    public async Task<IGattCharacteristic?> GetCharacteristicAsync(Guid uuid) {
        var characteristic = await _service.GetCharacteristicAsync(BluetoothUuid.FromGuid(uuid));
        return characteristic == null ? null : new InTheHandGattCharacteristic(characteristic);
    }
}
