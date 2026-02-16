using System.Diagnostics;
using System.Security.Cryptography;
using InTheHand.Bluetooth;

namespace MiBandHR.Core.Devices {
    public class MiBand4_Device : Device {
        const string AUTH_SRV_ID = "0000fee1-0000-1000-8000-00805f9b34fb";
        const string AUTH_CHAR_ID = "00000009-0000-3512-2118-0009af100700";

        static readonly BluetoothUuid HEARTRATE_SRV_ID = BluetoothUuid.FromShortId(0x180d);
        static readonly BluetoothUuid HEARTRATE_CHAR_ID = BluetoothUuid.FromShortId(0x2a39);
        static readonly BluetoothUuid HEARTRATE_NOTIFY_CHAR_ID = BluetoothUuid.FromShortId(0x2a37);

        byte[] _key;
        BluetoothDevice _connectedDevice;

        GattCharacteristic? _heartrateCharacteristic;
        GattCharacteristic? _heartrateNotifyCharacteristic;

        Thread? _keepHeartrateAliveThread;
        bool _continuous;

        public MiBand4_Device(BluetoothDevice d, string key) {
            _connectedDevice = d;
            DeviceId = d.Id;
            Name = d.Name;
            Model = DeviceModel.MIBAND_4;
            _key = EncodeKey(key);
        }

        byte[] EncodeKey(string key) {
            if (key.Length == 32) {
                return Enumerable.Range(0, key.Length)
                    .Where(x => x % 2 == 0)
                    .Select(x => Convert.ToByte(key.Substring(x, 2), 16))
                    .ToArray();
            }
            return SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)).Take(16).ToArray();
        }

        public override void Authenticate() {
            _ = Task.Run(async () => {
                try {
                    GattService service = await _connectedDevice.Gatt.GetPrimaryServiceAsync(BluetoothUuid.FromGuid(new Guid(AUTH_SRV_ID)));
                    if (service != null) {
                        GattCharacteristic characteristic = await service.GetCharacteristicAsync(BluetoothUuid.FromGuid(new Guid(AUTH_CHAR_ID)));
                        if (characteristic != null) {
                            characteristic.CharacteristicValueChanged += OnAuthenticateNotify;
                            await characteristic.StartNotificationsAsync();

                            using (var stream = new MemoryStream()) {
                                stream.Write(new byte[] { 0x01, 0x08 }, 0, 2);
                                stream.Write(_key, 0, _key.Length);
                                await BLE.Write(characteristic, stream.ToArray());
                            }
                        }
                    }
                } catch (Exception e) {
                    Console.WriteLine(e);
                }
            });
        }

        void OnAuthenticateNotify(object? sender, GattCharacteristicValueChangedEventArgs args) {
            var senderChar = (GattCharacteristic)sender!;
            byte[] value = args.Value;
            if (value.Length < 3) return;

            switch (value[1]) {
                case 0x01:
                    if (value[2] == 0x01) {
                        _ = BLE.Write(senderChar, new byte[] { 0x02, 0x08 });
                    }
                    break;
                case 0x02: {
                    byte[] number = value.Skip(3).ToArray();
                    using (var stream = new MemoryStream()) {
                        stream.Write(new byte[] { 0x03, 0x08 }, 0, 2);
                        byte[] encryptedNumber = EncryptAuthenticationNumber(number);
                        stream.Write(encryptedNumber, 0, encryptedNumber.Length);
                        _ = BLE.Write(senderChar, stream.ToArray());
                    }
                    break;
                }
                case 0x03:
                    if (value[2] == 0x01) {
                        Status = DeviceStatus.ONLINE_AUTH;
                    }
                    break;
            }
        }

        byte[] EncryptAuthenticationNumber(byte[] number) {
            using (Aes aes = Aes.Create()) {
                aes.Key = _key;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                using (ICryptoTransform encryptor = aes.CreateEncryptor()) {
                    return encryptor.TransformFinalBlock(number, 0, number.Length);
                }
            }
        }

        public override void Connect() {
            Disconnect();
            Status = DeviceStatus.ONLINE_UNAUTH;
            Authenticate();
        }

        public override void Disconnect() {
            StopHeartrateMonitor();
            if (_connectedDevice != null && _connectedDevice.Gatt.IsConnected) {
                _connectedDevice.Gatt.Disconnect();
            }
            Status = DeviceStatus.OFFLINE;
        }

        public override void Dispose() {
            Disconnect();
        }

        public override void StartHeartrateMonitor(bool continuous = false) {
            if (HeartrateMonitorStarted) return;
            _continuous = continuous;

            _ = Task.Run(async () => {
                try {
                    GattService service = await _connectedDevice.Gatt.GetPrimaryServiceAsync(HEARTRATE_SRV_ID);
                    if (service != null) {
                        _heartrateCharacteristic = await service.GetCharacteristicAsync(HEARTRATE_CHAR_ID);
                        _heartrateNotifyCharacteristic = await service.GetCharacteristicAsync(HEARTRATE_NOTIFY_CHAR_ID);

                        if (_heartrateNotifyCharacteristic != null) {
                            _heartrateNotifyCharacteristic.CharacteristicValueChanged += OnHeartrateNotify;
                            await _heartrateNotifyCharacteristic.StartNotificationsAsync();
                        }

                        if (_heartrateCharacteristic != null) {
                            await BLE.Write(_heartrateCharacteristic, new byte[] { 0x15, 0x02, 0x00 });
                            await BLE.Write(_heartrateCharacteristic, new byte[] { 0x15, 0x01, 0x00 });
                            await BLE.Write(_heartrateCharacteristic, new byte[] { 0x15, 0x01, 0x01 });

                            _keepHeartrateAliveThread = new Thread(RunHeartrateKeepAlive);
                            _keepHeartrateAliveThread.Start();
                            HeartrateMonitorStarted = true;
                        }
                    }
                } catch (Exception e) {
                    Console.WriteLine(e);
                }
            });
        }

        public override void StopHeartrateMonitor() {
            if (HeartrateMonitorStarted) {
                _ = Task.Run(async () => {
                    if (_heartrateCharacteristic != null) {
                        await BLE.Write(_heartrateCharacteristic, new byte[] { 0x15, 0x01, 0x00 });
                    }
                });
                HeartrateMonitorStarted = false;
            }
        }

        void OnHeartrateNotify(object? sender, GattCharacteristicValueChangedEventArgs args) {
            byte[] value = args.Value;
            if (value.Length == 2 && value[0] == 0) {
                Heartrate = value[1];
            }
        }

        void RunHeartrateKeepAlive() {
            try {
                while (HeartrateMonitorStarted) {
                    if (_heartrateCharacteristic != null) {
                        _ = BLE.Write(_heartrateCharacteristic, new byte[] { 0x16 });
                    }
                    Thread.Sleep(12000);
                }
            } catch (ThreadInterruptedException) {
                Debug.WriteLine("MiBand 4 Heartrate Keep Alive Thread Killed");
            }
        }
    }
}
