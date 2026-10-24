using MiBandHR.BluetoothChecks.Fakes;
using MiBandHR.Core.Bluetooth;
using MiBandHR.Core.Bluetooth.InTheHand;
using MiBandHR.Core.Bluetooth.Salar;
#if WINDOWS
using MiBandHR.Core.Bluetooth.Windows;
#else
using MiBandHR.Core.Bluetooth.BlueZ;
#endif

namespace MiBandHR.BluetoothChecks;

internal static class NotificationChecks {
    private sealed record Fixture(IGattCharacteristic Adapter, NativeOperation Start, NativeOperation Stop,
        Func<int> Handlers, Func<bool> Active, Action<byte[]> Emit, bool DetachableEvents = true);

    public static IEnumerable<(string Name, Func<Task> Run)> All() {
        var factories = new List<(string Name, Func<Fixture> Create)> {
            ("Salar", () => {
                var native = new SalarCharacteristic();
                return new(new SalarGattCharacteristic(native), native.Start, native.Stop,
                    () => native.Callbacks.Count, () => native.NativeActive, native.Emit, false);
            }),
            ("InTheHand", () => {
                var native = new global::InTheHand.Bluetooth.GattCharacteristic();
                return new(new InTheHandGattCharacteristic(native), native.Start, native.Stop,
                    () => native.Handlers.Count, () => native.NativeActive, native.Emit);
            })
        };
#if WINDOWS
        factories.Add(("Windows", () => {
            var native = new Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristic();
            return new(new WindowsGattCharacteristic(native), native.Start, native.Stop,
                () => native.Handlers.Count, () => native.NativeActive, native.Emit);
        }));
#else
        factories.Add(("BlueZ", () => {
            var native = new Linux.Bluetooth.GattCharacteristic();
            return new(new BlueZGattCharacteristic(native, Guid.NewGuid()), native.Start, native.Stop,
                () => native.Watchers.Count, () => native.NativeActive, native.Emit);
        }));
#endif
        foreach (var (name, create) in factories) {
            yield return ($"{name}.notify.deduplicate-stop-restart", () => DeduplicateStopRestart(create()));
            yield return ($"{name}.notify.stop-waits-start", () => StopWaitsStart(create()));
            yield return ($"{name}.notify.start-waits-stop", () => StartWaitsStop(create()));
            yield return ($"{name}.notify.partial-start-failure", () => PartialStartFailure(create()));
            if (create().DetachableEvents) {
                yield return ($"{name}.notify.stop-failure-detaches", () => StopFailureDetaches(create()));
                yield return ($"{name}.notify.subscribe-without-start-cleanup", () => SubscribeWithoutStart(create()));
            }
        }
#if WINDOWS
        yield return ("Windows.notify.failed-cccd-status", WindowsFailedStatus);
#else
        yield return ("BlueZ.notify.watcher-start-failure", BlueZWatcherFailure);
        yield return ("BlueZ.notify.descriptor-prime-failure", BlueZPrimeFailure);
#endif
    }

    private static async Task DeduplicateStopRestart(Fixture f) {
        var received = new List<byte[]>();
        f.Adapter.CharacteristicValueChanged += (_, args) => received.Add(args.Value);
        Parallel.For(0, 20, _ => f.Adapter.SubscribeToValueChanges());
        using var point = f.Start.PauseNext();
        var first = f.Adapter.StartNotificationsAsync();
        await Check.Done(point.Entered);
        Check.Equal(1, f.Handlers(), "Subscribe before native start, without duplicates");
        var second = f.Adapter.StartNotificationsAsync();
        await Check.Pending(second, "Second notification start must wait for first");
        Check.Equal(1, f.Start.Calls, "Only one native start in flight");
        point.Complete();
        await Check.Done(Task.WhenAll(first, second));
        Check.Equal(1, f.Start.Calls, "Repeated notification start must be idempotent");
        byte[] payload = [1, 2, 3];
        f.Emit(payload);
        Check.Equal(1, received.Count, "One forwarded event per native value");
        Check.True(payload.SequenceEqual(received[0]), "Payload must survive forwarding");
        await Check.Done(f.Adapter.StopNotificationsAsync());
        Check.Equal(0, f.Handlers(), "Stop removes native handler or callback");
        Check.True(!f.Active(), "Stop disables native notifications");
        f.Emit(payload);
        Check.Equal(1, received.Count, "No values forwarded after stop");
        await Check.Done(f.Adapter.StartNotificationsAsync());
        Check.Equal(1, f.Handlers(), "Restart installs one subscription without explicit Subscribe call");
        f.Emit(payload);
        Check.Equal(2, received.Count, "Restart forwards exactly once");
        await Check.Done(f.Adapter.StopNotificationsAsync());
        await Check.Done(f.Adapter.StopNotificationsAsync());
        Check.Equal(0, f.Handlers(), "Repeated stop leaves no subscription");
    }

    private static async Task StopWaitsStart(Fixture f) {
        using var point = f.Start.PauseNext();
        var start = f.Adapter.StartNotificationsAsync();
        await Check.Done(point.Entered);
        var stop = f.Adapter.StopNotificationsAsync();
        await Check.Pending(stop, "Stop must wait for pending native start");
        Check.Equal(0, f.Stop.Calls, "No native stop racing pending native start");
        point.Complete();
        await Check.Done(Task.WhenAll(start, stop));
        Check.Equal(0, f.Handlers(), "Queued stop removes subscription");
        Check.True(!f.Active(), "Late native start cannot leave notifications enabled");
    }

