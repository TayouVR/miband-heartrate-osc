using MiBandHR.BluetoothChecks.Fakes;
using MiBandHR.Core.Bluetooth;
using MiBandHR.Core.Bluetooth.InTheHand;
using MiBandHR.Core.Bluetooth.Salar;
#if WINDOWS
using MiBandHR.Core.Bluetooth.Windows;
using Windows.Devices.Bluetooth;
#else
using MiBandHR.Core.Bluetooth.BlueZ;
#endif

namespace MiBandHR.BluetoothChecks;

internal static class DeviceChecks {
    private sealed record Fixture(IBluetoothDevice Adapter, NativeOperation Connect, Func<int> Teardowns,
        NativeOperation? Disconnect = null);

    public static IEnumerable<(string Name, Func<Task> Run)> All() {
        var factories = new List<(string Name, Func<Fixture> Create)> {
            ("Salar", () => {
                var native = new SalarAdapter();
                return new(new SalarBluetoothDevice(native, "address", "band"), native.Connect,
                    () => native.Device.DisposeCalls, native.Device.Disconnect);
            }),
            ("InTheHand", () => {
                var native = new global::InTheHand.Bluetooth.BluetoothDevice();
                return new(new InTheHandBluetoothDevice(native), native.Gatt.Connect, () => native.Gatt.DisconnectCalls);
            })
        };
#if WINDOWS
        factories.Add(("Windows", () => {
            BluetoothLEDevice.Reset();
            var native = BluetoothLEDevice.Next;
            return new(new WindowsBluetoothDevice("id", "band"), BluetoothLEDevice.Open, () => native.DisposeCalls);
        }));
#else
        factories.Add(("BlueZ", () => {
            var native = new Linux.Bluetooth.Device();
            return new(new BlueZBluetoothDevice(native, "id", "band"), native.Connect,
                () => native.Disconnect.Calls, native.Disconnect);
        }));
#endif
        foreach (var (name, create) in factories) {
            yield return ($"{name}.device.concurrent-connect", () => ConcurrentConnect(create()));
            yield return ($"{name}.device.disconnect-waits-connect", () => DisconnectWaitsConnect(create()));
            yield return ($"{name}.device.cancel-native-connect", () => CancelNativeConnect(create()));
            yield return ($"{name}.device.cancel-queued-connect", () => CancelQueuedConnect(create()));
            yield return ($"{name}.device.pre-canceled-connect", () => PreCanceledConnect(create()));
            yield return ($"{name}.device.failure-releases-gate", () => FailureReleasesGate(create()));
        }
        yield return ("Salar.device.disconnect-finally-and-reconnect", SalarDisconnectFinally);
        yield return ("Salar.device.connect-waits-disconnect", SalarConnectWaitsDisconnect);
#if !WINDOWS
        yield return ("BlueZ.device.already-connected-waits-services", BlueZAlreadyConnected);
        yield return ("BlueZ.device.requires-connected-and-services", BlueZBothProperties);
        yield return ("BlueZ.device.one-watcher-and-disconnect-event", BlueZOneWatcher);
        yield return ("BlueZ.device.disconnect-awaited", BlueZDisconnectAwaited);
        yield return ("BlueZ.device.watcher-failure-cleans-connection", BlueZWatchFailure);
        yield return ("BlueZ.device.cancel-services-wait", BlueZCancelServicesWait);
        yield return ("BlueZ.device.final-readiness-after-watcher", BlueZFinalReadiness);
        yield return ("BlueZ.device.disconnect-before-publish", BlueZDisconnectBeforePublish);
        yield return ("BlueZ.device.stale-watcher-after-reconnect", BlueZStaleWatcher);
        yield return ("BlueZ.device.cancel-watcher-install", BlueZCancelWatcherInstall);
#endif
    }

    private static async Task ConcurrentConnect(Fixture f) {
        using var point = f.Connect.PauseNext();
        var first = f.Adapter.ConnectAsync();
        await Check.Done(point.Entered);
        var second = f.Adapter.ConnectAsync();
        await Check.Pending(second, "Second connect must queue behind the native connect");
        Check.Equal(1, f.Connect.Calls, "Only one native connection in flight");
        point.Complete();
        await Check.Done(Task.WhenAll(first, second));
        Check.True(f.Adapter.IsConnected, "Connection must be published after success");
        Check.Equal(1, f.Connect.Calls, "Repeated connects must reuse connection");
        await Check.Done(f.Adapter.DisconnectAsync());
    }

