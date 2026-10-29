using System.Text;
using VirtualTerminal.Interfaces;

namespace VirtualTerminal.Session;

/// <summary>
/// Base class for hosting a TUI application directly inside an <see cref="ITerminalSession"/>.
/// The application renders using VT/ANSI escape sequences (alternate screen buffer, colors,
/// box-drawing characters) and receives keyboard input through <see cref="Write(ReadOnlySpan{byte})"/>.
/// </summary>
public abstract class TerminalUserInterfaceSession : TerminalSession
{
    private readonly StringBuilder _inputBuffer = new StringBuilder();

    /// <summary>Current terminal width in columns.</summary>
    protected int Width { get; private set; } = 80;

    /// <summary>Current terminal height in rows.</summary>
    protected int Height { get; private set; } = 24;

    /// <summary>Feeds raw output bytes into the terminal decoder and notifies the UI.</summary>
    protected void FeedOutput(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        Decoder.Write(data);
        NotifyBufferUpdated();
    }

    /// <summary>Feeds a text string into the terminal decoder and notifies the UI.</summary>
    protected void FeedOutput(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        byte[] bytes = OutputEncoding.GetBytes(text);
        FeedOutput(bytes.AsSpan());
    }

    /// <summary>Enters the alternate screen buffer and triggers the first render.</summary>
    public virtual void Run()
    {
        ThrowIfDisposed();
        // Enable alternate screen buffer, disable line wrapping, hide cursor.
        FeedOutput("\x1b[?1049h\x1b[?7l\x1b[?25l\x1b[2J\x1b[H");
        Render();
    }

    /// <inheritdoc />
    public override void Resize(ushort columns, ushort rows, bool pushScrollback = false)
    {
        base.Resize(columns, rows, false);
        Width = columns;
        Height = rows;
        Render();
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();
        if (data.IsEmpty)
            return;

        _inputBuffer.Append(InputEncoding.GetString(data));

        while (TryParseKey(out ConsoleKey key))
        {
            OnKey(key);
        }
    }

    /// <summary>Called to redraw the whole TUI surface.</summary>
    protected abstract void Render();

    /// <summary>Called when a TUI key is pressed.</summary>
    /// <param name="key">The pressed key.</param>
    protected abstract void OnKey(ConsoleKey key);

    /// <summary>Clears the screen and moves the cursor to the top-left corner.</summary>
    protected void ClearScreen()
        => FeedOutput("\x1b[2J\x1b[H");

    /// <summary>Moves the cursor to the specified cell.</summary>
    protected void SetCursor(int column, int row)
        => FeedOutput($"\x1b[{Math.Clamp(row, 0, Height - 1) + 1};{Math.Clamp(column, 0, Width - 1) + 1}H");

    /// <summary>Draws text at the specified cell, clamped to the visible area.</summary>
    protected void DrawText(int column, int row, string text)
    {
        if (row < 0 || row >= Height)
            return;

        int available = Math.Max(0, Width - column);
        if (available <= 0)
            return;

        string clipped = text.Length > available ? text[..available] : text;
        SetCursor(column, row);
        FeedOutput(clipped);
    }

    /// <summary>Sets foreground and background colors using the 256-color palette.</summary>
    protected void SetColor(byte foreground, byte background)
        => FeedOutput($"\x1b[38;5;{foreground}m\x1b[48;5;{background}m");

    /// <summary>Resets all attributes to defaults.</summary>
    protected void ResetColor()
        => FeedOutput("\x1b[0m");

