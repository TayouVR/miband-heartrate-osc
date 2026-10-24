using MiBandHR.Core.Configuration;
using MiBandHR.Core.Devices;
using MiBandHR.Core.Bluetooth;
using MiBandHR.Core.Extras;
using Spectre.Console;

namespace MiBandHR.Cli;

internal interface ICliConsole {
    void ShowBanner();
    void WriteInfo(string message);
    void WriteError(string message);
    Task ShowScanningAsync(Func<IReadOnlyList<IBluetoothDevice>> getDevices, Task scanTask,
        CancellationToken cancellationToken);
    Task<IBluetoothDevice> SelectBluetoothDeviceAsync(IReadOnlyList<IBluetoothDevice> devices,
        CancellationToken cancellationToken);
    Task<DeviceModel> SelectDeviceModelAsync(CancellationToken cancellationToken);
    Task<string> AskAuthenticationKeyAsync(CancellationToken cancellationToken);
    Task WaitForStopAsync(Device device, CancellationToken cancellationToken);
}

internal interface ICliOutputFactory {
    IEnumerable<IAsyncDisposable> CreateOutputs(Device device, AppConfig config);
}

internal sealed class CliOutputFactory : ICliOutputFactory {
    public IEnumerable<IAsyncDisposable> CreateOutputs(Device device, AppConfig config) {
        if (config.Output.FileEnabled) yield return new DeviceHeartrateFileOutput("heartrate.txt", device);
        if (config.Output.CsvEnabled) yield return new DeviceHeartrateCsvOutput("heartrate.csv", device);
        if (config.Output.OscEnabled) yield return new DeviceHeartrateOscOutput(device);
    }
}

internal sealed class CliConsole : ICliConsole {
    public void ShowBanner() => AnsiConsole.Write(new FigletText("MiBand HR").Color(Color.Blue));

    public void WriteInfo(string message) => AnsiConsole.MarkupLine($"[blue]{Markup.Escape(message)}[/]");

    public void WriteError(string message) => AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");

    public async Task ShowScanningAsync(Func<IReadOnlyList<IBluetoothDevice>> getDevices, Task scanTask,
        CancellationToken cancellationToken) {
        await AnsiConsole.Live(CreateDeviceTable(getDevices()))
            .StartAsync(async context => {
                while (!scanTask.IsCompleted) {
                    cancellationToken.ThrowIfCancellationRequested();
                    context.UpdateTarget(CreateDeviceTable(getDevices()));
                    await Task.Delay(250, cancellationToken);
                }

                context.UpdateTarget(CreateDeviceTable(getDevices()));
            });
    }

    public async Task<IBluetoothDevice> SelectBluetoothDeviceAsync(IReadOnlyList<IBluetoothDevice> devices,
        CancellationToken cancellationToken) {
        AnsiConsole.Write(CreateDeviceTable(devices, showSelectionNumbers: true));
        AnsiConsole.MarkupLine("Select a [green]Bluetooth Device[/] by number:");
        var index = await ReadNumberAsync(devices.Count, cancellationToken);
        return devices[index];
    }

    public async Task<DeviceModel> SelectDeviceModelAsync(CancellationToken cancellationToken) {
        AnsiConsole.MarkupLine("Select [green]Device Model[/]:");
        AnsiConsole.MarkupLine("1. Mi Band 2/3\n2. Mi Band 4/5\n3. Dummy");
        return await ReadNumberAsync(3, cancellationToken) switch {
            0 => DeviceModel.Miband2_3,
            1 => DeviceModel.Miband4_5,
            2 => DeviceModel.Dummy,
            _ => throw new InvalidOperationException("Invalid device model selection.")
        };
    }

    public async Task<string> AskAuthenticationKeyAsync(CancellationToken cancellationToken) {
        AnsiConsole.MarkupLine("Enter [green]Authentication Key[/] (32 hex chars):");
        var key = new System.Text.StringBuilder();
        while (true) {
            var input = await ReadKeyAsync(cancellationToken);
            if (input.Key == ConsoleKey.Enter) {
                AnsiConsole.WriteLine();
                return key.ToString();
            }

            if (input.Key == ConsoleKey.Backspace) {
                if (key.Length > 0) key.Length--;
                continue;
            }

            if (!char.IsControl(input.KeyChar)) {
                key.Append(input.KeyChar);
            }
        }
    }

    public async Task WaitForStopAsync(Device device, CancellationToken cancellationToken) {
        AnsiConsole.MarkupLine("[blue]Monitoring heart rate. Press any key to stop.[/]");
        await AnsiConsole.Live(CreateHeartrateDisplay(device))
            .StartAsync(async context => {
                while (!Console.KeyAvailable) {
                    cancellationToken.ThrowIfCancellationRequested();
                    context.UpdateTarget(CreateHeartrateDisplay(device));
                    await Task.Delay(250, cancellationToken);
                }

                Console.ReadKey(true);
            });
    }

    private static async Task<int> ReadNumberAsync(int max, CancellationToken cancellationToken) {
        var input = new System.Text.StringBuilder();
        while (true) {
            var key = await ReadKeyAsync(cancellationToken);
            if (key.Key == ConsoleKey.Enter) {
                if (int.TryParse(input.ToString(), out var selected) && selected >= 1 && selected <= max) {
                    AnsiConsole.WriteLine();
                    return selected - 1;
                }

                input.Clear();
                AnsiConsole.MarkupLine($"[red]Enter a number from 1 to {max}.[/]");
                continue;
            }

            if (key.Key == ConsoleKey.Backspace) {
                if (input.Length > 0) input.Length--;
                continue;
            }

            if (char.IsDigit(key.KeyChar)) {
                input.Append(key.KeyChar);
                AnsiConsole.Write(key.KeyChar);
            }
        }
    }

    private static async Task<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken) {
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            if (Console.KeyAvailable) return Console.ReadKey(true);
            await Task.Delay(50, cancellationToken);
        }
    }

    private static Table CreateDeviceTable(IEnumerable<IBluetoothDevice> devices, bool showSelectionNumbers = false) {
        var deviceList = devices.ToArray();
        var table = new Table().Border(TableBorder.Rounded);
        if (showSelectionNumbers) table.AddColumn("No.");
        table.AddColumn("Name");
        table.AddColumn("ID");

        for (var index = 0; index < deviceList.Length; index++) {
            var device = deviceList[index];
            if (showSelectionNumbers) {
                table.AddRow((index + 1).ToString(), Markup.Escape(device.Name), Markup.Escape(device.Id));
            } else {
                table.AddRow(Markup.Escape(device.Name), Markup.Escape(device.Id));
            }
        }

        table.Caption("Select a device after scanning completes");
        return table;
    }

    private static Panel CreateHeartrateDisplay(Device device) {
        var content = new Rows(
            new Markup($"[bold red]Current Heart Rate:[/] [white]{device.Heartrate}[/] BPM"),
            new Markup($"[blue]Min:[/] {device.MinHeartrate} [blue]Max:[/] {device.MaxHeartrate}")
        );

        return new Panel(content)
            .Header("Heart Rate Monitor")
            .BorderColor(Color.Red)
            .Padding(1, 1, 1, 1);
    }
}