using System.Windows.Input;

namespace VirtualTerminal.Controls;

/// <summary>
/// Lightweight relay command implementation for Avalonia multiplexer bindings.
/// </summary>
public sealed class MultiplexerRelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    /// <summary>Initializes a new instance of <see cref="MultiplexerRelayCommand"/>.</summary>
    public MultiplexerRelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <inheritdoc/>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    /// <inheritdoc/>
    public void Execute(object? parameter) => _execute(parameter);

    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged;

    /// <summary>Raises the <see cref="CanExecuteChanged"/> event.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
