using System.Windows.Input;

namespace VirtualTerminal;

public partial class TerminalControl
{
    /// <summary>
    /// Reusable ICommand implementation that forwards Execute/CanExecute to a <see cref="TerminalControl"/>.
    /// </summary>
    public sealed class TerminalCommand : ICommand
    {
        private readonly Action<TerminalControl> _execute;
        private readonly Func<TerminalControl, bool> _canExecute;

        /// <summary>
        /// Initializes a new command.
        /// </summary>
        /// <param name="name">Display name of the command.</param>
        /// <param name="execute">Action invoked when the command executes.</param>
        /// <param name="canExecute">Predicate that determines whether the command can execute.</param>
        public TerminalCommand(string name, Action<TerminalControl> execute, Func<TerminalControl, bool> canExecute)
        {
            Name = name;
            _execute = execute;
            _canExecute = canExecute;
        }

        /// <summary>
        /// Gets the display name of the command.
        /// </summary>
        public string Name { get; }

        /// <inheritdoc/>
        public bool CanExecute(object? parameter)
            => parameter is TerminalControl control && _canExecute(control);

        /// <inheritdoc/>
        public void Execute(object? parameter)
        {
            if (parameter is TerminalControl control)
                _execute(control);
        }

        /// <inheritdoc/>
        public event EventHandler? CanExecuteChanged;

        /// <summary>
        /// Raises <see cref="CanExecuteChanged"/>.
        /// </summary>
        public void InvalidateCanExecute()
            => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
