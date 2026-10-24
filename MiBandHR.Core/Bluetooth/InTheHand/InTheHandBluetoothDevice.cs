using InTheHand.Bluetooth;

namespace MiBandHR.Core.Bluetooth.InTheHand;

/// <summary>
/// Wraps an <see cref="InTheHand.Bluetooth.BluetoothDevice"/> into the
/// platform-neutral <see cref="IBluetoothDevice"/> abstraction.
/// </summary>
public sealed class InTheHandBluetoothDevice(BluetoothDevice device) : IBluetoothDevice {
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    public string Id => device.Id;

    public string Name => device.Name;

    public bool IsConnected => device.Gatt.IsConnected;

    /// <summary>
    /// Establishes the GATT connection if it is not already open.
    /// On Linux (BlueZ) this also waits for BlueZ to resolve the device's
    /// GATT services before returning.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default) {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            cancellationToken.ThrowIfCancellationRequested();
            if (device.Gatt.IsConnected) return;
            try {
                await device.Gatt.ConnectAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            } catch {
                try {
                    device.Gatt.Disconnect();
                } catch {
                    // Preserve the connection failure or cancellation.
                }
                throw;
            }
        } finally {
            _lifecycleGate.Release();
        }
    }

    public async Task<IGattService?> GetPrimaryServiceAsync(Guid uuid) {
        await ConnectAsync();
        var service = await device.Gatt.GetPrimaryServiceAsync(BluetoothUuid.FromGuid(uuid));
        return service == null ? null : new InTheHandGattService(service);
    }

    public async Task<IReadOnlyList<IGattService>> GetPrimaryServicesAsync() {
        await ConnectAsync();
        var services = await device.Gatt.GetPrimaryServicesAsync();
        return services.Select(s => (IGattService)new InTheHandGattService(s)).ToList();
    }

    public async Task DisconnectAsync() {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try {
            device.Gatt.Disconnect();
        } finally {
            _lifecycleGate.Release();
        }
    }
    // TODO: implement disconnect detection and raise Disconnected for this backend.
#pragma warning disable CS0067
    public event EventHandler? Disconnected;
#pragma warning restore CS0067
}
