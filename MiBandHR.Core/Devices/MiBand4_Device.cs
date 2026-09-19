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
    ///
    /// Unlike Mi Band 2/3 the key is NOT sent to the band during the
    /// handshake — the band already knows it. The host immediately asks for
    /// the random challenge with <c>0x02 0x00</c> and answers with
    /// <c>0x03 0x00 + AES_ECB(key, challenge)</c>.
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
        /// Mi Band 4/5 skips the key exchange: the initial command is a bare
        /// random number request, <c>0x02 0x00</c>.
        /// </summary>
        protected override byte[] GetInitialAuthCommand() => new byte[] { 0x02, 0x00 };

        /// <summary>
        /// The encrypted challenge is returned with the <c>0x03 0x00</c> prefix.
        /// </summary>
        protected override byte[] EncryptedNumberResponsePrefix => new byte[] { 0x03, 0x00 };

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
