namespace MiBandHR.CliChecks;

internal static class Check {
    public static void True(bool condition, string message) {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static async Task Pending(Task task, string message) {
        await Task.WhenAny(task, Task.Delay(40));
        True(!task.IsCompleted, message);
    }

    public static async Task Until(Func<bool> condition, string message) {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!condition()) {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException(message);
            await Task.Delay(5);
        }
    }

    public static async Task ThrowsAsync<TException>(Task task) where TException : Exception {
        try {
            await task;
        } catch (TException) {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}