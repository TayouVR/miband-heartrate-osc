# Bluetooth adapter lifecycle checks

Dependency-free .NET 10 executable: no test framework, package references,
Bluetooth daemon, hardware, or production test seams. The project links the
actual production device, service, characteristic, contract, logging, and BlueZ
interop sources and compiles them against the small native-shaped APIs in
`Fakes/`. It does not replace the adapters under test.

From the repository root:

```sh
dotnet run --project tests/MiBandHR.BluetoothChecks/MiBandHR.BluetoothChecks.csproj
dotnet run --project tests/MiBandHR.BluetoothChecks/MiBandHR.BluetoothChecks.csproj -p:BluetoothChecksWindows=true
```

The default variant includes BlueZ, InTheHand, and Salar (48 checks). The
`BluetoothChecksWindows=true` variant compiles the production `WINDOWS` branches
against WinRT-shaped fakes instead of BlueZ (37 checks). Set the property back to
`false`, or omit it, to rebuild the default variant. Run these variants
sequentially because they share build output paths; do not use `--no-build`
when switching variants.

Append `-- --list` to list checks, or `-- <name-substring>` to select checks, for
example `-- BlueZ.device`. A failure exits with code 1; invalid selection exits
with code 2. Expected injected failures can print Bluetooth error log messages
even in a successful run.

## Coverage

- All four device adapters: concurrent connect deduplication; disconnect waiting
  for native connect; cancellation before entry, while queued, and during a
  noncancellable native connect; awaited cancellation teardown; failure recovery.
- Salar: awaited disconnect, disposal and field clearing even when disconnect
  fails, repeat disconnect, and reconnect waiting for old-device disposal.
- BlueZ: `Connected && ServicesResolved`, including already-connected and cached
  connection paths; one property watcher; awaited disconnect; watcher failure and
  cancellation cleanup; final readiness validation after watcher installation;
  disconnect-before-publication and stale callbacks from a previous session.
- All four characteristic adapters: subscribe/start deduplication, single value
  forwarding, start/stop serialization in both directions, cleanup of partially
  enabled native notifications, stop, and restart without an extra subscribe call.
- InTheHand, BlueZ, Windows: native event/watcher removal even if native stop
  fails; cleanup after subscribing without starting.
- BlueZ: failed watcher installation and descriptor priming after native enable.
- Windows: unsuccessful CCCD status cleanup as well as asynchronous failures.

`CompletionPoint` gates native operations before completion; `NativeOperation`
counts calls and injects failures; handler/watcher collections count live
subscriptions and removals. Each await has a five-second bound. Negative
completion assertions allow a short scheduling window rather than waiting for
hardware timing.

## Limits

These are source-level lifecycle regression checks, not binary/API compatibility
tests against the real Bluetooth libraries. In particular, Windows fakes use
tasks plus an `AsTask` extension, not real WinRT operations. Real platform builds
and hardware notification delivery still need separate validation.

Salar owns callbacks internally and exposes no independent event-unsubscribe
operation. Its checks verify callback release when native stop succeeds,
including partial-start failure cleanup; they do not claim callback release when
the native stop itself fails. Service methods are linked for compilation but are
not a separate service-discovery test suite. Checks target the cancellation-aware
`ConnectAsync` and awaitable `DisconnectAsync` contract and were validated against
the updated production sources, not a pre-fix runtime baseline.