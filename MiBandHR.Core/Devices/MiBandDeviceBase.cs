using System.Security.Cryptography;
using System.Threading.Channels;
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
public abstract class MiBandDeviceBase : Device {
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

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _sync = new();
    private IGattCharacteristic? _authCharacteristic;
    private EventHandler<GattCharacteristicValueChangedEventArgs>? _authNotifyHandler;
    private Task? _authNotificationTask;
    private TaskCompletionSource<bool>? _authCompletion;
    private IGattCharacteristic? _heartRateControlPoint;
    private IGattCharacteristic? _heartRateMeasurement;
    private EventHandler<GattCharacteristicValueChangedEventArgs>? _heartRateNotifyHandler;
    private CancellationTokenSource? _sessionCts;
    private CancellationTokenSource? _monitorStartCts;
    private CancellationTokenSource? _keepAliveCts;
    private Task? _connectTask;
    private Task? _monitorStartTask;
    private Task? _keepAliveTask;
    private Task? _monitorStopTask;
    private TaskCompletionSource? _disconnectCompletion;
    private Task? _unexpectedDisconnectTask;
    private Task? _disposeTask;
    private long _sessionGeneration;
    private long _monitorGeneration;
    private bool _continuous;
    private bool _disposed;
    private AuthState _authState = AuthState.None;

    protected virtual TimeSpan AuthenticationTimeout => TimeSpan.FromSeconds(30);
    protected virtual TimeSpan HeartrateStartupDelay => TimeSpan.FromSeconds(1);
    protected virtual TimeSpan HeartRateKeepAliveInterval => TimeSpan.FromSeconds(5);

    protected MiBandDeviceBase(IBluetoothDevice device, DeviceModel model) {
        ConnectedDevice = device;
        DeviceId = device.Id;
        Name = device.Name;
        Model = model;
        ConnectedDevice.Disconnected += OnDeviceDisconnected;
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
    protected virtual byte[] RandomNumberRequestCommand => [0x02, 0x08];

    /// <summary>
    /// Command prefix used when returning the encrypted challenge.
    /// Mi Band 2/3: <c>0x03 0x08</c>; Mi Band 4/5: <c>0x03 0x00</c>.
    /// </summary>
    protected virtual byte[] EncryptedNumberResponsePrefix => [0x03, 0x08];

    /// <summary>
    /// True when the handshake starts with a key-exchange frame (<c>0x01 0x08 + key</c>),
    /// i.e. Mi Band 2/3. Mi Band 4/5 skip the key exchange and the initial command
    /// is already the random-challenge request (<c>0x02 0x00</c>).
    /// </summary>
    protected virtual bool UsesKeyExchange => true;

    // ------------------------------------------------------------------
    // Connection lifecycle
    // ------------------------------------------------------------------

    public override Task ConnectAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        Task? connectTask;
        Task? disconnectTask;
        CancellationTokenSource? previousSession;
        Task? priorMonitorStart;
        Task? priorMonitorStop;
        lock (_sync) {
            ThrowIfDisposed();
            if (_disconnectCompletion != null) {
                disconnectTask = _disconnectCompletion.Task;
                connectTask = null;
                previousSession = null;
            } else if (_connectTask is { IsCompleted: false } activeConnect) {
                return cancellationToken.CanBeCanceled ? activeConnect.WaitAsync(cancellationToken) : activeConnect;
            } else if (Status == DeviceStatus.OnlineAuth && ConnectedDevice.IsConnected) {
                return Task.CompletedTask;
            } else {
                disconnectTask = null;
                previousSession = _sessionCts;
                priorMonitorStart = _monitorStartTask;
                priorMonitorStop = _monitorStopTask;
                var sessionCts = new CancellationTokenSource();
                var generation = ++_sessionGeneration;
                _sessionCts = sessionCts;
                connectTask = ConnectSessionAsync(generation, sessionCts, cancellationToken, priorMonitorStart,
                    priorMonitorStop);
                _connectTask = connectTask;
            }
        }

        if (disconnectTask != null) return ConnectAfterDisconnectAsync(disconnectTask, cancellationToken);
        CancelSafely(previousSession);
        previousSession?.Dispose();
        return connectTask!;
    }

