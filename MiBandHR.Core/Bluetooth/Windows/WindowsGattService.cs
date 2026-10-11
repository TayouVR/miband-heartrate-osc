#if WINDOWS

using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace MiBandHR.Core.Bluetooth.Windows;

/// <summary>
/// <see cref="IGattService"/> backed by a WinRT <see cref="GattDeviceService"/>.
/// </summary>
public sealed class WindowsGattService : IGattService
{
    private readonly GattDeviceService _service;

    public WindowsGattService(GattDeviceService service)
    {
        _service = service;
    }

    public Guid Uuid => _service.Uuid;

    public async Task<IGattCharacteristic?> GetCharacteristicAsync(Guid uuid)
    {
        var result = await _service.GetCharacteristicsForUuidAsync(uuid);
        if (result.Status != GattCommunicationStatus.Success || result.Characteristics.Count == 0)
        {
            return null;
        }

        return new WindowsGattCharacteristic(result.Characteristics[0]);
    }
}

#endif