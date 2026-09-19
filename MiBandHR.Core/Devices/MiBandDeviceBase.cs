using System.Security.Cryptography;
using MiBandHR.Core.Bluetooth;

namespace MiBandHR.Core.Devices;

/// <summary>
/// Base class shared by all Mi Band generations. It contains the protocol
/// implementation that is identical between the Mi Band 2/3 and Mi Band 4/5
/// (and newer) generations:
///
/// <list type="bullet">
///   <item><description>The three-step authentication handshake, see
///   <see href="https://leojrfs.github.io/writing/miband2-part1-auth/">Mi Band 2 Authentication by leojrfs</see>.
///   Full documentation lives in <c>docs/AUTHENTICATION.md</c>.</description></item>
///   <item><description>The heart rate monitoring session (start/stop/keep-alive).</description></item>
/// </list>
///
/// Concrete devices only provide the authentication key via
/// <see cref="GetAuthenticationKey"/>:
/// <list type="bullet">
///   <item><description>Mi Band 2/3 generates a random key and pairs with the band.</description></item>
///   <item><description>Mi Band 4/5 requires the key extracted from the vendor app
///   (e.g. via freemyband.com or huami-token).</description></item>
/// </list>
/// </summary>
public abstract class MiBandDeviceBase : Device
{
    // ------------------------------------------------------------------
    // GATT UUIDs
    // ------------------------------------------------------------------

    /// <summary>Huami/Xiaomi authentication service.</summary>
    protected static readonly Guid AuthServiceUuid = new("0000fee1-0000-1000-8000-00805f9b34fb");

    /// <summary>Authentication characteristic inside <see cref="AuthServiceUuid"/>.</summary>
    protected static readonly Guid AuthCharacteristicUuid = new("00000009-0000-3512-2118-0009af100700");

    /// <summary>Standard Bluetooth SIG Heart Rate service (0x180D).</summary>
    protected static readonly Guid HeartRateServiceUuid = new("0000180d-0000-1000-8000-00805f9b34fb");

    /// <summary>Heart Rate Control Point characteristic (0x2A39) – used to start/stop measurements.</summary>
    protected static readonly Guid HeartRateControlPointUuid = new("00002a39-0000-1000-8000-00805f9b34fb");

    /// <summary>Heart Rate Measurement characteristic (0x2A37) – notifies the measured values.</summary>
    protected static readonly Guid HeartRateMeasurementUuid = new("00002a37-0000-1000-8000-00805f9b34fb");

    /// <summary>Huami/Xiaomi sensor service (only needed on Mi Band 2/3 to enable the sensor).</summary>
    protected static readonly Guid SensorServiceUuid = new("0000fee0-0000-1000-8000-00805f9b34fb");

    /// <summary>Sensor control characteristic inside <see cref="SensorServiceUuid"/>.</summary>
    protected static readonly Guid SensorCharacteristicUuid = new("00000001-0000-3512-2118-0009af100700");

    // ------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------

    protected readonly IBluetoothDevice ConnectedDevice;

    private IGattCharacteristic? _heartRateControlPoint;
    private IGattCharacteristic? _heartRateMeasurement;

    private CancellationTokenSource? _keepAliveCts;
    private Task? _keepAliveTask;

    protected MiBandDeviceBase(IBluetoothDevice device, DeviceModel model)
    {
        ConnectedDevice = device;
        DeviceId = device.Id;
        Name = device.Name;
        Model = model;
    }

    /// <summary>
    /// Returns the 16 byte key used for the AES-128/ECB authentication handshake.
    /// </summary>
    protected abstract byte[] GetAuthenticationKey();

    // ------------------------------------------------------------------
    // Connection lifecycle
    // ------------------------------------------------------------------

    public override void Connect()
    {
        Disconnect();
        Status = DeviceStatus.ONLINE_UNAUTH;
        Authenticate();
    }

    public override void Disconnect()
    {
        StopHeartrateMonitor();
        if (ConnectedDevice.IsConnected)
        {
            ConnectedDevice.Disconnect();
        }
        Status = DeviceStatus.OFFLINE;
    }

    public override void Dispose()
    {
        Disconnect();
    }

    // ------------------------------------------------------------------
    // Authentication
    //
    // The handshake is documented extensively in docs/AUTHENTICATION.md.
    // Short summary:
    //   1. Host -> Band: [0x01, 0x08, <16 byte key>]        (send key)
    //   2. Band -> Host: [0x10, 0x01, 0x01]                  (key accepted)
    //      Host -> Band: [0x02, 0x08]                        (request random number)
    //   3. Band -> Host: [0x10, 0x02, 0x01, <16 byte number>] (random challenge)
    //      Host -> Band: [0x03, 0x08, AES_ECB(key, number)]  (encrypted response)
    //   4. Band -> Host: [0x10, 0x03, 0x01]                  (authentication complete)
    // ------------------------------------------------------------------

    public override void Authenticate()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var service = await ConnectedDevice.GetPrimaryServiceAsync(AuthServiceUuid);
                if (service == null) return;

                var characteristic = await service.GetCharacteristicAsync(AuthCharacteristicUuid);
                if (characteristic == null) return;

                characteristic.CharacteristicValueChanged += OnAuthenticateNotify;
                await characteristic.StartNotificationsAsync();