    private async Task ConnectAfterDisconnectAsync(Task disconnectTask, CancellationToken cancellationToken) {
        await disconnectTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        await ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    public override Task AuthenticateAsync(CancellationToken cancellationToken = default)
        => ConnectAsync(cancellationToken);

    private async Task ConnectSessionAsync(long generation, CancellationTokenSource sessionCts,
        CancellationToken callerToken, Task? priorMonitorStart, Task? priorMonitorStop) {
        await Task.Yield();
        using var callerRegistration = callerToken.Register(sessionCts.Cancel);
        var cancellationToken = sessionCts.Token;
        var acquired = false;
        var connected = false;
        try {
            await ObserveCleanupTaskAsync(priorMonitorStart).ConfigureAwait(false);
            await ObserveCleanupTaskAsync(priorMonitorStop).ConfigureAwait(false);
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            EnsureCurrentSession(generation, cancellationToken);
            await CleanupSessionResourcesAsync(sendStop: true, disconnectAdapter: true, throwOnFailure: false)
                .ConfigureAwait(false);
            EnsureCurrentSession(generation, cancellationToken);

            Heartrate = 0;
            BluetoothLog.Info($"Connecting to {Name}...");
            Status = DeviceStatus.Connecting;
            await ConnectedDevice.ConnectAsync(cancellationToken).ConfigureAwait(false);
            EnsureCurrentSession(generation, cancellationToken);
            await AuthenticateCoreAsync(generation, cancellationToken).ConfigureAwait(false);
            EnsureCurrentSession(generation, cancellationToken);
            Status = DeviceStatus.OnlineAuth;
            connected = true;
        } catch (Exception e) {
            try {
                if (!acquired) {
                    await _lifecycleGate.WaitAsync().ConfigureAwait(false);
                    acquired = true;
                }

                await CleanupSessionResourcesAsync(sendStop: false, disconnectAdapter: true, throwOnFailure: false)
                    .ConfigureAwait(false);
            } catch (Exception cleanupException) {
                BluetoothLog.Error($"Connection cleanup failed: {cleanupException.Message}");
            }

            Heartrate = 0;
            SetOfflineIfCurrent(generation);
            if (e is not OperationCanceledException) {
                BluetoothLog.Error($"Authentication failed: {e.Message}");
            }

            throw;
        } finally {
            if (acquired) _lifecycleGate.Release();
            if (!connected) {
                var ownsSession = false;
                lock (_sync) {
                    if (ReferenceEquals(_sessionCts, sessionCts)) {
                        _sessionCts = null;
                        ownsSession = true;
                    }
                }

                if (ownsSession) sessionCts.Dispose();
            }
        }
    }

    public override Task DisconnectAsync(CancellationToken cancellationToken = default) {
        TaskCompletionSource completion;
        CancellationTokenSource? sessionCts;
        CancellationTokenSource? monitorStartCts;
        CancellationTokenSource? keepAliveCts;
        Task? connectTask;
        Task? monitorStartTask;
        Task? monitorStopTask;

        lock (_sync) {
            if (_disconnectCompletion != null) return _disconnectCompletion.Task;

            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _disconnectCompletion = completion;
            ++_sessionGeneration;
            sessionCts = _sessionCts;
            _sessionCts = null;
            monitorStartCts = _monitorStartCts;
            keepAliveCts = _keepAliveCts;
            connectTask = _connectTask;
            monitorStartTask = _monitorStartTask;
            monitorStopTask = _monitorStopTask;
        }

        CancelSafely(sessionCts);
        CancelSafely(monitorStartCts);
        CancelSafely(keepAliveCts);
        _ = CompleteDisconnectAsync(completion, sessionCts, connectTask, monitorStartTask, monitorStopTask);
        return completion.Task;
    }

    private async Task CompleteDisconnectAsync(TaskCompletionSource completion, CancellationTokenSource? sessionCts,
        Task? connectTask, Task? monitorStartTask, Task? monitorStopTask) {
        await Task.Yield();
        Exception? failure = null;
        try {
            await ObserveCleanupTaskAsync(connectTask).ConfigureAwait(false);
            await ObserveCleanupTaskAsync(monitorStartTask).ConfigureAwait(false);
            await ObserveCleanupTaskAsync(monitorStopTask).ConfigureAwait(false);
            await _lifecycleGate.WaitAsync().ConfigureAwait(false);
            try {
                await CleanupSessionResourcesAsync(sendStop: true, disconnectAdapter: true, throwOnFailure: true)
                    .ConfigureAwait(false);
                Heartrate = 0;
                Status = DeviceStatus.Offline;
                BluetoothLog.Info($"Disconnected from {Name}.");
            } finally {
                _lifecycleGate.Release();
            }
        } catch (Exception e) {
            failure = e;
            Heartrate = 0;
            Status = DeviceStatus.Offline;
        } finally {
            sessionCts?.Dispose();
            lock (_sync) {
                if (ReferenceEquals(_disconnectCompletion, completion)) _disconnectCompletion = null;
            }
        }

        if (failure == null) completion.TrySetResult();
        else completion.TrySetException(failure);
    }

    private static async Task ObserveCleanupTaskAsync(Task? task) {
        if (task == null) return;
        try {
            await task.ConfigureAwait(false);
        } catch (OperationCanceledException) {
            // Cancellation is expected when disconnect interrupts startup.
        } catch {
            // The operation's owner reports its failure; disconnect still completes cleanup.
        }
    }

    public override ValueTask DisposeAsync() {
        lock (_sync) {
            if (_disposeTask != null) return new ValueTask(_disposeTask);
            _disposed = true;
            ConnectedDevice.Disconnected -= OnDeviceDisconnected;
            _disposeTask = DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync() {
        await Task.Yield();
        await DisconnectAsync().ConfigureAwait(false);
    }

    private void OnDeviceDisconnected(object? sender, EventArgs e) {
        var disconnectTask = DisconnectAsync();
        lock (_sync) {
            if (_unexpectedDisconnectTask is { IsCompleted: false }) return;
            _unexpectedDisconnectTask = ObserveUnexpectedDisconnectAsync(disconnectTask);
        }
    }

    private async Task ObserveUnexpectedDisconnectAsync(Task disconnectTask) {
        try {
            await disconnectTask.ConfigureAwait(false);
        } catch (Exception e) {
            BluetoothLog.Error($"Cleanup after disconnect failed: {e.Message}");
        }
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

    private async Task AuthenticateCoreAsync(long generation, CancellationToken cancellationToken) {
        Status = DeviceStatus.Authenticating;
        BluetoothLog.Info("Starting authentication handshake...");
        var service = await ConnectedDevice.GetPrimaryServiceAsync(AuthServiceUuid).ConfigureAwait(false);
        if (service == null) throw new InvalidOperationException("Authentication service (0xFEE1) was not found.");

        var characteristic = await service.GetCharacteristicAsync(AuthCharacteristicUuid).ConfigureAwait(false);
        if (characteristic == null) throw new InvalidOperationException("Authentication characteristic was not found.");

        var notifications = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false
        });
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<GattCharacteristicValueChangedEventArgs> handler = (_, args) => {
            if (!IsCurrentSession(generation)) return;
            notifications.Writer.TryWrite(args.Value.ToArray());
        };

        _authCharacteristic = characteristic;
        _authNotifyHandler = handler;
        _authCompletion = completion;
        _authState = UsesKeyExchange ? AuthState.SentKey : AuthState.RequestedChallenge;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(AuthenticationTimeout);
        try {
            characteristic.SubscribeToValueChanges();
            characteristic.CharacteristicValueChanged += handler;
            await characteristic.StartNotificationsAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            _authNotificationTask = ProcessAuthenticationNotificationsAsync(notifications.Reader, characteristic,
                generation, completion, timeoutCts.Token);
            await Write(characteristic, GetInitialAuthCommand()).ConfigureAwait(false);
            await completion.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            var timeout = new TimeoutException($"Authentication handshake timed out after {AuthenticationTimeout}.");
            completion.TrySetException(timeout);
            throw timeout;
        } catch (OperationCanceledException) {
            completion.TrySetCanceled(cancellationToken);
            throw;
        } catch (Exception e) {
            completion.TrySetException(e);
            throw;
        } finally {
            timeoutCts.Cancel();
            notifications.Writer.TryComplete();
            await ObserveCleanupTaskAsync(_authNotificationTask).ConfigureAwait(false);
            await ObserveCleanupTaskAsync(completion.Task).ConfigureAwait(false);
            await CleanupAuthenticationCoreAsync().ConfigureAwait(false);
        }
    }

    private async Task ProcessAuthenticationNotificationsAsync(ChannelReader<byte[]> reader,
        IGattCharacteristic characteristic, long generation, TaskCompletionSource<bool> completion,
        CancellationToken cancellationToken) {
        try {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) {
                while (reader.TryRead(out var value)) {
                    if (!IsCurrentSession(generation)) throw new OperationCanceledException(cancellationToken);
                    if (await ProcessAuthenticationNotificationAsync(characteristic, value).ConfigureAwait(false)) {
                        completion.TrySetResult(true);
                        return;
                    }
                }
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            completion.TrySetCanceled(cancellationToken);
        } catch (Exception e) {
            completion.TrySetException(e);
        }
    }

    private async Task<bool> ProcessAuthenticationNotificationAsync(IGattCharacteristic characteristic, byte[] value) {
        if (value.Length < 3) return false;

        switch (value[1]) {
            case 0x01:
                if (value[2] != 0x01) {
                    if (_authState == AuthState.SentKey) {
                        _authState = AuthState.Failed;
                        throw new InvalidOperationException("Authentication key was rejected.");
                    }

                    break;
                }

                if (_authState == AuthState.SentKey) {
                    BluetoothLog.Info("Key accepted, requesting random challenge...");
                    _authState = AuthState.RequestedChallenge;
                    await Write(characteristic, RandomNumberRequestCommand).ConfigureAwait(false);
                } else {
                    BluetoothLog.Info("Ignoring duplicate key-accepted notification.");
                }

                break;
            case 0x02:
                if (_authState != AuthState.RequestedChallenge) {
                    BluetoothLog.Info("Ignoring duplicate/stale challenge notification.");
                    break;
                }

                if (value[2] != 0x01) {
                    _authState = AuthState.Failed;
                    throw new InvalidOperationException("Authentication challenge was rejected.");
                }

                if (value.Length != 19) {
                    throw new InvalidDataException("Authentication challenge must contain exactly 16 bytes.");
                }

                BluetoothLog.Info("Challenge received, sending encrypted response...");
                var encryptedNumber = EncryptAuthenticationNumber(value.AsSpan(3).ToArray());
                _authState = AuthState.SentEncryptedResponse;
                await Write(characteristic, Combine(EncryptedNumberResponsePrefix, encryptedNumber))
                    .ConfigureAwait(false);
                break;
            case 0x03:
                if (value[2] != 0x01) {
                    if (_authState == AuthState.SentEncryptedResponse) {
                        _authState = AuthState.Failed;
                        throw new InvalidOperationException("Authentication response was rejected.");
                    }

                    break;
                }

                if (_authState == AuthState.Authenticated) break;
                if (_authState != AuthState.SentEncryptedResponse) {
                    throw new InvalidDataException("Authentication success was received in an invalid protocol state.");
                }

                BluetoothLog.Success("Authentication successful.");
                _authState = AuthState.Authenticated;
                Status = DeviceStatus.OnlineAuth;
                return true;
        }

        return false;
    }

    private async Task CleanupAuthenticationCoreAsync() {
        var characteristic = _authCharacteristic;
        var handler = _authNotifyHandler;
        _authCharacteristic = null;
        _authNotifyHandler = null;
        _authCompletion = null;
        _authNotificationTask = null;
        _authState = AuthState.None;
        if (characteristic == null) return;

        if (handler != null) characteristic.CharacteristicValueChanged -= handler;
        try {
            await characteristic.StopNotificationsAsync().ConfigureAwait(false);
        } catch (Exception e) {
            BluetoothLog.Error($"Unable to stop authentication notifications: {e.Message}");
        }
    }

    /// <summary>
    /// Encrypts the 16 byte random challenge with AES-128/ECB (no padding)
    /// using the device authentication key.
    /// </summary>
    private byte[] EncryptAuthenticationNumber(byte[] number) {
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

    public override Task StartHeartrateMonitorAsync(bool continuous = false,
        CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync) {
            ThrowIfDisposed();
            if (_monitorStartTask is { IsCompleted: false } activeStart) {
                return cancellationToken.CanBeCanceled ? activeStart.WaitAsync(cancellationToken) : activeStart;
            }

            if (HeartrateMonitorStarted) return Task.CompletedTask;
            if (_sessionCts == null || Status != DeviceStatus.OnlineAuth) {
                throw new InvalidOperationException("The device must be authenticated before monitoring starts.");
            }

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_sessionCts.Token, cancellationToken);
            _monitorStartCts = linkedCts;
            _monitorStartTask = StartMonitorSessionAsync(_sessionGeneration, linkedCts, continuous);
            return _monitorStartTask;
        }
    }

    private async Task StartMonitorSessionAsync(long sessionGeneration, CancellationTokenSource startupCts,
        bool continuous) {
        await Task.Yield();
        var cancellationToken = startupCts.Token;
        var acquired = false;
        var monitorGeneration = 0L;
        try {
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            EnsureCurrentSession(sessionGeneration, cancellationToken);
            if (HeartrateMonitorStarted) return;

            monitorGeneration = Interlocked.Increment(ref _monitorGeneration);
            _continuous = continuous;
            BluetoothLog.Info(continuous
                ? "Starting continuous heart rate monitoring..."
                : "Starting single heart rate measurement...");

            var service = await ConnectedDevice.GetPrimaryServiceAsync(HeartRateServiceUuid).ConfigureAwait(false);
            if (service == null) throw new InvalidOperationException("Heart rate service (0x180D) was not found.");

            var controlPoint = await service.GetCharacteristicAsync(HeartRateControlPointUuid).ConfigureAwait(false);
            var measurement = await service.GetCharacteristicAsync(HeartRateMeasurementUuid).ConfigureAwait(false);
            if (controlPoint == null) throw new InvalidOperationException("Heart rate control point was not found.");
            if (measurement == null) throw new InvalidOperationException("Heart rate measurement characteristic was not found.");

            _heartRateControlPoint = controlPoint;
            _heartRateMeasurement = measurement;
            EventHandler<GattCharacteristicValueChangedEventArgs> handler = (_, args) =>
                OnHeartRateNotify(sessionGeneration, monitorGeneration, args);
            _heartRateNotifyHandler = handler;
            measurement.SubscribeToValueChanges();
            measurement.CharacteristicValueChanged += handler;
            await measurement.StartNotificationsAsync().ConfigureAwait(false);
            await Task.Delay(HeartrateStartupDelay, cancellationToken).ConfigureAwait(false);
            EnsureCurrentSession(sessionGeneration, cancellationToken);

            if (continuous) {
                await WriteWithResponse(controlPoint, [0x15, 0x02, 0x00]).ConfigureAwait(false);
                await WriteWithResponse(controlPoint, [0x15, 0x01, 0x01]).ConfigureAwait(false);
                var keepAliveCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _sessionCts?.Token ?? cancellationToken);
                _keepAliveCts = keepAliveCts;
                _keepAliveTask = RunHeartRateKeepAliveAsync(controlPoint, sessionGeneration, monitorGeneration,
                    keepAliveCts.Token);
            } else {
                await WriteWithResponse(controlPoint, [0x15, 0x01, 0x00]).ConfigureAwait(false);
                await WriteWithResponse(controlPoint, [0x15, 0x02, 0x01]).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            HeartrateMonitorStarted = true;
            BluetoothLog.Success("Heart rate monitoring started.");
        } catch (Exception e) {
            if (acquired) await CleanupMonitoringCoreAsync(sendStop: true).ConfigureAwait(false);
            if (e is not OperationCanceledException) {
                BluetoothLog.Error($"Failed to start heart rate monitoring: {e.Message}");
            }

            throw;
        } finally {
            if (acquired) _lifecycleGate.Release();
            lock (_sync) {
                if (ReferenceEquals(_monitorStartCts, startupCts)) _monitorStartCts = null;
                _monitorStartTask = null;
            }

            startupCts.Dispose();
        }
    }

    public override async Task StopHeartrateMonitorAsync(CancellationToken cancellationToken = default) {
        Task? startupTask;
        Task? scheduledStopTask;
        CancellationTokenSource? startupCts;
        CancellationTokenSource? keepAliveCts;
        lock (_sync) {
            startupTask = _monitorStartTask;
            startupCts = _monitorStartCts;
            keepAliveCts = _keepAliveCts;
            Interlocked.Increment(ref _monitorGeneration);
            scheduledStopTask = _monitorStopTask;
        }

        CancelSafely(startupCts);
        CancelSafely(keepAliveCts);
        await ObserveCleanupTaskAsync(startupTask).ConfigureAwait(false);
        await ObserveCleanupTaskAsync(scheduledStopTask).ConfigureAwait(false);
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try {
            await CleanupMonitoringCoreAsync(sendStop: true).ConfigureAwait(false);
        } finally {
            _lifecycleGate.Release();
        }

        lock (_sync) scheduledStopTask = _monitorStopTask;
        await ObserveCleanupTaskAsync(scheduledStopTask).ConfigureAwait(false);
    }

    private void OnHeartRateNotify(long sessionGeneration, long monitorGeneration,
        GattCharacteristicValueChangedEventArgs args) {
        if (!IsCurrentMonitor(sessionGeneration, monitorGeneration)) return;
        var value = args.Value;
        if (value.Length < 2) return;

        var heartrate = value[1];
        if (heartrate > 0) Heartrate = heartrate;
        if (!_continuous) ScheduleMonitorStop(sessionGeneration, monitorGeneration);
    }

    private void ScheduleMonitorStop(long sessionGeneration, long monitorGeneration) {
        lock (_sync) {
            if (sessionGeneration != _sessionGeneration || _sessionCts == null ||
                monitorGeneration != _monitorGeneration || _heartRateMeasurement == null) return;
            if (_monitorStopTask is { IsCompleted: false }) return;
            _monitorStopTask = StopMonitorFromCallbackAsync(sessionGeneration, monitorGeneration);
        }
    }

    private async Task StopMonitorFromCallbackAsync(long sessionGeneration, long monitorGeneration) {
        await Task.Yield();
        try {
            if (!IsCurrentMonitor(sessionGeneration, monitorGeneration)) return;
            Task? keepAliveTask;
            lock (_sync) keepAliveTask = _keepAliveTask;
            await ObserveCleanupTaskAsync(keepAliveTask).ConfigureAwait(false);
            await _lifecycleGate.WaitAsync().ConfigureAwait(false);
            try {
                if (IsCurrentMonitor(sessionGeneration, monitorGeneration)) {
                    BluetoothLog.Info("Stopping heart rate monitoring.");
                    await CleanupMonitoringCoreAsync(sendStop: true).ConfigureAwait(false);
                }
            } finally {
                _lifecycleGate.Release();
            }
        } catch (Exception e) {
            BluetoothLog.Error($"Failed to stop heart rate monitoring: {e.Message}");
        } finally {
            lock (_sync) _monitorStopTask = null;
        }
    }

    /// <summary>
    /// The band stops the continuous measurement after a short idle period, so a
    /// ping (0x16) is sent periodically to keep the session alive.
    /// </summary>
    private async Task RunHeartRateKeepAliveAsync(IGattCharacteristic controlPoint, long sessionGeneration,
        long monitorGeneration, CancellationToken cancellationToken) {
        try {
            while (true) {
                await Task.Delay(HeartRateKeepAliveInterval, cancellationToken).ConfigureAwait(false);
                if (!IsCurrentMonitor(sessionGeneration, monitorGeneration)) return;
                await WriteWithResponse(controlPoint, [0x16]).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Expected when monitoring is stopped or the owning session disconnects.
        } catch (Exception e) {
            BluetoothLog.Error($"Heart rate keep-alive failed: {e.Message}");
            ScheduleMonitorStop(sessionGeneration, monitorGeneration);
        }
    }

    private async Task CleanupMonitoringCoreAsync(bool sendStop) {
        Interlocked.Increment(ref _monitorGeneration);
        var controlPoint = _heartRateControlPoint;
        var measurement = _heartRateMeasurement;
        var handler = _heartRateNotifyHandler;
        var keepAliveCts = _keepAliveCts;
        var keepAliveTask = _keepAliveTask;
        _heartRateNotifyHandler = null;
        _keepAliveCts = null;
        _keepAliveTask = null;
        _continuous = false;

        if (handler != null && measurement != null) measurement.CharacteristicValueChanged -= handler;
        CancelSafely(keepAliveCts);
        await ObserveCleanupTaskAsync(keepAliveTask).ConfigureAwait(false);
        keepAliveCts?.Dispose();

        if (sendStop && controlPoint != null) {
            try {
                await WriteWithResponse(controlPoint, [0x15, 0x01, 0x00]).ConfigureAwait(false);
                await WriteWithResponse(controlPoint, [0x15, 0x02, 0x00]).ConfigureAwait(false);
            } catch (Exception e) {
                BluetoothLog.Error($"Unable to stop heart rate measurement: {e.Message}");
            }
        }

        if (measurement != null) {
            try {
                await measurement.StopNotificationsAsync().ConfigureAwait(false);
            } catch (Exception e) {
                BluetoothLog.Error($"Unable to stop heart rate notifications: {e.Message}");
            }
        }

        _heartRateControlPoint = null;
        _heartRateMeasurement = null;
        HeartrateMonitorStarted = false;
    }

    private async Task CleanupSessionResourcesAsync(bool sendStop, bool disconnectAdapter, bool throwOnFailure) {
        await CleanupMonitoringCoreAsync(sendStop).ConfigureAwait(false);
        await CleanupAuthenticationCoreAsync().ConfigureAwait(false);
        if (!disconnectAdapter) return;

        try {
            await ConnectedDevice.DisconnectAsync().ConfigureAwait(false);
        } catch (Exception e) {
            BluetoothLog.Error($"Unable to disconnect {Name}: {e.Message}");
            if (throwOnFailure) throw;
        }
    }

    private bool IsCurrentSession(long generation) {
        lock (_sync) return generation == _sessionGeneration && _sessionCts != null;
    }

    private bool IsCurrentMonitor(long sessionGeneration, long monitorGeneration) {
        lock (_sync) {
            return sessionGeneration == _sessionGeneration && _sessionCts != null &&
                   monitorGeneration == _monitorGeneration && _heartRateMeasurement != null;
        }
    }

    private void EnsureCurrentSession(long generation, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrentSession(generation)) throw new OperationCanceledException("The device session was replaced.");
    }

    private void SetOfflineIfCurrent(long generation) {
        lock (_sync) {
            if (generation == _sessionGeneration) Status = DeviceStatus.Offline;
        }
    }

    private void ThrowIfDisposed() {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);
    }