    private static async Task DisconnectWaitsConnect(Fixture f) {
        using var point = f.Connect.PauseNext();
        var connect = f.Adapter.ConnectAsync();
        await Check.Done(point.Entered);
        var disconnect = f.Adapter.DisconnectAsync();
        await Check.Pending(disconnect, "Disconnect cannot finish before native connect");
        Check.Equal(0, f.Teardowns(), "No teardown while native connect owns lifecycle");
        point.Complete();
        await Check.Done(Task.WhenAll(connect, disconnect));
        Check.True(!f.Adapter.IsConnected, "Late native completion must not leave connection alive");
        Check.Equal(1, f.Teardowns(), "Native connection must be torn down once");
    }

    private static async Task CancelNativeConnect(Fixture f) {
        using var cancellation = new CancellationTokenSource();
        using var point = f.Connect.PauseNext();
        using var teardown = f.Disconnect?.PauseNext();
        var connect = f.Adapter.ConnectAsync(cancellation.Token);
        await Check.Done(point.Entered);
        cancellation.Cancel();
        await Check.Pending(connect, "Cancellation must retain ownership of noncancellable native connect");
        Check.Equal(0, f.Teardowns(), "No premature teardown of pending native operation");
        point.Complete();
        if (teardown != null) {
            await Check.Done(teardown.Entered);
            await Check.Pending(connect, "Canceled connect must await native teardown");
            teardown.Complete();
        }
        await Check.Throws<OperationCanceledException>(connect);
        Check.True(!f.Adapter.IsConnected, "Canceled native completion must not publish a connection");
        Check.Equal(1, f.Teardowns(), "Canceled connection must be torn down");
        await Check.Done(f.Adapter.ConnectAsync());
        Check.True(f.Adapter.IsConnected, "Cancellation must release lifecycle gate for restart");
        await Check.Done(f.Adapter.DisconnectAsync());
    }

    private static async Task CancelQueuedConnect(Fixture f) {
        using var point = f.Connect.PauseNext();
        using var cancellation = new CancellationTokenSource();
        var first = f.Adapter.ConnectAsync();
        await Check.Done(point.Entered);
        var queued = f.Adapter.ConnectAsync(cancellation.Token);
        cancellation.Cancel();
        await Check.Throws<OperationCanceledException>(queued);
        Check.Equal(1, f.Connect.Calls, "Canceled waiter must not invoke native connect");
        Check.Equal(0, f.Teardowns(), "Canceled waiter must not tear down another caller's connection");
        point.Complete();
        await Check.Done(first);
        Check.True(f.Adapter.IsConnected, "Active connection attempt must survive queued cancellation");
        await Check.Done(f.Adapter.DisconnectAsync());
    }

    private static async Task PreCanceledConnect(Fixture f) {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Check.Throws<OperationCanceledException>(f.Adapter.ConnectAsync(cancellation.Token));
        Check.Equal(0, f.Connect.Calls, "Pre-canceled connect must not touch native API");
        Check.Equal(0, f.Teardowns(), "Pre-canceled connect must not tear down native API");
    }

    private static async Task FailureReleasesGate(Fixture f) {
        f.Connect.FailNext();
        await Check.Throws<NativeFailureException>(f.Adapter.ConnectAsync());
        Check.True(!f.Adapter.IsConnected, "Failed connect cannot be published");
        await Check.Done(f.Adapter.ConnectAsync());
        Check.True(f.Adapter.IsConnected, "Connect failure must allow retry");
        await Check.Done(f.Adapter.DisconnectAsync());
    }

    private static async Task SalarDisconnectFinally() {
        var native = new SalarAdapter();
        var original = native.Device;
        var adapter = new SalarBluetoothDevice(native, "address", "band");
        await Check.Done(adapter.ConnectAsync());
        Check.True(native.LastConfig is { AutoConnect: false }, "Salar must not enable auto-connect");
        using var point = original.Disconnect.PauseNext();
        var disconnect = adapter.DisconnectAsync();
        await Check.Done(point.Entered);
        await Check.Pending(disconnect, "Disconnect must await Salar native task");
        Check.Equal(0, original.DisposeCalls, "Dispose must happen only after native task settles");
        point.Fail();
        await Check.Done(disconnect);
        Check.Equal(1, original.DisposeCalls, "Failing disconnect must still dispose device");
        Check.True(!adapter.IsConnected, "Failing disconnect must clear device field");
        await Check.Done(adapter.DisconnectAsync());
        Check.Equal(1, original.Disconnect.Calls, "Repeated disconnect must not reuse cleared field");
        Check.Equal(1, original.DisposeCalls, "Repeated disconnect must not double-dispose");
        native.Device = new SalarDevice();
        await Check.Done(adapter.ConnectAsync());
        Check.True(adapter.IsConnected, "Disconnect failure must allow fresh connection");
        await Check.Done(adapter.DisconnectAsync());
        Check.Equal(1, native.Device.DisposeCalls, "Fresh device must be owned and disposed");
    }

