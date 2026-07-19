using System.Text;

namespace VirtualTerminal.Session;

/// <summary>
/// An in-memory session that bridges a script/CLI to the <see cref="Views.Controls.TerminalControl"/>
/// with a real line editor: positional cursor editing (left/right/home/end), insert and delete at the
/// cursor, kill shortcuts, and command history (Up/Down). It parses the xterm key sequences the
/// control emits (CSI <c>~</c> table, SS3, arrows) rather than treating input as raw chars.
/// </summary>
public class InMemoryTerminalEngineSession : InMemoryTerminalSession
{
    private const string ShellPrompt = "rikitav-shell> ";

    private readonly StringBuilder _input = new StringBuilder();
    private int _cursor;

    private readonly List<string> _history = [];
    private string _savedCurrent = string.Empty;    // in-progress line preserved while browsing history
    private int _historyCursor;                     // == _history.Count means "the new line being typed"

    // The prompt currently shown (with color codes) and its visible width, for cursor positioning.
    private string _activePromptColored = "\x1b[32m" + ShellPrompt + "\x1b[0m";
    private int _activePromptLength = ShellPrompt.Length;

    private TaskCompletionSource<string>? _readLineTcs;

    public InMemoryTerminalEngineSession() : base()
    {
        Title = "In-Memory Shell";
        PrintPrompt();
    }

    /// <summary>Completes when the user submits a line while a command is awaiting input.</summary>
    public Task<string> ReadLineAsync()
    {
        if (_readLineTcs != null)
            return _readLineTcs.Task;

        _readLineTcs = new TaskCompletionSource<string>();
        return _readLineTcs.Task;
    }

    // ---- Input parsing ----
    public override void Write(ReadOnlySpan<byte> data)
    {
        string s = InputEncoding.GetString(data);
        int i = 0;
        bool skipNextLf = false;

        while (i < s.Length)
        {
            char c = s[i];

            if (c == '\x1b')
            {
                if (i + 1 < s.Length && s[i + 1] == '[')
                {
                    int j = i + 2;
                    while (j < s.Length && !IsCsiFinal(s[j]))
                        j++;
                    if (j < s.Length)
                    {
                        HandleCsi(s.AsSpan(i, j - i + 1));
                        i = j + 1;
                        skipNextLf = false;
                        continue;
                    }
                }
                else if (i + 2 < s.Length && s[i + 1] == 'O')
                {
                    HandleSs3(s[i + 2]);
                    i += 3;
                    skipNextLf = false;
                    continue;
                }

                i++; // lone ESC — ignore
                continue;
            }

            if (c == '\r')
            {
                Submit();
                skipNextLf = true;
                i++;
                continue;
            }

            if (c == '\n')
            {
                if (skipNextLf)
                {
                    skipNextLf = false;
                    i++;
                    continue;
                }

                Submit();
                i++;
                continue;
            }

            skipNextLf = false;
            HandleChar(c);
            i++;
        }
    }

    private static bool IsCsiFinal(char c)
        => c >= 0x40 && c <= 0x7E;

    private void HandleCsi(ReadOnlySpan<char> seq)
    {
        if (seq.Length < 3)
            return;

        char final = seq[^1];
        string param = seq.Slice(2, seq.Length - 3).ToString();
        int count = int.TryParse(param, out int n) && n > 0 ? n : 1;

        switch (final)
        {
            case 'A':
                HistoryPrevious();
                break;
            case 'B':
                HistoryNext();
                break;
            case 'C':
                for (int i = 0; i < count; i++)
                    CursorRight();
                break;
            case 'D':
                for (int i = 0; i < count; i++)
                    CursorLeft();
                break;
            case 'H':
                CursorHome();
                break;
            case 'F':
                CursorEnd();
                break;
            case '~':
                switch (param)
                {
                    case "1":
                    case "7":
                        CursorHome();
                        break;
                    case "4":
                    case "8":
                        CursorEnd();
                        break;
                    case "3":
                        DeleteForward();
                        break;
                }
                break;
        }
    }

    private void HandleSs3(char code)
    {
        switch (code)
        {
            case 'A':
                HistoryPrevious();
                break;
            case 'B':
                HistoryNext();
                break;
            case 'C':
                CursorRight();
                break;
            case 'D':
                CursorLeft();
                break;
        }
    }

    private void HandleChar(char c)
    {
        switch (c)
        {
            case '\b':
            case (char)0x7F:
                Backspace();
                break;
            case '\x03': // Ctrl+C
                CancelLine();
                break;
            case '\x0C': // Ctrl+L — clear screen, keep the line
                FeedOutput("\x1b[H\x1b[2J");
                RedrawLine();
                break;
            case '\x01': // Ctrl+A — home
                CursorHome();
                break;
            case '\x05': // Ctrl+E — end
                CursorEnd();
                break;
            case '\x15': // Ctrl+U — kill to start
                KillToStart();
                break;
            case '\x0B': // Ctrl+K — kill to end
                KillToEnd();
                break;
            case '\t':
                InsertChar('\t');
                break;
            default:
                if (!char.IsControl(c))
                    InsertChar(c);
                break;
        }
    }

