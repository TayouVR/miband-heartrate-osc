# Main Infos for miband-heartrate-osc

## Repository layout

| Project | Purpose | Platform |
|---------|---------|----------|
| `MiBandHR.Core` | Cross-platform library: device protocol, authentication, heart rate monitoring, outputs (file/CSV/OSC), TOML configuration | net10.0, any OS |
| `MiBandHR.Cli` | Console frontend using Spectre.Console | net10.0, any OS with BLE |
| `MiBandHR.WPF` | Original Windows WPF frontend | net9.0-windows |
| `MiBand-Heartrate` | Legacy/empty (only `obj`) | – |
| `AvaloniaApplication1*` | Abandoned experiment (only `obj`/`bin`) | – |

## Bluetooth architecture

All Bluetooth access goes through the platform-neutral abstraction in
`MiBandHR.Core/Bluetooth/`:

* `IBluetoothScanner` / `IBluetoothDevice` / `IGattService` / `IGattCharacteristic`
* Default implementation: `Bluetooth/InTheHand/` wrapping
  [InTheHand.BluetoothLE](https://www.nuget.org/packages/InTheHand.BluetoothLE)
  (Windows, Linux/BlueZ, macOS).

Device classes (`Devices/MiBand2_3_Device.cs`, `Devices/MiBand4_Device.cs`)
only depend on the abstraction, never on `InTheHand.Bluetooth` directly.
Shared protocol logic (authentication handshake, heart rate session,
keep-alive) lives in `Devices/MiBandDeviceBase.cs`. To add support for another
BLE stack, add a new folder under `Bluetooth/` implementing the four
interfaces and pass the resulting `IBluetoothDevice` to the device
constructors.

Frontends that enumerate devices use `IBluetoothScanner` (or the
backwards-compatible `BLE` facade in `MiBandHR.Core/BLE.cs`, which the WPF
project still uses).

## Protocol documentation

The Mi Band authentication handshake is documented extensively in
[`docs/AUTHENTICATION.md`](docs/AUTHENTICATION.md). Update it whenever the
authentication or heart rate protocol code changes.

## Windows-only code

`MiBandHR.WPF/Extras/RegistrySetting.cs` (window position persistence) is
Windows-only and intentionally lives in the WPF project, not in Core.

## Build & test

```bash
dotnet build MiBandHR.Core/MiBandHR.Core.csproj   # library
dotnet build MiBandHR.Cli/MiBandHR.Cli.csproj     # console app
dotnet build MiBandHR.WPF/MiBandHR.WPF.csproj     # Windows-only, needs Windows or EnableWindowsTargeting
```

There is no test project; verification is done by building and by running the
CLI against a real device.