    private static async Task SalarConnectWaitsDisconnect() {
        var native = new SalarAdapter();
        var original = native.Device;
        var adapter = new SalarBluetoothDevice(native, "address", "band");
        await Check.Done(adapter.ConnectAsync());
        using var point = original.Disconnect.PauseNext();
        var disconnect = adapter.DisconnectAsync();
        await Check.Done(point.Entered);
        native.Device = new SalarDevice();
        var reconnect = adapter.ConnectAsync();
        await Check.Pending(reconnect, "Reconnect must wait for disconnect and dispose");
        Check.Equal(1, native.Connect.Calls, "No native reconnect before old disconnect completes");
        point.Complete();
        await Check.Done(Task.WhenAll(disconnect, reconnect));
        Check.Equal(1, original.DisposeCalls, "Original device must be disposed before replacement");
        Check.True(adapter.IsConnected, "Reconnect must publish replacement device");
        await Check.Done(adapter.DisconnectAsync());
    }

#if !WINDOWS
    private static async Task BlueZAlreadyConnected() {
        var native = new Linux.Bluetooth.Device();
        native.Properties.Connected = true;
        var adapter = new BlueZBluetoothDevice(native, "id", "band");
        var connect = adapter.ConnectAsync();
        await Check.Pending(connect, "Already connected BlueZ must still await ServicesResolved");
        Check.Equal(0, native.Connect.Calls, "Already connected device must not reconnect");
        native.Properties.ServicesResolved = true;
        await Check.Done(connect);
        native.Properties.ServicesResolved = false;
        var repeat = adapter.ConnectAsync();
        await Check.Pending(repeat, "Cached IsConnected must not bypass current service resolution");
        native.Properties.ServicesResolved = true;
        await Check.Done(repeat);
        Check.Equal(1, native.Watchers.Count, "Repeated connect must retain just one watcher");
        await Check.Done(adapter.DisconnectAsync());
    }

    private static async Task BlueZBothProperties() {
        var native = new Linux.Bluetooth.Device { ResolveOnConnect = false };
        var adapter = new BlueZBluetoothDevice(native, "id", "band");
        var connect = adapter.ConnectAsync();
        await Check.Pending(connect, "Connected alone is insufficient");
        native.Properties.Connected = false;
        native.Properties.ServicesResolved = true;
        await Check.Pending(connect, "ServicesResolved alone is insufficient");
        native.Properties.Connected = true;
        await Check.Done(connect);
        Check.True(adapter.IsConnected, "Both properties allow connection to complete");
        await Check.Done(adapter.DisconnectAsync());
    }

    private static async Task BlueZOneWatcher() {
        var native = new Linux.Bluetooth.Device();
        var adapter = new BlueZBluetoothDevice(native, "id", "band");
        var events = 0;
        adapter.Disconnected += (_, _) => events++;
        using var point = native.Watchers.Subscribe.PauseNext();
        var first = adapter.ConnectAsync();
        await Check.Done(point.Entered);
        var second = adapter.ConnectAsync();
        await Check.Pending(second, "Concurrent watcher installation must be serialized");
        point.Complete();
        await Check.Done(Task.WhenAll(first, second));
        Check.Equal(1, native.Watchers.Subscribe.Calls, "Only one watcher installation");
        native.LoseConnection();
        native.LoseConnection();
        Check.Equal(1, events, "One disconnection event per connected session");
        Check.True(!adapter.IsConnected, "Connected watcher updates cached connection state");
        await Check.Done(adapter.DisconnectAsync());
        Check.Equal(0, native.Watchers.Count, "Disconnect must detach property watcher");
        Check.Equal(1, native.Watchers.Disposed, "Watcher disposed exactly once");
    }

    private static async Task BlueZDisconnectAwaited() {
        var native = new Linux.Bluetooth.Device();
        var adapter = new BlueZBluetoothDevice(native, "id", "band");
        await Check.Done(adapter.ConnectAsync());
        using var point = native.Disconnect.PauseNext();
        var disconnect = adapter.DisconnectAsync();
        await Check.Done(point.Entered);
        await Check.Pending(disconnect, "BlueZ disconnect must await D-Bus disconnect");
        Check.Equal(0, native.Watchers.Count, "Disconnect must remove watcher before native wait");
        var reconnect = adapter.ConnectAsync();
        await Check.Pending(reconnect, "Reconnect must wait for D-Bus disconnect");
        Check.Equal(1, native.Connect.Calls, "No concurrent native reconnect");
        point.Complete();
        await Check.Done(Task.WhenAll(disconnect, reconnect));
        Check.True(adapter.IsConnected, "Reconnect succeeds after awaited disconnect");
        await Check.Done(adapter.DisconnectAsync());
    }

