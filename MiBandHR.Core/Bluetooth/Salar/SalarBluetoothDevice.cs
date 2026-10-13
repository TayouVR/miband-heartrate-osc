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
public sealed class SalarBluetoothDevice : IBluetoothDevice
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private readonly IBleAdapter _adapter;
    private readonly string _address;
    private readonly string _name;
    private IBleDevice? _device;

    public event EventHandler? Disconnected;

    public SalarBluetoothDevice(IBleAdapter adapter, string address, string name)
    {
        _adapter = adapter;
        _address = address;
        _name = name;
    }

    public string Id => _address;

    public string Name => _device?.Name ?? _name;

    public bool IsConnected => _device?.State == BleDeviceState.Connected;

    public async Task ConnectAsync()
    {
        if (IsConnected)
        {
            return;
        }

        BluetoothLog.Info($"Connecting to {Name} ({_address})...");
        try
        {
            _device = await _adapter.ConnectAsync(_address, new ConnectionConfig
            {
                AutoConnect = false,
                ConnectionTimeout = ConnectTimeout
            });
            BluetoothLog.Success($"Connected to {Name}.");
        }
        catch (Exception e)
        {
            BluetoothLog.Error($"Connection to {Name} failed: {e.Message}");
            throw;
        }
    }

    public async Task<IGattService?> GetPrimaryServiceAsync(Guid uuid)
    {
        await ConnectAsync();

        var service = await _device!.GetServiceAsync(uuid);
        return service == null ? null : new SalarGattService(service);
    }

    public async Task<IReadOnlyList<IGattService>> GetPrimaryServicesAsync()
    {
        await ConnectAsync();

        var services = await _device!.GetServicesAsync();
        var result = new List<IGattService>();
        foreach (var service in services)
        {
            result.Add(new SalarGattService(service));
        }
        return result;
    }

    public void Disconnect()
    {
        var device = _device;
        if (device == null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await device.DisconnectAsync();
                device.Dispose();
            }
            catch
            {
                // Disconnect is best-effort; the link may already be down.
            }
        });
    }
}
