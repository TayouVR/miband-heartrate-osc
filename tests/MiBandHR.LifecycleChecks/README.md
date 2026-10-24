# Device lifecycle checks

Run the dependency-free fake-backed regression harness from the repository root:

```sh
dotnet run --project tests/MiBandHR.LifecycleChecks/MiBandHR.LifecycleChecks.csproj
```

The harness exercises production `MiBandDeviceBase`, both authentication protocol
completion and error paths, cancellation/cleanup, monitoring startup/stop,
keep-alive failure observation, repeated reconnect handler counts, and dummy
worker disposal. The fake BLE device and GATT characteristics expose controlled
completion points and subscription/write counters; no radio or Bluetooth daemon
is required. The project is also registered in `MiBand-Heartrate.slnx`.