    private static async Task StartWaitsStop(Fixture f) {
        await Check.Done(f.Adapter.StartNotificationsAsync());
        using var point = f.Stop.PauseNext();
        var stop = f.Adapter.StopNotificationsAsync();
        await Check.Done(point.Entered);
        var restart = f.Adapter.StartNotificationsAsync();
        await Check.Pending(restart, "Restart must await pending native stop");
        Check.Equal(1, f.Start.Calls, "No native restart racing native stop");
        point.Complete();
        await Check.Done(Task.WhenAll(stop, restart));
        Check.True(f.Active(), "Queued restart must enable notifications");
        Check.Equal(1, f.Handlers(), "Queued restart must retain one subscription");
        await Check.Done(f.Adapter.StopNotificationsAsync());
    }

    private static async Task PartialStartFailure(Fixture f) {
        f.Start.FailNext();
        using var point = f.Stop.PauseNext();
        var start = f.Adapter.StartNotificationsAsync();
        await Check.Done(point.Entered);
        await Check.Pending(start, "Failed startup must await cleanup of partial native startup");
        point.Complete();
        await Check.Throws<NativeFailureException>(start);
        Check.Equal(1, f.Stop.Calls, "Partial native startup must be stopped");
        Check.Equal(0, f.Handlers(), "Failed startup must remove subscriptions");
        Check.True(!f.Active(), "Failed startup must disable partially enabled native notifications");
        await Check.Done(f.Adapter.StartNotificationsAsync());
        Check.Equal(1, f.Handlers(), "Retry after failure installs exactly one subscription");
        await Check.Done(f.Adapter.StopNotificationsAsync());
    }

    private static async Task StopFailureDetaches(Fixture f) {
        var received = 0;
        f.Adapter.CharacteristicValueChanged += (_, _) => received++;
        await Check.Done(f.Adapter.StartNotificationsAsync());
        f.Stop.FailNext();
        await Check.StopAllowingNativeFailure(f.Adapter.StopNotificationsAsync());
        Check.Equal(0, f.Handlers(), "Native stop failure must not leak local subscription");
        f.Emit([7]);
        Check.Equal(0, received, "No forwarded values after failed stop");
        await Check.Done(f.Adapter.StartNotificationsAsync());
        Check.Equal(2, f.Start.Calls, "Failed stop must reset startup flag");
        Check.Equal(1, f.Handlers(), "Restart after failed stop must not duplicate handler");
        await Check.Done(f.Adapter.StopNotificationsAsync());
    }

    private static async Task SubscribeWithoutStart(Fixture f) {
        f.Adapter.SubscribeToValueChanges();
        f.Adapter.SubscribeToValueChanges();
        await Check.Done(f.Adapter.StopNotificationsAsync());
        Check.Equal(0, f.Handlers(), "Stop must remove handler even when native start never ran");
    }

#if WINDOWS
    private static async Task WindowsFailedStatus() {
        var native = new Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristic {
            NextStartStatus = Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Unreachable
        };
        var adapter = new WindowsGattCharacteristic(native);
        await Check.Throws<InvalidOperationException>(adapter.StartNotificationsAsync());
        Check.Equal(0, native.Handlers.Count, "Unsuccessful CCCD status must detach handler");
        Check.True(!native.NativeActive, "Unsuccessful CCCD status must attempt disable");
        await Check.Done(adapter.StartNotificationsAsync());
        await Check.Done(adapter.StopNotificationsAsync());
    }
#else
    private static async Task BlueZWatcherFailure() {
        var native = new Linux.Bluetooth.GattCharacteristic();
        var adapter = new BlueZGattCharacteristic(native, Guid.NewGuid());
        native.Watchers.Subscribe.FailNext();
        await Check.Throws<NativeFailureException>(adapter.StartNotificationsAsync());
        Check.Equal(0, native.Start.Calls, "Failed watcher must not enable native notifications");
        Check.Equal(0, native.Watchers.Count, "Failed watcher leaves no subscription");
        await Check.Done(adapter.StartNotificationsAsync());
        await Check.Done(adapter.StopNotificationsAsync());
    }

    private static async Task BlueZPrimeFailure() {
        var native = new Linux.Bluetooth.GattCharacteristic();
        var adapter = new BlueZGattCharacteristic(native, Guid.NewGuid());
        Tmds.DBus.Connection.System.ReadDescriptor.FailNext();
        await Check.Throws<NativeFailureException>(adapter.StartNotificationsAsync());
        Check.Equal(1, native.Start.Calls, "Priming failure must occur after native enable");
        Check.Equal(1, native.Stop.Calls, "Priming failure must stop enabled notifications");
        Check.Equal(0, native.Watchers.Count, "Priming failure must dispose property watcher");
        Check.True(!native.NativeActive, "Priming failure must disable native notifications");
        await Check.Done(adapter.StartNotificationsAsync());
        await Check.Done(adapter.StopNotificationsAsync());
    }
#endif
}