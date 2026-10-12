using System.Security.Cryptography;
using MiBandHR.Core.Bluetooth;

namespace MiBandHR.Core.Devices;

/// <summary>
/// Base class shared by all Mi Band generations. It contains the protocol
/// implementation that is identical between the Mi Band 2/3 and Mi Band 4/5
/// (and newer) generations:
///
/// <list type="bullet">
///   <item><description>The challenge/response part of the authentication
///   handshake, see
///   <see href="https://leojrfs.github.io/writing/miband2-part1-auth/">Mi Band 2 Authentication by leojrfs</see>.
///   Full documentation lives in <c>docs/AUTHENTICATION.md</c>.</description></item>
///   <item><description>The heart rate monitoring session (start/stop/keep-alive).</description></item>
/// </list>
///
/// The generations differ in how the handshake is started:
/// <list type="bullet">
///   <item><description>Mi Band 2/3 sends a freshly generated key to the band
///   first (<c>0x01 0x08 + key</c>) and asks for the random challenge with
///   <c>0x02 0x08</c>.</description></item>
///   <item><description>Mi Band 4/5 is bound to a vendor account; the band
///   already knows the key, so the host immediately asks for the random
///   challenge with <c>0x02 0x00</c> and answers with
///   <c>0x03 0x00 + encrypted</c>.</description></item>
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

    // ------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------

    protected readonly IBluetoothDevice ConnectedDevice;

    private IGattCharacteristic? _heartRateControlPoint;
    private IGattCharacteristic? _heartRateMeasurement;

    private bool _continuous;

    private AuthState _authState = AuthState.None;

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

    /// <summary>
    /// The first command frame of the authentication handshake, i.e. what is
    /// sent to the band right after enabling notifications on the auth
    /// characteristic. Mi Band 2/3: <c>0x01 0x08 + key</c>;
    /// Mi Band 4/5: <c>0x02 0x00</c>.
    /// </summary>
    protected abstract byte[] GetInitialAuthCommand();

    /// <summary>
    /// Command used to request the random challenge after the initial command
    /// was acknowledged. Mi Band 2/3: <c>0x02 0x08</c>; Mi Band 4/5:
    /// <c>0x02 0x08</c> (after the initial <c>0x02 0x00</c>).
    /// </summary>
    protected virtual byte[] RandomNumberRequestCommand => new byte[] { 0x02, 0x08 };

    /// <summary>
    /// Command prefix used when returning the encrypted challenge.
    /// Mi Band 2/3: <c>0x03 0x08</c>; Mi Band 4/5: <c>0x03 0x00</c>.
    /// </summary>
    protected virtual byte[] EncryptedNumberResponsePrefix => new byte[] { 0x03, 0x08 };

    /// <summary>
    /// True when the handshake starts with a key-exchange frame (<c>0x01 0x08 + key</c>),
    /// i.e. Mi Band 2/3. Mi Band 4/5 skip the key exchange and the initial command
    /// is already the random-challenge request (<c>0x02 0x00</c>).
    /// </summary>
    protected virtual bool UsesKeyExchange => true;

    // ------------------------------------------------------------------
    // Connection lifecycle
    // ------------------------------------------------------------------

    public override void Connect()
    {
        BluetoothLog.Info($"Connecting to {Name}...");
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
            BluetoothLog.Info($"Disconnected from {Name}.");
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
    // Short summary (Mi Band 2/3 flavour):
    //   1. Host -> Band: [0x01, 0x08, <16 byte key>]        (send key)
    //   2. Band -> Host: [0x10, 0x01, 0x01]                  (key accepted)
    //      Host -> Band: [0x02, 0x08]                        (request random number)
    //   3. Band -> Host: [0x10, 0x02, 0x01, <16 byte number>] (random challenge)
    //      Host -> Band: [0x03, 0x08, AES_ECB(key, number)]  (encrypted response)
    //   4. Band -> Host: [0x10, 0x03, 0x01]                  (authentication complete)
    //
    // Mi Band 4/5 skips the key exchange: step 1 is [0x02, 0x00] and the
    // encrypted response is [0x03, 0x00, AES_ECB(key, number)].
    // ------------------------------------------------------------------

    public override void Authenticate()
    {
        _authState = AuthState.None;

        _ = Task.Run(async () =>
        {
            try
            {
                // Establish the GATT connection first. On some platforms
                // (Windows) service discovery connects implicitly, on others
                // (Linux/BlueZ) it does not — and the auth handshake would
                // fail with "Not Connected".
                await ConnectedDevice.ConnectAsync();

                BluetoothLog.Info("Starting authentication handshake...");
                var service = await ConnectedDevice.GetPrimaryServiceAsync(AuthServiceUuid);
                if (service == null)
                {
                    BluetoothLog.Error("Authentication failed: auth service (0xFEE1) not found.");
                    return;
                }

                var characteristic = await service.GetCharacteristicAsync(AuthCharacteristicUuid);
                if (characteristic == null)
                {
                    BluetoothLog.Error("Authentication failed: auth characteristic not found.");
                    return;
                }

                characteristic.SubscribeToValueChanges();
                characteristic.CharacteristicValueChanged += OnAuthenticateNotify;
                await characteristic.StartNotificationsAsync();
                BluetoothLog.Info("Notifications started on auth characteristic, sending initial command...");

                // Kick off the handshake (generation specific).
                var initialCommand = GetInitialAuthCommand();
                BluetoothLog.Info($"Initial auth command: [{string.Join(" ", initialCommand.Select(b => b.ToString("X2")))}]");
                _authState = UsesKeyExchange ? AuthState.SentKey : AuthState.RequestedChallenge;
                await Write(characteristic, initialCommand);
            }
            catch (Exception e)
            {
                _authState = AuthState.Failed;
                BluetoothLog.Error($"Authentication failed: {e.Message}");
            }
        });
    }

    private void OnAuthenticateNotify(object? sender, GattCharacteristicValueChangedEventArgs args)
    {
        if (sender is not IGattCharacteristic characteristic) return;

        byte[] value = args.Value;
        BluetoothLog.Info($"Auth notification received ({value.Length} bytes): [{string.Join(" ", value.Select(b => b.ToString("X2")))}]");

        // Responses are at least 3 bytes: [0x10, command, status].
        if (value.Length < 3) return;

        switch (value[1])
        {
            // Response to the initial command (0x01 on MB2/3, 0x02 on MB4/5).
            case 0x01:
                if (value[2] == 0x01 && _authState == AuthState.SentKey)
                {
                    BluetoothLog.Info("Key accepted, requesting random challenge...");
                    _authState = AuthState.RequestedChallenge;
                    _ = Write(characteristic, RandomNumberRequestCommand);
                }
                else if (value[2] != 0x01)
                {
                    BluetoothLog.Error("Authentication failed (key rejected).");
                    _authState = AuthState.Failed;
                }
                else
                {
                    // Duplicate "key accepted" notification; the challenge has
                    // already been requested, so don't request it again.
                    BluetoothLog.Info("Ignoring duplicate key-accepted notification.");
                }
                break;

            // Response to the random number request: encrypt the challenge and send it back.
            case 0x02:
            {
                if (value[2] != 0x01)
                {
                    BluetoothLog.Error("Authentication failed (random number request rejected).");
                    _authState = AuthState.Failed;
                    break;
                }

                if (_authState != AuthState.RequestedChallenge)
                {
                    // Duplicate or stale challenge (e.g. the band re-sent the
                    // same notification). Responding twice would make the band
                    // reject the handshake with [10 03 04].
                    BluetoothLog.Info("Ignoring duplicate/stale challenge notification.");
                    break;
                }

                BluetoothLog.Info("Challenge received, sending encrypted response...");
                byte[] number = value.Skip(3).ToArray();
                byte[] encryptedNumber = EncryptAuthenticationNumber(number);
                _authState = AuthState.SentEncryptedResponse;
                _ = Write(characteristic, Combine(EncryptedNumberResponsePrefix, encryptedNumber));
                break;
            }

            // Response to the encrypted number: success -> authenticated.
            case 0x03:
                if (value[2] == 0x01)
                {
                    // Only report/act on the first success. A duplicate
                    // [10 03 01] (BlueZ can re-deliver) must not re-trigger
                    // Status = ONLINE_AUTH, which would double-start monitoring.
                    if (_authState == AuthState.Authenticated)
                    {
                        break;
                    }

                    BluetoothLog.Success("Authentication successful.");
                    _authState = AuthState.Authenticated;
                    Status = DeviceStatus.ONLINE_AUTH;
                }
                else
                {
                    BluetoothLog.Error("Authentication failed (encrypted number rejected).");
                    _authState = AuthState.Failed;
                }
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

    public override void StartHeartrateMonitor(bool continuous = false)
    {
        if (HeartrateMonitorStarted) return;
        _continuous = continuous;

        BluetoothLog.Info(continuous
            ? "Starting continuous heart rate monitoring..."
            : "Starting single heart rate measurement...");

        _ = Task.Run(async () =>
        {
            try
            {
                var service = await ConnectedDevice.GetPrimaryServiceAsync(HeartRateServiceUuid);
                if (service == null)
                {
                    BluetoothLog.Error("Could not start monitoring: heart rate service (0x180D) not found.");
                    return;
                }

                _heartRateControlPoint = await service.GetCharacteristicAsync(HeartRateControlPointUuid);
                _heartRateMeasurement = await service.GetCharacteristicAsync(HeartRateMeasurementUuid);

                if (_heartRateMeasurement != null)
                {
                    _heartRateMeasurement.SubscribeToValueChanges();
                    _heartRateMeasurement.CharacteristicValueChanged += OnHeartRateNotify;
                    await _heartRateMeasurement.StartNotificationsAsync();

                    // Give the CCCD write (StartNotify) time to reach the band
                    // before starting the measurement. Without this the band
                    // measures (LED on) but never notifies HR values.
                    await Task.Delay(1000);
                }

                if (_heartRateControlPoint != null)
                {
                    if (continuous)
                    {
                        // Stop any ongoing one-shot measurement, then start the
                        // continuous measurement (mirrors Gadgetbridge). No
                        // keep-alive ping is needed — the heart rate notifications
                        // keep the link active.
                        await WriteWithResponse(_heartRateControlPoint, new byte[] { 0x15, 0x02, 0x00 });
                        await WriteWithResponse(_heartRateControlPoint, new byte[] { 0x15, 0x01, 0x01 });
                    }
                    else
                    {
                        // Single (one-shot) measurement.
                        await WriteWithResponse(_heartRateControlPoint, new byte[] { 0x15, 0x01, 0x00 });
                        await WriteWithResponse(_heartRateControlPoint, new byte[] { 0x15, 0x02, 0x01 });
                    }

                    HeartrateMonitorStarted = true;
                    BluetoothLog.Success("Heart rate monitoring started.");
                }
                else
                {
                    BluetoothLog.Error("Could not start monitoring: heart rate control point not found.");
                }
            }
            catch (Exception e)
            {
                BluetoothLog.Error($"Failed to start heart rate monitoring: {e.Message}");
            }
        });
    }

    public override void StopHeartrateMonitor()
    {
        if (!HeartrateMonitorStarted) return;

        BluetoothLog.Info("Stopping heart rate monitoring.");

        _ = Task.Run(async () =>
        {
            if (_heartRateControlPoint != null)
            {
                await WriteWithResponse(_heartRateControlPoint, new byte[] { 0x15, 0x01, 0x00 });
                await WriteWithResponse(_heartRateControlPoint, new byte[] { 0x15, 0x02, 0x00 });
            }
        });

        HeartrateMonitorStarted = false;
    }

    private void OnHeartRateNotify(object? sender, GattCharacteristicValueChangedEventArgs args)
    {
        byte[] value = args.Value;

        if (value.Length < 2) return;

        // Standard Heart Rate Measurement format (Bluetooth HRS): byte 0 is the
        // flags field, byte 1 is the 8-bit heart rate value.
        int heartrate = value[1];

        if (heartrate > 0)
        {
            Heartrate = (ushort)heartrate;
        }

        // A one-shot measurement delivers exactly one value.
        if (!_continuous)
        {
            StopHeartrateMonitor();
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Writes a payload to a characteristic <b>without response</b>, swallowing
    /// transport errors.
    ///
    /// The Huami auth (0x00000009-...) and sensor (0x00000001-...) characteristics
    /// only support write-without-response (BlueZ returns
    /// <c>org.bluez.Error.NotSupported</c> for a write-with-response). On Linux
    /// the backend omits the BlueZ "type" option so BlueZ auto-selects the write
    /// procedure from the characteristic's flags.
    /// </summary>
    protected static async Task Write(IGattCharacteristic characteristic, byte[] data)
    {
        try
        {
            BluetoothLog.Info($"Writing {data.Length} bytes to {characteristic.Uuid}: [{string.Join(" ", data.Select(b => b.ToString("X2")))}]");
            await characteristic.WriteValueWithoutResponseAsync(data);
        }
        catch (Exception e)
        {
            BluetoothLog.Error($"Unable to write on {characteristic.Uuid} - {e.Message}");
        }
    }

    /// <summary>
    /// Writes a payload to a characteristic <b>with response</b>, swallowing
    /// transport errors.
    ///
    /// The Heart Rate Control Point (0x2A39) is a Bluetooth SIG characteristic
    /// that supports write-with-response, so the start/stop/keep-alive command
    /// frames use this write type.
    /// </summary>
    protected static async Task WriteWithResponse(IGattCharacteristic characteristic, byte[] data)
    {
        try
        {
            BluetoothLog.Info($"Writing {data.Length} bytes to {characteristic.Uuid}: [{string.Join(" ", data.Select(b => b.ToString("X2")))}]");
            await characteristic.WriteValueWithResponseAsync(data);
        }
        catch (Exception e)
        {
            BluetoothLog.Error($"Unable to write on {characteristic.Uuid} - {e.Message}");
        }
    }

    protected static byte[] Combine(byte[] prefix, byte[] payload)
    {
        var result = new byte[prefix.Length + payload.Length];
        Buffer.BlockCopy(prefix, 0, result, 0, prefix.Length);
        Buffer.BlockCopy(payload, 0, result, prefix.Length, payload.Length);
        return result;
    }

    /// <summary>
    /// Progression of the authentication handshake. Used to make the
    /// notification handler idempotent, because some stacks (BlueZ) or the band
    /// itself can deliver the same notification more than once. Re-acting to a
    /// duplicate frame (e.g. re-requesting the challenge or re-sending the
    /// encrypted response) makes the band reject the handshake with
    /// <c>[10 03 04]</c>.
    /// </summary>
    private enum AuthState
    {
        None,
        SentKey,
        RequestedChallenge,
        SentEncryptedResponse,
        Authenticated,
        Failed
    }
}
