namespace MiBandHR.BluetoothChecks;

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

    public static Task Done(Task task) => task.WaitAsync(Timeout);

    public static async Task Pending(Task task, string message) {
        // Give an incorrectly ungated asynchronous continuation a chance to run.
        await Task.WhenAny(task, Task.Delay(30));
        True(!task.IsCompleted, message);
    }

    public static async Task Throws<T>(Task task) where T : Exception {
        try { await Done(task); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }

    public static async Task StopAllowingNativeFailure(Task task) {
        try { await Done(task); }
        catch (Fakes.NativeFailureException) { }
    }
}