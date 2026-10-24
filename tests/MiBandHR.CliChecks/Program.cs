namespace MiBandHR.CliChecks;

internal static class Program {
    private static async Task<int> Main() {
        var checks = new (string Name, Func<Task> Run)[] {
            ("success awaits monitoring and disposes outputs/device", CliChecks.SuccessCleansUp),
            ("duplicate and late discovery events are ignored", CliChecks.DuplicateAndLateDiscovery),
            ("passive discovery includes unrelated devices and only constructs the selection", CliChecks.PassiveDiscoveryIncludesUnrelatedDevices),
            ("cancellation waits for non-cancellable scan", CliChecks.CancellationWaitsForScan),
            ("cancellation during connect cleans up without retry", CliChecks.CancellationDuringConnect),
            ("cancellation during monitor startup cleans up", CliChecks.CancellationDuringMonitoringStart),
            ("cancellation while monitoring cleans up", CliChecks.CancellationWhileMonitoring),
            ("connect failure is not retried and disposes device", CliChecks.ConnectionFailureCleansUp),
            ("monitor startup failure disposes device", CliChecks.MonitoringFailureCleansUp),
            ("partial output creation and cleanup failures do not skip cleanup", CliChecks.OutputFailureStillCleansEverything),
            ("empty and failed scans detach subscriptions", CliChecks.ScanExitPathsDetach)
        };

        var failed = 0;
        foreach (var (name, run) in checks) {
            try {
                await run().WaitAsync(TimeSpan.FromSeconds(6));
                Console.WriteLine($"PASS {name}");
            } catch (Exception exception) {
                failed++;
                Console.Error.WriteLine($"FAIL {name}: {exception}");
            }
        }

        Console.WriteLine($"{checks.Length - failed}/{checks.Length} checks passed");
        return failed == 0 ? 0 : 1;
    }
}