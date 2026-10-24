using MiBandHR.Core.Devices;

namespace MiBandHR.LifecycleChecks;

internal static class LifecycleChecks {
    public static IEnumerable<(string Name, Func<Task> Run)> All() {
        yield return ("connect concurrent callers share one authenticated handshake", ConcurrentConnectsShareHandshake);
        yield return ("authentication ignores duplicates and detaches stale callbacks", DuplicateAndStaleAuthenticationNotifications);
        yield return ("authentication rejects success in invalid state", AuthenticationRejectsInvalidSuccess);
        yield return ("authentication rejection releases subscriptions", AuthenticationRejectionFails);
        yield return ("missing authentication service fails and disconnects", MissingAuthenticationServiceFails);
        yield return ("missing authentication characteristic fails", MissingAuthenticationCharacteristicFails);
        yield return ("authentication handshake times out", AuthenticationTimesOut);
        yield return ("authentication write failure reaches caller", AuthenticationWriteFailurePropagates);
        yield return ("disconnect cancels authentication and awaits cleanup", DisconnectCancelsAuthentication);
        yield return ("previous session notifications cannot authenticate replacement", StaleSessionNotificationIsIgnored);
        yield return ("disconnect waits for non-cancellable connection work", DisconnectWaitsForNativeConnect);
        yield return ("reconnect cycles release auth handlers", ReconnectCyclesReleaseHandlers);
        yield return ("monitor startup is coalesced and stop is awaited", MonitoringStartupAndStopAreAwaited);
        yield return ("stop during partial notification startup cleans up", StopDuringPartialMonitorStartup);
        yield return ("disconnect during partial notification startup cleans up", DisconnectDuringPartialMonitorStartup);
        yield return ("one-shot measurement stops from its notification", OneShotNotificationStopsMonitor);
        yield return ("keep-alive failure is observed and stops monitoring", KeepAliveFailureIsObserved);
        yield return ("monitoring write failure reaches caller and cleans up", MonitoringWriteFailurePropagates);
        yield return ("missing heart rate service fails monitoring startup", MissingHeartRateServiceFails);
        yield return ("unexpected disconnect awaits owned cleanup", UnexpectedDisconnectCleansUp);
        yield return ("dummy monitor stop and disposal await worker", DummyStopAndDisposeAwaitWorker);
    }

    private static async Task ConcurrentConnectsShareHandshake() {
        var harness = DeviceHarness.Create();
        var first = harness.Device.ConnectAsync();
        var second = harness.Device.ConnectAsync();
        var authentication = harness.Device.AuthenticateAsync();

        await Check.Until(() => harness.Authentication.WritesWithoutResponse.Count == 1,
            "Initial auth command should be issued once.");
        await Check.Pending(first, "Connect should remain pending until authentication succeeds.");
        Check.Equal(1, harness.BluetoothDevice.ConnectCalls, "Transport connect count");
        Check.Equal(1, harness.Authentication.WritesWithoutResponse.Count, "Initial auth write count");

        harness.Authentication.Emit(DeviceHarness.ChallengeNotification());
        await Check.Until(() => harness.Authentication.WritesWithoutResponse.Count == 2,
            "Encrypted response should be written once.");
        harness.Authentication.Emit([0x10, 0x03, 0x01]);
        await Task.WhenAll(first, second, authentication).WaitAsync(Check.Timeout);
        Check.Equal(DeviceStatus.OnlineAuth, harness.Device.Status, "Authenticated state");
        Check.Equal(0, harness.Authentication.SubscriberCount, "Auth handlers after success");
        Check.Equal(1, harness.Authentication.StopNotificationCalls, "Auth notification stop count");
        await harness.Device.DisposeAsync();
    }

