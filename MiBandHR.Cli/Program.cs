using System.CommandLine;
using System.CommandLine.Parsing;

namespace MiBandHR.Cli;

class Program {
    static async Task Main(string[] args) {
        Option<FileInfo> configOption = new("--config") {
            Aliases = { "-c" },
            Description = "Path to configuration file",
        };

        var rootCommand = new RootCommand("MiBand Heart Rate Monitor CLI");
        rootCommand.Add(configOption);

        ParseResult parseResult = rootCommand.Parse(args);
        string? configFile = null;
        if (parseResult.GetValue(configOption) is FileInfo parsedFile) {
            configFile = parsedFile.FullName;
        }

        foreach (ParseError parseError in parseResult.Errors) {
            Console.Error.WriteLine(parseError.Message);
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) => {
            eventArgs.Cancel = true;
            try {
                cancellation.Cancel();
            } catch (AggregateException exception) {
                Console.Error.WriteLine($"Cancellation callback failed: {exception.Message}");
            }
        };
        Console.CancelKeyPress += cancelHandler;

        try {
            var app = new App(configFile);
            await app.RunAsync(cancellation.Token);
        } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) {
            Console.WriteLine("Operation cancelled.");
        } catch (Exception exception) {
            Console.Error.WriteLine($"CLI failed: {exception.Message}");
            Environment.ExitCode = 1;
        } finally {
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}
