using VirtualTerminal.Session;

namespace VirtualTerminal.TestApp;

/// <summary>
/// Minimal demo TUI application running inside a terminal session.
/// Shows a navigable list inside a box; the selected item can be activated with Enter.
/// </summary>
public class SimpleTuiDemoSession : TerminalUserInterfaceSession
{
    private readonly string[] _items =
    [
        "Show status",
        "Open settings",
        "View logs",
        "Exit"
    ];

    private int _selectedIndex;
    private string _status = "Ready";

    /// <summary>
    /// Renders the demo TUI, including the bordered menu, selection highlight, and status bar.
    /// </summary>
    protected override void Render()
    {
        ClearScreen();

        // Main window
        DrawBox(0, 0, Width, Height - 1, " Simple TUI Demo ");

        // Menu items
        for (int i = 0; i < _items.Length; i++)
        {
            if (i == _selectedIndex)
            {
                SetColor(0, 7); // black on white
                DrawText(2, 2 + i, " " + _items[i].PadRight(Width - 5) + " ");
            }
            else
            {
                SetColor(7, 0); // white on black
                DrawText(2, 2 + i, " " + _items[i].PadRight(Width - 5) + " ");
            }
        }

        ResetColor();

        // Status bar at the bottom
        SetColor(15, 4); // white on blue
        DrawText(0, Height - 1, _status.PadRight(Width)[..Width]);
        ResetColor();
    }

    /// <summary>
    /// Handles keyboard input to navigate the menu or activate the selected item.
    /// </summary>
    /// <param name="key">The key that was pressed.</param>
    protected override void OnKey(ConsoleKey key)
    {
        switch (key)
        {
            case ConsoleKey.UpArrow:
                _selectedIndex = Math.Max(0, _selectedIndex - 1);
                Render();
                break;

            case ConsoleKey.DownArrow:
                _selectedIndex = Math.Min(_items.Length - 1, _selectedIndex + 1);
                Render();
                break;

            case ConsoleKey.Enter:
                Activate();
                break;

            case ConsoleKey.Escape:
            case ConsoleKey.Q:
                Dispose();
                break;
        }
    }

    private void Activate()
    {
        _status = _selectedIndex switch
        {
            0 => "Status: all systems operational",
            1 => "Settings: not implemented yet",
            2 => "Logs: no new messages",
            3 => "Exiting...",
            _ => "Unknown"
        };

        Render();

        if (_selectedIndex == 3)
            Dispose();
    }
}