    private static async Task DuplicateAndStaleAuthenticationNotifications() {
        var harness = DeviceHarness.Create();
        var connect = harness.Device.ConnectAsync();
        await Check.Until(() => harness.Authentication.WritesWithoutResponse.Count == 1, "Initial write missing.");
        var challenge = DeviceHarness.ChallengeNotification();
        harness.Authentication.Emit(challenge);
        await Check.Until(() => harness.Authentication.WritesWithoutResponse.Count == 2, "Response write missing.");
        harness.Authentication.Emit(challenge);
        harness.Authentication.Emit([0x10, 0x03, 0x01]);
        await connect.WaitAsync(Check.Timeout);
        harness.Authentication.Emit(challenge);
        harness.Authentication.Emit([0x10, 0x03, 0x01]);
        await Task.Delay(30);
        Check.Equal(2, harness.Authentication.WritesWithoutResponse.Count, "Duplicate/stale auth writes");
        Check.Equal(DeviceStatus.OnlineAuth, harness.Device.Status, "Stale callback changed state");
        await harness.Device.DisposeAsync();
    }

    private static async Task AuthenticationRejectsInvalidSuccess() {
        var harness = DeviceHarness.Create();
        var connect = harness.Device.ConnectAsync();
        await Check.Until(() => harness.Authentication.WritesWithoutResponse.Count == 1, "Initial write missing.");
        harness.Authentication.Emit([0x10, 0x03, 0x01]);
        await Check.Throws<InvalidDataException>(connect);
        Check.Equal(DeviceStatus.Offline, harness.Device.Status, "Status after invalid success");
        Check.Equal(0, harness.Authentication.SubscriberCount, "Auth handlers after invalid success");
        Check.True(harness.BluetoothDevice.DisconnectCalls > 0, "Failed authentication must disconnect transport.");
        await harness.Device.DisposeAsync();
    }

    private static async Task AuthenticationRejectionFails() {
        var harness = DeviceHarness.Create();
        var connect = harness.Device.ConnectAsync();
        await Check.Until(() => harness.Authentication.WritesWithoutResponse.Count == 1, "Initial write missing.");
        harness.Authentication.Emit([0x10, 0x02, 0x04]);
        await Check.Throws<InvalidOperationException>(connect);
        Check.Equal(0, harness.Authentication.SubscriberCount, "Auth handlers after rejection");
        Check.Equal(DeviceStatus.Offline, harness.Device.Status, "Status after rejection");
        await harness.Device.DisposeAsync();
    }

    private static async Task MissingAuthenticationServiceFails() {
        var harness = DeviceHarness.Create(includeAuthService: false);
        await Check.Throws<InvalidOperationException>(harness.Device.ConnectAsync());
        Check.Equal(DeviceStatus.Offline, harness.Device.Status, "Status after service discovery failure");
        Check.True(harness.BluetoothDevice.DisconnectCalls > 0, "Missing auth service must disconnect transport.");
        await harness.Device.DisposeAsync();
    }

    private static async Task MissingAuthenticationCharacteristicFails() {
        var harness = DeviceHarness.Create(includeAuthCharacteristic: false);
        await Check.Throws<InvalidOperationException>(harness.Device.ConnectAsync());
        Check.Equal(DeviceStatus.Offline, harness.Device.Status, "Status after missing auth characteristic");
        await harness.Device.DisposeAsync();
    }

    private static async Task AuthenticationTimesOut() {
        var harness = DeviceHarness.Create(TimeSpan.FromMilliseconds(100));
        var connect = harness.Device.ConnectAsync();
        await Check.Until(() => harness.Authentication.WritesWithoutResponse.Count == 1, "Initial write missing.");
        await Check.Throws<TimeoutException>(connect);
        Check.Equal(DeviceStatus.Offline, harness.Device.Status, "Status after auth timeout");
        Check.Equal(0, harness.Authentication.SubscriberCount, "Auth handlers after timeout");
        await harness.Device.DisposeAsync();
    }

