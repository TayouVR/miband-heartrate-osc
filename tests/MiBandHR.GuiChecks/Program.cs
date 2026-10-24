using MiBandHR.Core.Configuration;

namespace MiBandHR.GuiChecks;

internal static class Program {
    private static async Task<int> Main() {
        ConfigurationManager.Initialize(Path.Combine(AppContext.BaseDirectory, "gui-checks-config.toml"));
        var checks = GuiChecks.All().ToArray();
        var failed = 0;

        foreach (var (name, run) in checks) {
            try {
                await run().WaitAsync(TimeSpan.FromSeconds(5));
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