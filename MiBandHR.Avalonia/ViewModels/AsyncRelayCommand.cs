using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace MiBandHR.Avalonia.ViewModels;

public sealed class AsyncRelayCommand : ICommand, INotifyPropertyChanged {
    private readonly Func<object?, CancellationToken, Task> _execute;
    private readonly Predicate<object?>? _canExecute;
    private readonly object _sync = new();
    private CancellationTokenSource? _executionCancellation;
    private Task? _executionTask;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<object?, CancellationToken, Task> execute,
        Predicate<object?>? canExecute = null) {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<Exception>? ExecutionFailed;

    public bool IsExecuting {
        get {
            lock (_sync) return _isExecuting;
        }
    }

    public Task? ExecutionTask {
        get {
            lock (_sync) return _executionTask;
        }
    }

    public bool CanExecute(object? parameter) {
        lock (_sync) {
            return !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);
        }
    }

    public Task ExecuteAsync(object? parameter = null, CancellationToken cancellationToken = default) {
        CancellationTokenSource executionCancellation;
        lock (_sync) {
            if (_isExecuting || !(_canExecute?.Invoke(parameter) ?? true)) return Task.CompletedTask;
            _isExecuting = true;
            executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _executionCancellation = executionCancellation;
        }

        NotifyExecutionStateChanged();
        var executionTask = ExecuteCoreAsync(parameter, executionCancellation);
        lock (_sync) _executionTask = executionTask;
        return executionTask;
    }

    public void Cancel() {
        CancellationTokenSource? cancellation;
        lock (_sync) cancellation = _executionCancellation;
        try {
            cancellation?.Cancel();
        } catch (ObjectDisposedException) {
            // The command completed after its cancellation source was captured.
        }
    }

    private async Task ExecuteCoreAsync(object? parameter, CancellationTokenSource executionCancellation) {
        try {
            await _execute(parameter, executionCancellation.Token);
        } catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested) {
            // Caller-requested cancellation is an expected command outcome.
        } catch (Exception exception) {
            try {
                ExecutionFailed?.Invoke(exception);
            } catch {
                // An exception observer must not become an unobserved command failure.
            }
        } finally {
            lock (_sync) {
                if (ReferenceEquals(_executionCancellation, executionCancellation)) _executionCancellation = null;
                _isExecuting = false;
            }
            executionCancellation.Dispose();
            NotifyExecutionStateChanged();
        }
    }

    public void Execute(object? parameter) => _ = ExecuteAsync(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private void NotifyExecutionStateChanged() {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExecuting)));
        RaiseCanExecuteChanged();
    }
}