    private static async Task AuthenticationWriteFailurePropagates() {
        var harness = DeviceHarness.Create();
        harness.Authentication.FailWriteWithoutResponse = _ => new IOException("injected write failure");
        await Check.Throws<IOException>(harness.Device.ConnectAsync());
        Check.Equal(DeviceStatus.Offline, harness.Device.Status, "Status after auth write failure");
        Check.Equal(0, harness.Authentication.SubscriberCount, "Auth handlers after write failure");
        await harness.Device.DisposeAsync();
    }

    private static async Task DisconnectCancelsAuthentication() {
        var harness = DeviceHarness.Create();
        var connect = harness.Device.ConnectAsync();
        await Check.Until(() => harness.Authentication.WritesWithoutResponse.Count == 1, "Initial write missing.");
        using var canceledCaller = new CancellationTokenSource();
        canceledCaller.Cancel();
        var disconnect = harness.Device.DisconnectAsync(canceledCaller.Token);
        await Check.Throws<OperationCanceledException>(connect);
        await disconnect.WaitAsync(Check.Timeout);
        Check.Equal(DeviceStatus.Offline, harness.Device.Status, "Status after disconnect");
        Check.Equal(0, harness.Authentication.SubscriberCount, "Auth handlers after disconnect");
        Check.True(!harness.BluetoothDevice.IsConnected, "Transport remained connected.");
        await harness.Device.DisposeAsync();
    }

    private static async Task StaleSessionNotificationIsIgnored() {
        var harness = DeviceHarness.Create();
        var firstConnect = harness.Device.ConnectAsync();
        await Check.Until(() => harness.Authentication.WritesWithoutResponse.Count == 1, "Initial write missing.");
        var staleHandler = harness.Authentication.CaptureHandlers();
        var disconnect = harness.Device.DisconnectAsync();
        await Check.Throws<OperationCanceledException>(firstConnect);
        await disconnect.WaitAsync(Check.Timeout);

        var newSessionWriteCount = harness.Authentication.WritesWithoutResponse.Count;
        var replacementConnect = harness.Device.ConnectAsync();
        await Check.Until(() => harness.Authentication.WritesWithoutResponse.Count == newSessionWriteCount + 1,
            "Replacement auth command was not written.");
        harness.Authentication.EmitTo(staleHandler, [0x10, 0x03, 0x01]);
        await Check.Pending(replacementConnect, "A stale notification authenticated the replacement session.");
        Check.Equal(newSessionWriteCount + 1, harness.Authentication.WritesWithoutResponse.Count,
            "Stale notification produced a write in the replacement session.");

        harness.Authentication.Emit(DeviceHarness.ChallengeNotification());
        await Check.Until(() => harness.Authentication.WritesWithoutResponse.Count == newSessionWriteCount + 2,
            "Replacement response write missing.");
        harness.Authentication.Emit([0x10, 0x03, 0x01]);
        await replacementConnect.WaitAsync(Check.Timeout);
        await harness.Device.DisposeAsync();
    }

    private static async Task DisconnectWaitsForNativeConnect() {
        var harness = DeviceHarness.Create();
        var connectGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.BluetoothDevice.ConnectGate = connectGate;
        var connect = harness.Device.ConnectAsync();
        await Check.Until(() => harness.BluetoothDevice.ConnectCalls == 1, "Transport connect did not start.");
        var disconnect = harness.Device.DisconnectAsync();
        await Check.Pending(disconnect, "Disconnect returned while non-cancellable connect was still running.");
        connectGate.TrySetResult();
        await Check.Throws<OperationCanceledException>(connect);
        await disconnect.WaitAsync(Check.Timeout);
        Check.True(!harness.BluetoothDevice.IsConnected, "Connection survived canceled session cleanup.");
        await harness.Device.DisposeAsync();
    }

