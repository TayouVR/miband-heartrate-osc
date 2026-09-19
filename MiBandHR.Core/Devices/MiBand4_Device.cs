using System.Security.Cryptography;
using System.Text;
using MiBandHR.Core.Bluetooth;

namespace MiBandHR.Core.Devices
{
    /// <summary>
    /// Mi Band 4 and Mi Band 5.
    ///
    /// These generations are bound to a Huami/Xiaomi account. The 16 byte
    /// authentication key has to be extracted from the vendor ecosystem once
    /// (e.g. via freemyband.com or the huami-token tool) and is then supplied
    /// by the user as a 32 character hex string.
    /// See docs/AUTHENTICATION.md for the full protocol description.
    /// </summary>
    public class MiBand4_Device : MiBandDeviceBase
    {
        private readonly byte[] _key;

        public MiBand4_Device(IBluetoothDevice device, string key) : base(device, DeviceModel.MIBAND_4)
        {
            _key = EncodeKey(key);
        }

        protected override byte[] GetAuthenticationKey() => _key;

        /// <summary>
        /// Accepts the key either as 32 character hex string (the format
        /// produced by freemyband.com / huami-token) or as arbitrary text,
        /// in which case the first 16 bytes of its SHA-256 hash are used.
        /// </summary>
        private static byte[] EncodeKey(string key)
        {
            if (key.Length == 32)
            {
                return Enumerable.Range(0, key.Length)
                    .Where(x => x % 2 == 0)
                    .Select(x => Convert.ToByte(key.Substring(x, 2), 16))
                    .ToArray();
            }
            return SHA256.HashData(Encoding.UTF8.GetBytes(key)).Take(16).ToArray();
        }
    }
}
