namespace MiBandHR.LifecycleChecks;

internal static class Check {
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static void True(bool condition, string message) {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual, string message) {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) {
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
        }
    }

    public static async Task Pending(Task task, string message) {
        await Task.WhenAny(task, Task.Delay(30));
        True(!task.IsCompleted, message);
    }

    public static async Task Throws<T>(Task task) where T : Exception {
        try {
            await task.WaitAsync(Timeout);
        } catch (T) {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }

    public static async Task Until(Func<bool> condition, string message) {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition()) {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException(message);
            await Task.Delay(5);
        }
    }
}