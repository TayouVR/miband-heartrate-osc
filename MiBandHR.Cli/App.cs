using MiBandHR.Core;
using MiBandHR.Core.Configuration;
using MiBandHR.Core.Devices;
using InTheHand.Bluetooth;
using Spectre.Console;
using System.Collections.Concurrent;
using MiBand_Heartrate.Extras;

namespace MiBandHR.Cli;

public class App {
    private readonly string? _configPath;
    private readonly ConcurrentDictionary<string, BluetoothDevice> _discoveredDevices = new();
    private Device? _device;

    public App(string? configPath) {
        _configPath = configPath;
    }

    public async Task RunAsync() {
        ConfigurationManager.Initialize(_configPath);
        AnsiConsole.Write(new FigletText("MiBand HR").Color(Color.Blue));

        var ble = new BLE();
        ble.DeviceFound += (s, d) => {
            
            _discoveredDevices[d.Id] = d;
        };

        AnsiConsole.MarkupLine("[yellow]Scanning for devices...[/]");
        var scanTask = ble.ScanForDevicesAsync();

        BluetoothDevice? selectedBluetoothDevice = null;

        await AnsiConsole.Live(CreateDeviceTable())
            .StartAsync(async ctx => {
                while (selectedBluetoothDevice == null) {
                    ctx.UpdateTarget(CreateDeviceTable());
                    
                    if (Console.KeyAvailable) {
                         var key = Console.ReadKey(true);
                         if (key.Key == ConsoleKey.Enter && _discoveredDevices.Count > 0) {
                             break;
                         }
                    }
                    await Task.Delay(500);
                }
            });

        if (_discoveredDevices.Count == 0) {
            AnsiConsole.MarkupLine("[red]No devices found.[/]");
            return;
        }

        var deviceList = _discoveredDevices.Values.ToList();
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<BluetoothDevice>()
                .Title("Select a [green]Bluetooth Device[/]:")
                .PageSize(10)
                .AddChoices(deviceList)
                .UseConverter(d => Markup.Escape($"{d.Name} ({d.Id})"))
        );

        selectedBluetoothDevice = choice;

        var modelChoice = AnsiConsole.Prompt(
            new SelectionPrompt<DeviceModel>()
                .Title("Select [green]Device Model[/]:")
                .AddChoices(DeviceModel.MIBAND_2_3, DeviceModel.MIBAND_4, DeviceModel.DUMMY)
        );

        switch (modelChoice) {
            case DeviceModel.MIBAND_2_3:
                _device = new MiBand2_3_Device(selectedBluetoothDevice);
                break;
            case DeviceModel.MIBAND_4:
                var authKey = AnsiConsole.Ask<string>("Enter [green]Authentication Key[/] (32 hex chars):");
                _device = new MiBand4_Device(selectedBluetoothDevice, authKey);
                break;
            case DeviceModel.DUMMY:
                _device = new Dummy_Device();
                break;
        }

        if (_device == null) return;

        AnsiConsole.MarkupLine($"[yellow]Connecting to {_device.Name}...[/]");
        _device.Connect();

        // Wait for auth
        while (_device.Status != DeviceStatus.ONLINE_AUTH && _device.Status != DeviceStatus.OFFLINE) {
            await Task.Delay(100);
        }

        if (_device.Status == DeviceStatus.OFFLINE) {
            AnsiConsole.MarkupLine("[red]Failed to connect or authenticate.[/]");
            return;
        }

        AnsiConsole.MarkupLine("[green]Connected and Authenticated![/]");

        var continuous = ConfigurationManager.Instance.CurrentConfig.Monitoring.ContinuousMode;
        _device.StartHeartrateMonitor(continuous);

        // Setup outputs as in WPF version
        DeviceHeartrateFileOutput? fileOutput = null;
        DeviceHeartrateCSVOutput? csvOutput = null;
        DeviceHeartrateOscOutput? oscOutput = null;

        if (ConfigurationManager.Instance.CurrentConfig.Output.FileEnabled)
            fileOutput = new DeviceHeartrateFileOutput("heartrate.txt", _device);
        if (ConfigurationManager.Instance.CurrentConfig.Output.CsvEnabled)
            csvOutput = new DeviceHeartrateCSVOutput("heartrate.csv", _device);
        if (ConfigurationManager.Instance.CurrentConfig.Output.OscEnabled)
            oscOutput = new DeviceHeartrateOscOutput(_device);

        AnsiConsole.MarkupLine("[blue]Monitoring heart rate. Press any key to stop.[/]");

        await AnsiConsole.Live(CreateHeartrateDisplay())
            .StartAsync(async ctx => {
                while (!Console.KeyAvailable) {
                    ctx.UpdateTarget(CreateHeartrateDisplay());
                    await Task.Delay(500);
                }
            });

        _device.Disconnect();
        AnsiConsole.MarkupLine("[yellow]Disconnected.[/]");
    }

    private Table CreateDeviceTable() {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Name");
        table.AddColumn("ID");

        foreach (var device in _discoveredDevices.Values) {
            table.AddRow(Markup.Escape(device.Name), Markup.Escape(device.Id));
        }

        table.Caption("Press [yellow]ENTER[/] to select a device");
        return table;
    }

    private Panel CreateHeartrateDisplay() {
        var hr = _device?.Heartrate ?? 0;
        var min = _device?.MinHeartrate ?? 0;
        var max = _device?.MaxHeartrate ?? 0;
        
        var content = new Rows(
            new Markup($"[bold red]Current Heart Rate:[/] [white]{hr}[/] BPM"),
            new Markup($"[blue]Min:[/] {min} [blue]Max:[/] {max}")
        );

        return new Panel(content)
            .Header("Heart Rate Monitor")
            .BorderColor(Color.Red)
            .Padding(1, 1, 1, 1);
    }
}