    /// <summary>
    /// Draws a box using box-drawing characters. The border is drawn inside the given rectangle.
    /// </summary>
    protected void DrawBox(int x, int y, int width, int height, string? title = null)
    {
        if (width < 2 || height < 2)
            return;

        int right = x + width - 1;
        int bottom = y + height - 1;

        // Top border
        SetCursor(x, y);
        FeedOutput('┌' + new string('─', width - 2) + '┐');

        // Sides
        for (int row = y + 1; row < bottom; row++)
        {
            DrawText(x, row, "│");
            DrawText(right, row, "│");
        }

        // Bottom border
        SetCursor(x, bottom);
        FeedOutput('└' + new string('─', width - 2) + '┘');

        // Title
        if (!string.IsNullOrEmpty(title) && title.Length < width - 2)
        {
            int titleX = x + (width - title.Length) / 2;
            DrawText(titleX, y, title);
        }
    }

    /// <summary>Leaves the alternate screen buffer and shows the cursor again.</summary>
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        FeedOutput("\x1b[?25h\x1b[?7h\x1b[?1049l");
    }

    private bool TryParseKey(out ConsoleKey key)
    {
        key = ConsoleKey.None;
        if (_inputBuffer.Length == 0)
            return false;

        char first = _inputBuffer[0];

        // ESC sequences
        if (first == '\x1b' && _inputBuffer.Length >= 2)
        {
            char second = _inputBuffer[1];

            if (second == '[' && _inputBuffer.Length >= 3)
            {
                char third = _inputBuffer[2];
                key = third switch
                {
                    'A' => ConsoleKey.UpArrow,
                    'B' => ConsoleKey.DownArrow,
                    'C' => ConsoleKey.RightArrow,
                    'D' => ConsoleKey.LeftArrow,
                    'H' => ConsoleKey.Home,
                    'F' => ConsoleKey.End,
                    _ => ConsoleKey.None
                };

                // Also handle CSI ~ sequences (Home/End/Delete/Insert/PageUp/PageDown).
                if (key == ConsoleKey.None && _inputBuffer.Length >= 4 && char.IsDigit(third))
                {
                    int end = _inputBuffer.ToString().IndexOf('~', 2);
                    if (end > 0)
                    {
                        string param = _inputBuffer.ToString(2, end - 2);
                        key = param switch
                        {
                            "1" or "7" => ConsoleKey.Home,
                            "4" or "8" => ConsoleKey.End,
                            "3" => ConsoleKey.Delete,
                            "2" => ConsoleKey.Insert,
                            "5" => ConsoleKey.PageUp,
                            "6" => ConsoleKey.PageDown,
                            _ => ConsoleKey.None
                        };

                        _inputBuffer.Remove(0, end + 1);
                        return key != ConsoleKey.None;
                    }
                }

                if (key != ConsoleKey.None)
                {
                    _inputBuffer.Remove(0, 3);
                    return true;
                }

                // Unknown CSI: consume just the ESC to avoid getting stuck.
                _inputBuffer.Remove(0, 1);
                return false;
            }

            if (second == 'O' && _inputBuffer.Length >= 3)
            {
                key = _inputBuffer[2] switch
                {
                    'A' => ConsoleKey.UpArrow,
                    'B' => ConsoleKey.DownArrow,
                    'C' => ConsoleKey.RightArrow,
                    'D' => ConsoleKey.LeftArrow,
                    'H' => ConsoleKey.Home,
                    'F' => ConsoleKey.End,
                    _ => ConsoleKey.None
                };

                if (key != ConsoleKey.None)
                {
                    _inputBuffer.Remove(0, 3);
                    return true;
                }

                _inputBuffer.Remove(0, 1);
                return false;
            }

            // Bare ESC counts as Escape.
            _inputBuffer.Remove(0, 1);
            key = ConsoleKey.Escape;
            return true;
        }

        // Single-byte keys
        _inputBuffer.Remove(0, 1);

        key = first switch
        {
            '\r' or '\n' => ConsoleKey.Enter,
            '\t' => ConsoleKey.Tab,
            '\b' or '\x7F' => ConsoleKey.Backspace,
            ' ' => ConsoleKey.Spacebar,
            >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'
                => (ConsoleKey)char.ToUpperInvariant(first),
            _ => ConsoleKey.None
        };

        return key != ConsoleKey.None;
    }
}
