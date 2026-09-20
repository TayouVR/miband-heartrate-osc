# Main Infos for miband-heartrate-osc

## Repository layout

| Project | Purpose | Platform |
|---------|---------|----------|
| `MiBandHR.Core` | Cross-platform library: device protocol, authentication, heart rate monitoring, outputs (file/CSV/OSC), TOML configuration | net10.0, any OS |
| `MiBandHR.Cli` | Console frontend using Spectre.Console | net10.0, any OS with BLE |
| `MiBandHR.Avalonia` | Avalonia GUI frontend (port of the WPF app) | net10.0, Windows/Linux/macOS |
| `MiBandHR.WPF` | Original Windows WPF frontend (legacy) | net9.0-windows |
| `MiBand-Heartrate` | Legacy/empty (only `obj`) | – |
| `AvaloniaApplication1*` | Abandoned experiment (only `obj`/`bin`) | – |

## Bluetooth architecture

All Bluetooth access goes through the platform-neutral abstraction in
`MiBandHR.Core/Bluetooth/`:

* `IBluetoothScanner` / `IBluetoothDevice` / `IGattService` / `IGattCharacteristic`
* Backends (see "Platform-specific backends" below):
  * `Bluetooth/Salar/` wrapping
    [Salar.BluetoothLE](https://www.nuget.org/packages/Salar.BluetoothLE) —
    **default on Linux** (BlueZ, connects without pairing).
  * `Bluetooth/InTheHand/` wrapping
    [InTheHand.BluetoothLE](https://www.nuget.org/packages/InTheHand.BluetoothLE) —
    default on Windows and macOS.

Device classes (`Devices/MiBand2_3_Device.cs`, `Devices/MiBand4_Device.cs`)
only depend on the abstraction, never on a concrete BLE stack directly.
Shared protocol logic (authentication handshake, heart rate session,
keep-alive) lives in `Devices/MiBandDeviceBase.cs`. To add support for another
BLE stack, add a new folder under `Bluetooth/` implementing the four
interfaces and pass the resulting `IBluetoothDevice` to the device
constructors.

Frontends that enumerate devices use `IBluetoothScanner` (or the
backwards-compatible `BLE` facade in `MiBandHR.Core/BLE.cs`, which the WPF
project still uses). The concrete scanner is chosen by
`Bluetooth/BluetoothScannerFactory.cs`: `SalarBluetoothScanner` on Linux,
`InTheHandBluetoothScanner` elsewhere.

### Platform-specific backends

`BluetoothScannerFactory.CreateDefault()` selects the backend per platform:

* **Linux** — `Bluetooth/Salar/`: `SalarBluetoothScanner` +
  `SalarBluetoothDevice` + `SalarGattService` + `SalarGattCharacteristic`.
  This is the **default on Linux** because Salar's BlueZ implementation
  connects to devices **without pairing**, which the Mi Band 3 requires (see
  below).
* **Windows / macOS** — `Bluetooth/InTheHand/`: `InTheHandBluetoothScanner` +
  `InTheHandBluetoothDevice` + `InTheHandGattService` +
  `InTheHandGattCharacteristic`: thin wrappers over the InTheHand API.

### Linux (BlueZ) backend

The default Linux backend is **Salar.BluetoothLE** (`Bluetooth/Salar/`). Its
BlueZ implementation talks to the system BlueZ daemon over D-Bus and, unlike
InTheHand, connects to a device **without pairing** first. Pairing is neither
required nor desired for Mi Band heart rate monitoring, and InTheHand's forced
pre-connect pairing was the root cause of the repeated
`org.bluez.Error.NotConnected` failures seen with Mi Band 3. Salar also avoids
InTheHand's other Linux gaps (no `encrypt-notify` property handling, no
descriptor access, notify-start `InProgress` race) because it drives BlueZ
itself and writes the CCCD directly.

The scanner owns a single `LinuxBleAdapter` and surfaces devices from its
`ScanResultReceived` observable; `SalarBluetoothDevice` starts out "pending"
(only the MAC address is known) and resolves the connected `IBleDevice` on the
first `ConnectAsync()`. Notification subscription is a single call
(`StartNotificationsAsync(Action<byte[]>)`) — no subscribe-then-start ordering
or retry loop is needed.

#### Legacy InTheHand/BlueZ backend (retained as fallback)

The `BlueZ*` classes under `Bluetooth/InTheHand/` are an earlier Linux backend
that drives BlueZ through the native `Linux.Bluetooth` D-Bus proxies extracted
from InTheHand's objects (bridged via reflection in `BlueZNativeInterop`, so
the cross-platform assembly keeps no compile-time dependency on the
Linux-only package). They connect **without pairing** and wait for BlueZ
`Connected` + `ServicesResolved`, and `BlueZGattCharacteristic` subscribes to
the `Value` property before calling BlueZ `StartNotifyAsync` with bounded
retry on `org.bluez.Error.InProgress` (`LinuxBlueZInterop`). They are no longer
selected by default but remain available if the InTheHand backend is preferred
on Linux.

