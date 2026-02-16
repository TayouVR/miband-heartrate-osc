using System.Diagnostics;
using System.Security.Cryptography;
using InTheHand.Bluetooth;

namespace MiBandHR.Core.Devices {
    public class MiBand2_3_Device : Device {
        const string AUTH_SRV_ID = "0000fee1-0000-1000-8000-00805f9b34fb";
        const string AUTH_CHAR_ID = "00000009-0000-3512-2118-0009af100700";

        static readonly BluetoothUuid HEARTRATE_SRV_ID = BluetoothUuid.FromShortId(0x180d);             // "0000180d-0000-1000-8000-00805f9b34fb";
        static readonly BluetoothUuid HEARTRATE_CHAR_ID = BluetoothUuid.FromShortId(0x2a39);            // "00002a39-0000-1000-8000-00805f9b34fb";
        static readonly BluetoothUuid HEARTRATE_NOTIFY_CHAR_ID = BluetoothUuid.FromShortId(0x2a37);     // "00002a37-0000-1000-8000-00805f9b34fb";

        const string SENSOR_SRV_ID = "0000fee0-0000-1000-8000-00805f9b34fb";
        const string SENSOR_CHAR_ID = "00000001-0000-3512-2118-0009af100700";

        byte[]? _key;
        BluetoothDevice _connectedDevice;

        GattService? _heartrateService;
        GattCharacteristic? _heartrateCharacteristic;
        GattCharacteristic? _heartrateNotifyCharacteristic;
        GattService? _sensorService;

        Thread? _keepHeartrateAliveThread;
        bool _continuous;

        public MiBand2_3_Device(BluetoothDevice d) {
            _connectedDevice = d;
            DeviceId = d.Id;
            Name = d.Name;
            Model = DeviceModel.MIBAND_2_3;
        }

        public override void Authenticate() {
            _ = Task.Run(async () => {
                GattService service = await _connectedDevice.Gatt.GetPrimaryServiceAsync(BluetoothUuid.FromGuid(new Guid(AUTH_SRV_ID)));
                if (service != null) {
                    GattCharacteristic characteristic = await service.GetCharacteristicAsync(BluetoothUuid.FromGuid(new Guid(AUTH_CHAR_ID)));
                    if (characteristic != null) {
                        characteristic.CharacteristicValueChanged += OnAuthenticateNotify;
                        await characteristic.StartNotificationsAsync();

                        _key = SHA256.HashData(Guid.NewGuid().ToByteArray()).Take(16).ToArray();

                        using (var stream = new MemoryStream()) {
                            stream.Write(new byte[] { 0x01, 0x08 }, 0, 2);
                            stream.Write(_key, 0, _key.Length);
                            await BLE.Write(characteristic, stream.ToArray());
                        }
                    }
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
                aes.Key = _key!;
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
                    _sensorService = await _connectedDevice.Gatt.GetPrimaryServiceAsync(BluetoothUuid.FromGuid(new Guid(SENSOR_SRV_ID)));
                    if (_sensorService != null) {
                        var characteristic = await _sensorService.GetCharacteristicAsync(BluetoothUuid.FromGuid(new Guid(SENSOR_CHAR_ID)));
                        if (characteristic != null) {
                            await BLE.Write(characteristic, new byte[] { 0x01, 0x03, 0x19 });
                        }
                    }

                    _heartrateService = await _connectedDevice.Gatt.GetPrimaryServiceAsync(HEARTRATE_SRV_ID);
                    if (_heartrateService != null) {
                        _heartrateCharacteristic = await _heartrateService.GetCharacteristicAsync(HEARTRATE_CHAR_ID);
                        _heartrateNotifyCharacteristic = await _heartrateService.GetCharacteristicAsync(HEARTRATE_NOTIFY_CHAR_ID);

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
                Debug.WriteLine("MiBand 2/3 Heartrate Keep Alive Thread Killed");
            }
        }
    }
}
