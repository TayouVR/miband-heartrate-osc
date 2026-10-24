namespace MiBandHR.GuiChecks;

internal static class Check {
    public static void True(bool condition, string message) {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static async Task Until(Func<bool> condition, string message) {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
        while (!condition()) {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException(message);
            await Task.Delay(5);
        }
    }

    public static async Task Pending(Task task, string message) {
        await Task.WhenAny(task, Task.Delay(30));
        True(!task.IsCompleted, message);
    }
}