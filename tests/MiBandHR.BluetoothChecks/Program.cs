namespace MiBandHR.BluetoothChecks;

internal static class Program {
    private static async Task<int> Main(string[] args) {
        var checks = DeviceChecks.All().Concat(NotificationChecks.All()).ToArray();
        if (args.Contains("--list")) {
            foreach (var (name, _) in checks) Console.WriteLine(name);
            return 0;
        }
        if (args.Length > 1) {
            Console.Error.WriteLine("Usage: MiBandHR.BluetoothChecks [--list | name-substring]");
            return 2;
        }
        var selected = checks.Where(c => args.Length == 0 || c.Name.Contains(args[0], StringComparison.OrdinalIgnoreCase)).ToArray();
        if (selected.Length == 0) { Console.Error.WriteLine("No matching checks"); return 2; }
        var failed = 0;
        foreach (var (name, run) in selected) {
            try {
                await Check.Done(run());
                Console.WriteLine($"PASS {name}");
            } catch (Exception exception) {
                failed++;
                Console.Error.WriteLine($"FAIL {name}: {exception}");
            }
        }
        Console.WriteLine($"{selected.Length - failed}/{selected.Length} checks passed");
        return failed == 0 ? 0 : 1;
    }
}