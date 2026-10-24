using System.Collections.Concurrent;

namespace MiBandHR.BluetoothChecks.Fakes;

public sealed class CompletionPoint : IDisposable {
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Entered => _entered.Task;
    public void Complete() => _completion.TrySetResult();
    public void Fail() => _completion.TrySetException(new NativeFailureException());
    public void Dispose() => Complete();

    internal Task Invoke() {
        _entered.TrySetResult();
        return _completion.Task;
    }
}

public sealed class NativeFailureException : Exception {
    public NativeFailureException() : base("Injected native failure") { }
}

public sealed class NativeOperation {
    private readonly ConcurrentQueue<CompletionPoint> _points = new();
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);

    public CompletionPoint PauseNext() {
        var point = new CompletionPoint();
        _points.Enqueue(point);
        return point;
    }

    public void FailNext() => PauseNext().Fail();

    public Task Invoke() {
        Interlocked.Increment(ref _calls);
        return _points.TryDequeue(out var point) ? point.Invoke() : Task.CompletedTask;
    }
}

public sealed class Handlers<T> where T : Delegate {
    private readonly List<T> _handlers = [];
    public int Count { get { lock (_handlers) return _handlers.Count; } }
    public int Adds { get; private set; }
    public int Removes { get; private set; }

    public void Add(T handler) {
        lock (_handlers) {
            _handlers.Add(handler);
            Adds++;
        }
    }

    public void Remove(T handler) {
        lock (_handlers) {
            if (_handlers.Remove(handler)) Removes++;
        }
    }

    public T[] Snapshot() { lock (_handlers) return _handlers.ToArray(); }
    public void Clear() { lock (_handlers) { Removes += _handlers.Count; _handlers.Clear(); } }
}

public sealed class Watchers<T> {
    public NativeOperation Subscribe { get; } = new();
    public Handlers<Action<T>> Handlers { get; } = new();
    public int Count => Handlers.Count;
    public int Disposed => Handlers.Removes;

    public async Task<IDisposable> Watch(Action<T> handler) {
        await Subscribe.Invoke();
        Handlers.Add(handler);
        return new Subscription(() => Handlers.Remove(handler));
    }

    public void Raise(T value) {
        foreach (var handler in Handlers.Snapshot()) handler(value);
    }

    private sealed class Subscription(Action dispose) : IDisposable {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}