## Protocol documentation

The Mi Band authentication handshake is documented extensively in
[`docs/AUTHENTICATION.md`](docs/AUTHENTICATION.md). Update it whenever the
authentication or heart rate protocol code changes.

## Windows-only code

`MiBandHR.WPF/Extras/RegistrySetting.cs` (window position persistence) is
Windows-only and intentionally lives in the WPF project, not in Core. The
Avalonia frontend persists window geometry in the shared TOML configuration
(`Window` section of `AppConfig`) instead, so it works on every platform.

## GUI frontend (Avalonia)

`MiBandHR.Avalonia` is a port of the WPF app to
[Avalonia UI](https://avaloniaui.net/) (Fluent theme). Structure:

* `Views/` – AXAML windows (`MainWindow`, `ConnectionWindow`,
  `AuthenticationKeyWindow`) plus their code-behind.
* `ViewModels/` – plain `INotifyPropertyChanged` view models and a small
  `RelayCommand`. Unlike WPF there is no `CommandManager`, so commands are
  re-queried explicitly via `RaiseCanExecuteChanged()` when device state
  changes; device callbacks arrive on background threads and are marshalled
  to the UI thread with `Dispatcher.UIThread.Post`.
* Dialogs use `Window.ShowDialog<TResult>`: the connection window returns the
  created `Device` instance, the authentication window returns the entered key.
* Window icons/assets live in `Assets/` (copied from `icons/`).

## Build & test

```bash
dotnet build MiBandHR.Core/MiBandHR.Core.csproj         # library
dotnet build MiBandHR.Cli/MiBandHR.Cli.csproj           # console app
dotnet build MiBandHR.Avalonia/MiBandHR.Avalonia.csproj # cross-platform GUI
dotnet build MiBandHR.WPF/MiBandHR.WPF.csproj           # Windows-only, needs Windows or EnableWindowsTargeting
```

There is no test project; verification is done by building and by running the
CLI/GUI against a real device. On Linux the GUI needs the usual desktop
libraries (fontconfig, freetype, X11/Wayland) present; on NixOS use the flake
(see below).

## Nix flake

The repository root contains a `flake.nix`:

* `nix develop` – dev shell with the .NET 10 SDK and all native runtime
  libraries the Avalonia GUI needs (fontconfig, freetype, X11 libs, ICU)
  exposed via `LD_LIBRARY_PATH`.
* `nix build .#miband-heartrate-gui` (also `.#default`) – packaged GUI app,
  wrapped with its runtime dependencies, including a `.desktop` entry and icon.
* `nix build .#miband-heartrate-cli` – packaged console app.

NuGet dependencies of both packages are pinned in `nuget-deps.json`
(consumed by `buildDotnetModule`). To regenerate it after changing package
references:

```bash
nix shell nixpkgs#nuget-to-json
dotnet restore MiBandHR.Avalonia/MiBandHR.Avalonia.csproj --packages /tmp/nuget/packages
dotnet restore MiBandHR.Cli/MiBandHR.Cli.csproj --packages /tmp/nuget/packages
nuget-to-json /tmp/nuget/packages > nuget-deps.json
```