    private static async Task ReconnectCyclesReleaseHandlers() {
        var harness = DeviceHarness.Create();
        for (var i = 0; i < 3; i++) {
            await harness.ConnectAuthenticatedAsync();
            Check.Equal(0, harness.Authentication.SubscriberCount, $"Auth subscribers after cycle {i}");
            await harness.Device.DisconnectAsync();
            Check.Equal(0, harness.Authentication.SubscriberCount, $"Auth subscribers after disconnect {i}");
        }

        Check.Equal(3, harness.Authentication.StartNotificationCalls, "Auth notification start count");
        Check.Equal(3, harness.Authentication.StopNotificationCalls, "Auth notification stop count");
        await harness.Device.DisposeAsync();
    }

    private static async Task MonitoringStartupAndStopAreAwaited() {
        var harness = DeviceHarness.Create();
        await harness.ConnectAuthenticatedAsync();
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Measurement.StartGate = startGate;
        var firstStart = harness.Device.StartHeartrateMonitorAsync(continuous: true);
        var secondStart = harness.Device.StartHeartrateMonitorAsync(continuous: true);
        await Check.Until(() => harness.Measurement.StartNotificationCalls == 1, "Notification start was not coalesced.");
        await Check.Pending(firstStart, "Monitoring startup returned before notification setup completed.");
        startGate.TrySetResult();
        await Task.WhenAll(firstStart, secondStart).WaitAsync(Check.Timeout);
        Check.True(harness.Device.HeartrateMonitorStarted, "Monitor was not marked started.");
        Check.Equal(2, harness.ControlPoint.WritesWithResponse.Count, "Continuous start writes");

        await harness.Device.StopHeartrateMonitorAsync();
        Check.True(!harness.Device.HeartrateMonitorStarted, "Monitor remained started after stop.");
        Check.Equal(0, harness.Measurement.SubscriberCount, "Measurement handlers after stop");
        Check.True(!harness.Measurement.NotificationsStarted, "Measurement notifications remained enabled.");
        Check.True(harness.ControlPoint.WritesWithResponse.Count >= 4, "Stop command frames were not written.");
        await harness.Device.DisposeAsync();
    }

    private static async Task StopDuringPartialMonitorStartup() {
        var harness = DeviceHarness.Create();
        await harness.ConnectAuthenticatedAsync();
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Measurement.StartGate = startGate;
        var start = harness.Device.StartHeartrateMonitorAsync(continuous: true);
        await Check.Until(() => harness.Measurement.StartNotificationCalls == 1, "Notification start did not begin.");
        var stop = harness.Device.StopHeartrateMonitorAsync();
        await Check.Pending(stop, "Stop returned before partial startup teardown.");
        startGate.TrySetResult();
        await Check.Throws<OperationCanceledException>(start);
        await stop.WaitAsync(Check.Timeout);
        Check.Equal(0, harness.Measurement.SubscriberCount, "Partial start retained handlers.");
        Check.True(!harness.Measurement.NotificationsStarted, "Partial start left notifications enabled.");
        await harness.Device.DisposeAsync();
    }

    private static async Task DisconnectDuringPartialMonitorStartup() {
        var harness = DeviceHarness.Create();
        await harness.ConnectAuthenticatedAsync();
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Measurement.StartGate = startGate;
        var start = harness.Device.StartHeartrateMonitorAsync(continuous: true);
        await Check.Until(() => harness.Measurement.StartNotificationCalls == 1, "Notification start did not begin.");
        var disconnect = harness.Device.DisconnectAsync();
        await Check.Pending(disconnect, "Disconnect returned before partial startup teardown.");
        startGate.TrySetResult();
        await Check.Throws<OperationCanceledException>(start);
        await disconnect.WaitAsync(Check.Timeout);
        Check.Equal(0, harness.Measurement.SubscriberCount, "Disconnect during start retained handlers.");
        Check.True(!harness.Measurement.NotificationsStarted, "Disconnect during start left notifications enabled.");
        Check.True(!harness.BluetoothDevice.IsConnected, "Disconnect during start left transport connected.");
        await harness.Device.DisposeAsync();
    }