    private static void CancelSafely(CancellationTokenSource? cancellationTokenSource) {
        try {
            cancellationTokenSource?.Cancel();
        } catch (ObjectDisposedException) {
            // The owner already completed and disposed this operation.
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Writes a payload to a characteristic <b>without response</b>.
    ///
    /// The Huami auth (0x00000009-...) and sensor (0x00000001-...) characteristics
    /// only support write-without-response (BlueZ returns
    /// <c>org.bluez.Error.NotSupported</c> for a write-with-response). On Linux
    /// the backend omits the BlueZ "type" option so BlueZ auto-selects the write
    /// procedure from the characteristic's flags.
    /// </summary>
    protected static async Task Write(IGattCharacteristic characteristic, byte[] data) {
        try {
            //BluetoothLog.Info($"Writing {data.Length} bytes to {characteristic.Uuid}: [{string.Join(" ", data.Select(b => b.ToString("X2")))}]");
            await characteristic.WriteValueWithoutResponseAsync(data).ConfigureAwait(false);
        } catch (Exception e) {
            BluetoothLog.Error($"Unable to write on {characteristic.Uuid} - {e.Message}");
            throw;
        }
    }

    /// <summary>
    /// Writes a payload to a characteristic <b>with response</b>.
    ///
    /// The Heart Rate Control Point (0x2A39) is a Bluetooth SIG characteristic
    /// that supports write-with-response, so the start/stop/keep-alive command
    /// frames use this write type.
    /// </summary>
    protected static async Task WriteWithResponse(IGattCharacteristic characteristic, byte[] data) {
        try {
            //BluetoothLog.Info($"Writing {data.Length} bytes to {characteristic.Uuid}: [{string.Join(" ", data.Select(b => b.ToString("X2")))}]");
            await characteristic.WriteValueWithResponseAsync(data).ConfigureAwait(false);
        } catch (Exception e) {
            BluetoothLog.Error($"Unable to write on {characteristic.Uuid} - {e.Message}");
            throw;
        }
    }

    protected static byte[] Combine(byte[] prefix, byte[] payload) {
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
    private enum AuthState {
        None,
        SentKey,
        RequestedChallenge,
        SentEncryptedResponse,
        Authenticated,
        Failed
    }
}
