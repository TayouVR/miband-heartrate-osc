# Avalonia lifecycle checks

Run the executable fake-backed checks from the repository root:

```sh
dotnet run --project tests/MiBandHR.GuiChecks/MiBandHR.GuiChecks.csproj
```

The checks exercise the real `MainViewModel` and `AsyncRelayCommand` using fake
devices and scanners. They cover command re-entry and failure state, serialized
retries, explicit disconnect, awaited replacement and shutdown, monitoring
startup failure, duplicate discovery, and late scan results. The check project
uses the current .NET SDK runtime identifier because the Avalonia application
project is configured as self-contained.