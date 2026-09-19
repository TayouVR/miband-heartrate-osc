using System.Security.Cryptography;
using MiBandHR.Core.Bluetooth;

namespace MiBandHR.Core.Devices
{
    /// <summary>
    /// Mi Band 2 and Mi Band 3.
    ///
    /// These generations do not require a vendor account: on the very first
    /// connection the host sends a random 16 byte key and the band is
    /// "paired" with it (the user has to confirm by touching the band's button).
    /// See docs/AUTHENTICATION.md for the full protocol description.
    /// </summary>
    public class MiBand2_3_Device : MiBandDeviceBase
    {
        private byte[]? _key;

        public MiBand2_3_Device(IBluetoothDevice device) : base(device, DeviceModel.MIBAND_2_3)
        {
        }

        /// <summary>
        /// Lazily generates a fresh random key for the connection.
        /// The band accepts the first key it sees after a factory reset.
        /// </summary>
        protected override byte[] GetAuthenticationKey()
        {
            return _key ??= SHA256.HashData(Guid.NewGuid().ToByteArray()).Take(16).ToArray();
        }

        /// <summary>
        /// Mi Band 2/3 pairing starts by sending the key to the band:
        /// <c>0x01 0x08 + 16 byte key</c>.
        /// </summary>
        protected override byte[] GetInitialAuthCommand()
        {
            return Combine(new byte[] { 0x01, 0x08 }, GetAuthenticationKey());
        }

        /// <summary>
        /// Mi Band 2/3 needs the raw sensor to be enabled explicitly via the
        /// Huami sensor service before heart rate measurements can start.
        /// Returns the sensor control characteristic so the base class can
        /// send the trigger frame afterwards.
        /// </summary>
        protected override async Task<IGattCharacteristic?> PrepareHeartRateSensorAsync()
        {
            var sensorService = await ConnectedDevice.GetPrimaryServiceAsync(SensorServiceUuid);
            if (sensorService == null) return null;

            var characteristic = await sensorService.GetCharacteristicAsync(SensorCharacteristicUuid);
            if (characteristic != null)
            {
                await Write(characteristic, new byte[] { 0x01, 0x03, 0x19 });
            }

            return characteristic;
        }
    }
}