    private static async Task OneShotNotificationStopsMonitor() {
        var harness = DeviceHarness.Create();
        await harness.ConnectAuthenticatedAsync();
        await harness.Device.StartHeartrateMonitorAsync(continuous: false);
        harness.Measurement.Emit(0x00, 0x72);
        await Check.Until(() => !harness.Device.HeartrateMonitorStarted && harness.Measurement.SubscriberCount == 0,
            "One-shot notification did not stop monitoring.");
        Check.Equal((ushort)0x72, harness.Device.Heartrate, "One-shot heart rate value");
        await harness.Device.DisposeAsync();
    }

    private static async Task KeepAliveFailureIsObserved() {
        var harness = DeviceHarness.Create();
        await harness.ConnectAuthenticatedAsync();
        harness.ControlPoint.FailWriteWithResponse = data => data.Length == 1 && data[0] == 0x16
            ? new IOException("injected keep-alive failure")
            : null;
        await harness.Device.StartHeartrateMonitorAsync(continuous: true);
        await Check.Until(() => !harness.Device.HeartrateMonitorStarted && harness.Measurement.SubscriberCount == 0,
            "Keep-alive failure was not observed and cleaned up.");
        await harness.Device.DisposeAsync();
    }

    private static async Task MonitoringWriteFailurePropagates() {
        var harness = DeviceHarness.Create();
        await harness.ConnectAuthenticatedAsync();
        harness.ControlPoint.FailWriteWithResponse = data => data.Length == 3 && data[0] == 0x15 &&
            data[1] == 0x01 && data[2] == 0x01 ? new IOException("injected monitor start failure") : null;
        await Check.Throws<IOException>(harness.Device.StartHeartrateMonitorAsync(continuous: true));
        Check.True(!harness.Device.HeartrateMonitorStarted, "Failed monitor start remained active.");
        Check.Equal(0, harness.Measurement.SubscriberCount, "Monitor handlers after write failure");
        Check.True(!harness.Measurement.NotificationsStarted, "Notifications remained enabled after write failure.");
        await harness.Device.DisposeAsync();
    }

    private static async Task MissingHeartRateServiceFails() {
        var harness = DeviceHarness.Create(includeHeartrateService: false);
        await harness.ConnectAuthenticatedAsync();
        await Check.Throws<InvalidOperationException>(harness.Device.StartHeartrateMonitorAsync());
        Check.True(!harness.Device.HeartrateMonitorStarted, "Missing service set monitor started.");
        await harness.Device.DisposeAsync();
    }

    private static async Task UnexpectedDisconnectCleansUp() {
        var harness = DeviceHarness.Create();
        await harness.ConnectAuthenticatedAsync();
        await harness.Device.StartHeartrateMonitorAsync(continuous: true);
        harness.BluetoothDevice.RaiseDisconnected();
        await Check.Until(() => harness.Device.Status == DeviceStatus.Offline &&
                               !harness.Device.HeartrateMonitorStarted && harness.Measurement.SubscriberCount == 0,
            "Unexpected disconnect did not clean up the session.");
        await harness.Device.DisposeAsync();
    }

    private static async Task DummyStopAndDisposeAwaitWorker() {
        var device = new Dummy_Device();
        await device.ConnectAsync();
        await device.StartHeartrateMonitorAsync();
        Check.True(device.HeartrateMonitorStarted, "Dummy monitor did not start.");
        await device.StopHeartrateMonitorAsync().WaitAsync(Check.Timeout);
        Check.True(!device.HeartrateMonitorStarted, "Dummy worker survived StopAsync.");
        await device.StartHeartrateMonitorAsync();
        await device.DisposeAsync().AsTask().WaitAsync(Check.Timeout);
        Check.True(!device.HeartrateMonitorStarted, "Dummy worker survived disposal.");
    }
}