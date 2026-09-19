using System;
using System.Windows.Input;

namespace MiBandHR.Avalonia.ViewModels;

/// <summary>
/// Minimal ICommand implementation. Unlike the WPF version there is no
/// CommandManager in Avalonia, so CanExecute is re-evaluated when
/// <see cref="RaiseCanExecuteChanged"/> is called explicitly.
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
