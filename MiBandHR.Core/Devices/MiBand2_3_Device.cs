using System.Security.Cryptography;
using MiBandHR.Core.Bluetooth;
using MiBandHR.Core.Configuration;

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
        /// Returns the authentication key for this band.
        ///
        /// On the first connection a fresh random key is generated and persisted
        /// (keyed by the device address) so that subsequent reconnects reuse the
        /// same key — the band stores the first key it accepts and rejects any
        /// different key afterwards. A factory reset makes the band forget it.
        /// </summary>
        protected override byte[] GetAuthenticationKey()
        {
            if (_key != null)
            {
                return _key;
            }

            var deviceConfig = ConfigurationManager.Instance.CurrentConfig.Device;
            if (deviceConfig.AuthKeys.TryGetValue(DeviceId, out var hexKey))
            {
                _key = Convert.FromHexString(hexKey);
            }
            else
            {
                _key = SHA256.HashData(Guid.NewGuid().ToByteArray()).Take(16).ToArray();
                deviceConfig.AuthKeys[DeviceId] = Convert.ToHexString(_key);
                ConfigurationManager.Instance.SaveConfiguration();
            }

            return _key;
        }

        /// <summary>
        /// Mi Band 2/3 pairing starts by sending the key to the band:
        /// <c>0x01 0x08 + 16 byte key</c>.
        /// </summary>
        protected override byte[] GetInitialAuthCommand()
        {
            return Combine(new byte[] { 0x01, 0x08 }, GetAuthenticationKey());
        }
    }
}