    private static async Task BlueZWatchFailure() {
        var native = new Linux.Bluetooth.Device();
        var adapter = new BlueZBluetoothDevice(native, "id", "band");
        native.Watchers.Subscribe.FailNext();
        await Check.Throws<NativeFailureException>(adapter.ConnectAsync());
        Check.True(!adapter.IsConnected && !native.Properties.Connected, "Watcher failure must tear down connection");
        Check.Equal(0, native.Watchers.Count, "Failed watcher leaves no subscription");
        await Check.Done(adapter.ConnectAsync());
        Check.Equal(1, native.Watchers.Count, "Retry installs exactly one watcher");
        await Check.Done(adapter.DisconnectAsync());
    }

    private static async Task BlueZCancelServicesWait() {
        var native = new Linux.Bluetooth.Device { ResolveOnConnect = false };
        var adapter = new BlueZBluetoothDevice(native, "id", "band");
        using var cancellation = new CancellationTokenSource();
        var connect = adapter.ConnectAsync(cancellation.Token);
        await Check.Pending(connect, "Connect must await unresolved services");
        cancellation.Cancel();
        await Check.Throws<OperationCanceledException>(connect);
        Check.True(!native.Properties.Connected && !adapter.IsConnected, "Canceled service wait must disconnect");
        Check.Equal(0, native.Watchers.Count, "Canceled service wait cannot leak watcher");
    }

    private static async Task BlueZFinalReadiness() {
        var native = new Linux.Bluetooth.Device();
        var adapter = new BlueZBluetoothDevice(native, "id", "band");
        using var watcher = native.Watchers.Subscribe.PauseNext();
        var connect = adapter.ConnectAsync();
        await Check.Done(watcher.Entered);
        native.Properties.ServicesResolved = false;
        watcher.Complete();
        await Check.Throws<InvalidOperationException>(connect);
        Check.True(!adapter.IsConnected && !native.Properties.Connected, "Lost readiness during watcher install must tear down");
        Check.Equal(0, native.Watchers.Count, "Final readiness failure must dispose newly installed watcher");
        await Check.Done(adapter.ConnectAsync());
        await Check.Done(adapter.DisconnectAsync());
    }

    private static async Task BlueZDisconnectBeforePublish() {
        var native = new Linux.Bluetooth.Device();
        var adapter = new BlueZBluetoothDevice(native, "id", "band");
        using var watcher = native.Watchers.Subscribe.PauseNext();
        var connect = adapter.ConnectAsync();
        await Check.Done(watcher.Entered);
        using var finalRead = native.ReadProperties.PauseNext();
        watcher.Complete();
        await Check.Done(finalRead.Entered);
        native.LoseConnection();
        native.Properties.Connected = true;
        finalRead.Complete();
        await Check.Throws<InvalidOperationException>(connect);
        Check.True(!adapter.IsConnected, "Disconnect callback before publish must invalidate attempt even if properties recover");
        Check.Equal(0, native.Watchers.Count, "Invalidated attempt must dispose watcher");
    }

    private static async Task BlueZStaleWatcher() {
        var native = new Linux.Bluetooth.Device();
        var adapter = new BlueZBluetoothDevice(native, "id", "band");
        var events = 0;
        adapter.Disconnected += (_, _) => events++;
        await Check.Done(adapter.ConnectAsync());
        var stale = native.Watchers.Handlers.Snapshot().Single();
        await Check.Done(adapter.DisconnectAsync());
        await Check.Done(adapter.ConnectAsync());
        stale(new Tmds.DBus.PropertyChanges { Changed = [new("Connected", false)] });
        Check.True(adapter.IsConnected, "Disposed watcher callback must not clear a newer connection");
        Check.Equal(0, events, "Disposed watcher callback must not emit a new-session disconnection");
        native.LoseConnection();
        Check.Equal(1, events, "Current watcher must still report disconnection");
        await Check.Done(adapter.DisconnectAsync());
    }

    private static async Task BlueZCancelWatcherInstall() {
        var native = new Linux.Bluetooth.Device();
        var adapter = new BlueZBluetoothDevice(native, "id", "band");
        using var cancellation = new CancellationTokenSource();
        using var watcher = native.Watchers.Subscribe.PauseNext();
        var connect = adapter.ConnectAsync(cancellation.Token);
        await Check.Done(watcher.Entered);
        cancellation.Cancel();
        await Check.Pending(connect, "Cancellation must await noncancellable watcher install");
        watcher.Complete();
        await Check.Throws<OperationCanceledException>(connect);
        Check.True(!adapter.IsConnected && !native.Properties.Connected, "Canceled watcher install must tear down connection");
        Check.Equal(0, native.Watchers.Count, "Canceled watcher install must dispose late subscription");
    }
#endif
}