    // ---- Line editing primitives ----
    private void InsertChar(char c)
    {
        _input.Insert(_cursor, c);
        _cursor++;
        RedrawLine();
    }

    private void Backspace()
    {
        if (_cursor <= 0)
            return;
        _input.Remove(_cursor - 1, 1);
        _cursor--;
        RedrawLine();
    }

    private void DeleteForward()
    {
        if (_cursor >= _input.Length)
            return;
        _input.Remove(_cursor, 1);
        RedrawLine();
    }

    private void CursorLeft()
    {
        if (_cursor <= 0)
            return;
        _cursor--;
        FeedOutput("\x1b[D");
    }

    private void CursorRight()
    {
        if (_cursor >= _input.Length)
            return;
        _cursor++;
        FeedOutput("\x1b[C");
    }

    private void CursorHome()
    {
        _cursor = 0;
        RedrawLine();
    }

    private void CursorEnd()
    {
        _cursor = _input.Length;
        RedrawLine();
    }

    private void KillToStart()
    {
        if (_cursor <= 0)
            return;
        _input.Remove(0, _cursor);
        _cursor = 0;
        RedrawLine();
    }

    private void KillToEnd()
    {
        if (_cursor >= _input.Length)
            return;
        _input.Remove(_cursor, _input.Length - _cursor);
        RedrawLine();
    }

    private void HistoryPrevious()
    {
        if (_history.Count == 0 || _historyCursor == 0)
            return;
        if (_historyCursor == _history.Count)
            _savedCurrent = _input.ToString();
        _historyCursor--;
        SetLine(_history[_historyCursor]);
    }

    private void HistoryNext()
    {
        if (_historyCursor >= _history.Count)
            return;
        _historyCursor++;
        SetLine(_historyCursor == _history.Count ? _savedCurrent : _history[_historyCursor]);
    }

    private void SetLine(string text)
    {
        _input.Clear();
        _input.Append(text);
        _cursor = _input.Length;
        RedrawLine();
    }

    private void CancelLine()
    {
        _cursor = _input.Length;
        RedrawLine(); // move caret to the end so ^C prints after the line
        FeedOutput("^C\r\n");
        ResetInput();
        _historyCursor = _history.Count;
        _savedCurrent = string.Empty;
        PrintPrompt();
    }

    private void Submit()
    {
        FeedOutput("\r\n");
        string line = _input.ToString();
        ResetInput();

        if (_readLineTcs != null)
        {
            TaskCompletionSource<string>? tcs = _readLineTcs;
            _readLineTcs = null;
            tcs.SetResult(line);
            return;
        }

        if (!string.IsNullOrEmpty(line) && (_history.Count == 0 || _history[^1] != line))
            _history.Add(line);

        _historyCursor = _history.Count;
        _savedCurrent = string.Empty;
        _ = ExecuteCommandAsync(line);
    }

    private void ResetInput()
    {
        _input.Clear();
        _cursor = 0;
    }

    /// <summary>Reprints the active prompt + current input, clears the trailing tail, and positions the caret.</summary>
    private void RedrawLine()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append('\r');
        sb.Append(_activePromptColored);
        sb.Append(_input);
        sb.Append("\x1b[K"); // erase to end of line — removes any stale tail
        sb.Append('\r');

        int target = _activePromptLength + _cursor;
        if (target > 0)
            sb.Append("\x1b[").Append(target).Append('C'); // move caret forward

        FeedOutput(sb.ToString());
    }

    public void PrintPrompt()
    {
        _activePromptColored = "\x1b[32m" + ShellPrompt + "\x1b[0m";
        _activePromptLength = ShellPrompt.Length;
        ResetInput();

        _historyCursor = _history.Count;
        _savedCurrent = string.Empty;
        FeedOutput(_activePromptColored);
    }

    private async Task ExecuteCommandAsync(string rawCommand)
    {
        string[] parts = rawCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            PrintPrompt();
            return;
        }

        string cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "help":
                FeedOutput("Available commands:\r\n");
                FeedOutput("  help  - Show this message\r\n");
                FeedOutput("  echo  - Print text to terminal\r\n");
                FeedOutput("  read  - Interactive input testing\r\n");
                FeedOutput("  clear - Clear the screen\r\n");
                FeedOutput("  exit  - Disconnect session\r\n");
                FeedOutput("Arrows/Home/End/Ins/Del and Up/Down history are supported.\r\n");
                break;

            case "echo":
                FeedOutput(parts.Length > 1 ? string.Join(' ', parts[1..]) + "\r\n" : "\r\n");
                break;

            case "read":
                _activePromptColored = "Enter your name: ";
                _activePromptLength = _activePromptColored.Length;
                ResetInput();
                FeedOutput(_activePromptColored);
                string name = await ReadLineAsync();
                FeedOutput($"\x1b[33mHello, {name}!\x1b[0m\r\n");
                break;

            case "clear":
                FeedOutput("\x1b[H\x1b[2J");
                break;

            case "exit":
                FeedOutput("Goodbye!\r\n");
                Disconnect();
                return;

            default:
                FeedOutput($"\x1b[31mCommand not found: {cmd}\x1b[0m\r\n");
                break;
        }

        PrintPrompt();
    }
}
