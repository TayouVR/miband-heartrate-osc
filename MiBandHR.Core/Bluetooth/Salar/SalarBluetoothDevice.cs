using Salar.BluetoothLE;
using Salar.BluetoothLE.Core.Enums;
using Salar.BluetoothLE.Core.Interfaces;
using Salar.BluetoothLE.Core.Models;

namespace MiBandHR.Core.Bluetooth.Salar;

/// <summary>
/// <see cref="IBluetoothDevice"/> backed by Salar.BluetoothLE.
///
/// On Linux this uses Salar's BlueZ backend, which connects to the device
/// <b>without pairing</b> (the behaviour the Mi Band 3 requires), so it avoids
/// the <c>org.bluez.Error.NotConnected</c> failures caused by InTheHand's
/// forced pre-connect pairing.
///
/// Because Salar's <see cref="IBleAdapter.ConnectAsync"/> connects by address
/// and returns the connected device, this wrapper starts out "pending" (only
/// the address is known, from the scan result) and resolves the real
/// <see cref="IBleDevice"/> on the first <see cref="ConnectAsync"/> call.
/// </summary>
public sealed class SalarBluetoothDevice(IBleAdapter adapter, string address, string name) : IBluetoothDevice {
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private IBleDevice? _device;

    // TODO: implement disconnect detection and raise Disconnected for this backend.
#pragma warning disable CS0067
    public event EventHandler? Disconnected;
#pragma warning restore CS0067

    public string Id => address;

    public string Name => _device?.Name ?? name;

    public bool IsConnected => _device?.State == BleDeviceState.Connected;

    public async Task ConnectAsync(CancellationToken cancellationToken = default) {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsConnected) return;
            await DisconnectCoreAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            BluetoothLog.Info($"Connecting to {Name} ({address})...");
            _device = await adapter.ConnectAsync(address, new ConnectionConfig {
                AutoConnect = false,
                ConnectionTimeout = ConnectTimeout
            }).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) {
                await DisconnectCoreAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            BluetoothLog.Success($"Connected to {Name}.");
        } catch (Exception e) {
            if (e is not OperationCanceledException) {
                BluetoothLog.Error($"Connection to {Name} failed: {e.Message}");
            }
            throw;
        } finally {
            _lifecycleGate.Release();
        }
    }

    public async Task<IGattService?> GetPrimaryServiceAsync(Guid uuid) {
        await ConnectAsync();

        var service = await _device!.GetServiceAsync(uuid);
        return service == null ? null : new SalarGattService(service);
    }

    public async Task<IReadOnlyList<IGattService>> GetPrimaryServicesAsync() {
        await ConnectAsync();

        var services = await _device!.GetServicesAsync();
        var result = new List<IGattService>();
        foreach (var service in services) {
            result.Add(new SalarGattService(service));
        }

        return result;
    }

    public async Task DisconnectAsync() {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try {
            await DisconnectCoreAsync().ConfigureAwait(false);
        } finally {
            _lifecycleGate.Release();
        }
    }

    private async Task DisconnectCoreAsync() {
        var device = _device;
        _device = null;
        if (device == null) return;
        try {
            await device.DisconnectAsync().ConfigureAwait(false);
        } catch {
            // Disconnect is best-effort; the link may already be down.
        } finally {
            device.Dispose();
        }
    }
}
