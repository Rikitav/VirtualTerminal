using System.Windows.Controls;

namespace VirtualTerminal.Controls;

/// <summary>
/// Specialized terminal multiplexer that provides a tiling split workspace for <see cref="TerminalControl"/> instances,
/// inheriting all generic split, animation, layout persistence, and command binding capabilities from <see cref="MultiplexerControl"/>.
/// </summary>
public class TerminalMultiplexerControl : MultiplexerControl
{
    private Func<TerminalControl>? _terminalControlFactory;

    /// <summary>Factory method to create new <see cref="TerminalControl"/> instances when splitting.</summary>
    public Func<TerminalControl>? TerminalControlFactory
    {
        get => _terminalControlFactory;
        set
        {
            _terminalControlFactory = value;
            NewPaneFactory = value != null ? () => value() : () => new TerminalControl();
        }
    }

    /// <summary>Gets the currently active / focused <see cref="TerminalControl"/>.</summary>
    public TerminalControl? ActiveTerminal => ActiveControl as TerminalControl;

    /// <summary>Initializes a new instance of <see cref="TerminalMultiplexerControl"/>.</summary>
    public TerminalMultiplexerControl()
    {
        TerminalControlFactory = () => new TerminalControl();
    }

    /// <summary>Splits the currently active terminal pane horizontally or vertically.</summary>
    /// <param name="orientation">Vertical for side-by-side panes, Horizontal for stacked panes.</param>
    /// <returns>The newly created <see cref="TerminalControl"/>.</returns>
    public TerminalControl? SplitActivePane(Orientation orientation)
    {
        var term = TerminalControlFactory?.Invoke() ?? new TerminalControl();
        if (SplitActive(term, orientation))
            return term;

        return null;
    }

    /// <summary>Closes the currently active terminal pane.</summary>
    public void CloseActivePane() => CloseActive();
}