                // Step 1: send the authentication key to the band.
                await Write(characteristic, Combine(new byte[] { 0x01, 0x08 }, GetAuthenticationKey()));
            }
            catch (Exception e)
            {
                Console.WriteLine($"Authentication failed: {e.Message}");
            }
        });
    }

    private void OnAuthenticateNotify(object? sender, GattCharacteristicValueChangedEventArgs args)
    {
        if (sender is not IGattCharacteristic characteristic) return;

        byte[] value = args.Value;
        // Responses are at least 3 bytes: [0x10, command, status].
        if (value.Length < 3) return;

        switch (value[1])
        {
            // Response to "send key" (0x01): key accepted -> request random number.
            case 0x01 when value[2] == 0x01:
                _ = Write(characteristic, new byte[] { 0x02, 0x08 });
                break;

            // Response to "request random number" (0x02): encrypt the challenge and send it back.
            case 0x02:
            {
                byte[] number = value.Skip(3).ToArray();
                byte[] encryptedNumber = EncryptAuthenticationNumber(number);
                _ = Write(characteristic, Combine(new byte[] { 0x03, 0x08 }, encryptedNumber));
                break;
            }

            // Response to "send encrypted number" (0x03): success -> authenticated.
            case 0x03 when value[2] == 0x01:
                Status = DeviceStatus.ONLINE_AUTH;
                break;
        }
    }

    /// <summary>
    /// Encrypts the 16 byte random challenge with AES-128/ECB (no padding)
    /// using the device authentication key.
    /// </summary>
    private byte[] EncryptAuthenticationNumber(byte[] number)
    {
        using Aes aes = Aes.Create();
        aes.Key = GetAuthenticationKey();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using ICryptoTransform encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(number, 0, number.Length);
    }

    // ------------------------------------------------------------------
    // Heart rate monitoring
    // ------------------------------------------------------------------

    /// <summary>
    /// Hook allowing Mi Band 2/3 to enable the raw sensor before measurements.
    /// Default implementation does nothing (Mi Band 4+ does not need it).
    /// </summary>
    protected virtual Task PrepareHeartRateSensorAsync() => Task.CompletedTask;

    public override void StartHeartrateMonitor(bool continuous = false)
    {
        if (HeartrateMonitorStarted) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await PrepareHeartRateSensorAsync();

                var service = await ConnectedDevice.GetPrimaryServiceAsync(HeartRateServiceUuid);
                if (service == null) return;

                _heartRateControlPoint = await service.GetCharacteristicAsync(HeartRateControlPointUuid);
                _heartRateMeasurement = await service.GetCharacteristicAsync(HeartRateMeasurementUuid);

                if (_heartRateMeasurement != null)
                {
                    _heartRateMeasurement.CharacteristicValueChanged += OnHeartRateNotify;
                    await _heartRateMeasurement.StartNotificationsAsync();
                }

                if (_heartRateControlPoint != null)
                {
                    // Stop one-shot measurement, stop continuous measurement,
                    // then start a continuous measurement.
                    await Write(_heartRateControlPoint, new byte[] { 0x15, 0x02, 0x00 });
                    await Write(_heartRateControlPoint, new byte[] { 0x15, 0x01, 0x00 });
                    await Write(_heartRateControlPoint, new byte[] { 0x15, 0x01, 0x01 });

                    _keepAliveCts = new CancellationTokenSource();
                    _keepAliveTask = RunHeartRateKeepAliveAsync(_keepAliveCts.Token);
                    HeartrateMonitorStarted = true;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        });
    }

    public override void StopHeartrateMonitor()
    {
        if (!HeartrateMonitorStarted) return;

        _keepAliveCts?.Cancel();
        _keepAliveCts = null;
        _keepAliveTask = null;

        _ = Task.Run(async () =>
        {
            if (_heartRateControlPoint != null)
            {
                await Write(_heartRateControlPoint, new byte[] { 0x15, 0x01, 0x00 });
            }
        });

        HeartrateMonitorStarted = false;
    }

    private void OnHeartRateNotify(object? sender, GattCharacteristicValueChangedEventArgs args)
    {
        byte[] value = args.Value;
        // The first byte is a flags field; 0x00 means the heart rate is
        // encoded as a single byte in value[1].
        if (value.Length == 2 && value[0] == 0)
        {
            Heartrate = value[1];
        }
    }

    /// <summary>
    /// The band stops the continuous measurement after ~15 s without activity,
    /// so a ping (0x16) is sent every 12 s to keep the session alive.
    /// </summary>
    private async Task RunHeartRateKeepAliveAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_heartRateControlPoint != null)
                {
                    await Write(_heartRateControlPoint, new byte[] { 0x16 });
                }
                await Task.Delay(TimeSpan.FromSeconds(12), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on StopHeartrateMonitor().
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>Writes a payload to a characteristic, swallowing transport errors.</summary>
    protected static async Task Write(IGattCharacteristic characteristic, byte[] data)
    {
        try
        {
            await characteristic.WriteValueWithResponseAsync(data);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Unable to write on {characteristic.Uuid} - {e.Message}");
        }
    }

    protected static byte[] Combine(byte[] prefix, byte[] payload)
    {
        var result = new byte[prefix.Length + payload.Length];
        Buffer.BlockCopy(prefix, 0, result, 0, prefix.Length);
        Buffer.BlockCopy(payload, 0, result, prefix.Length, payload.Length);
        return result;
    }
}
