using System.Buffers;
using System.Collections.Concurrent;
using System.Text;

namespace VirtualTerminal.Session;

/// <summary>
/// A canonical, in-memory terminal session with a real line editor: positional cursor editing
/// (left/right/home/end), insert and delete at the cursor, kill shortcuts, and command history
/// (Up/Down). It parses the xterm key sequences the control emits (CSI table, SS3, arrows)
/// rather than treating input as raw characters.
/// </summary>
public class CanonicalTerminalSession : TerminalSession
{
    private readonly StringBuilder _input = new StringBuilder();
    private readonly ConcurrentQueue<byte> _outputQueue = [];
    private readonly List<string> _history = [];

    private TaskCompletionSource<string>? _readLineTcs;
    private string _savedCurrent = string.Empty;    // in-progress line preserved while browsing history
    private int _historyCursor;                     // == _history.Count means "the new line being typed"
    private int _cursor;

    /// <summary>Gets or sets the prompt displayed before user input.</summary>
    public string ShellPrompt { get; set; } = "> ";

    /// <summary>Gets a <see cref="TextWriter"/> that feeds output into this session.</summary>
    public TextWriter Writer { get; }

    /// <summary>Gets a <see cref="TextReader"/> that reads from this session.</summary>
    public TextReader Reader { get; }

    /// <summary>
    /// Initializes a new canonical terminal session with default I/O wrappers and prompt.
    /// </summary>
    public CanonicalTerminalSession() : base()
    {
        Writer = new TerminalTextWriter(this);
        Reader = new TerminalTextReader(this);

        Title = "In-Memory Shell";
        PrintPrompt();
    }

    /// <summary>Completes when the user submits a line while a caller is awaiting input.</summary>
    public Task<string> ReadLineAsync()
    {
        ThrowIfDisposed();

        if (_readLineTcs != null)
            return _readLineTcs.Task;

        _input.Clear();
        _cursor = 0;

        _readLineTcs = new TaskCompletionSource<string>();
        return _readLineTcs.Task;
    }

    /// <summary>
    /// Feeds raw output bytes to the terminal decoder and queues them for <see cref="Read(Span{byte})"/>.
    /// </summary>
    /// <param name="data">Output bytes to process.</param>
    public virtual void FeedOutput(ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();
        if (data.IsEmpty)
            return;

        foreach (byte b in data)
        {
            _outputQueue.Enqueue(b);
        }

        Decoder.Write(data);
        NotifyBufferUpdated();
        NotifyInputAvailable();
    }

    /// <summary>
    /// Encodes <paramref name="text"/> and feeds the resulting bytes to <see cref="FeedOutput(ReadOnlySpan{byte})"/>.
    /// </summary>
    /// <param name="text">Text to output.</param>
    public virtual void FeedOutput(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        int byteCount = OutputEncoding.GetByteCount(text);
        byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            int written = OutputEncoding.GetBytes(text, rented);
            FeedOutput(rented.AsSpan(0, written));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    // ---- Input parsing ----

    /// <summary>
    /// Processes input bytes as xterm key sequences or raw characters.
    /// </summary>
    /// <param name="data">Input bytes to process.</param>
    public override void Write(ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();
        if (data.IsEmpty)
            return;

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

    /// <summary>Reprints the active prompt + current input, clears the trailing tail, and positions the caret.</summary>
    private void RedrawLine()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append('\r');
        sb.Append(ShellPrompt);
        sb.Append(_input);
        sb.Append("\x1b[K"); // erase to end of line — removes any stale tail
        sb.Append('\r');

        int target = ShellPrompt.Length + _cursor;
        if (target > 0)
            sb.Append("\x1b[").Append(target).Append('C'); // move caret forward

        FeedOutput(sb.ToString());
    }

    /// <summary>Resets the input line and prints a fresh prompt.</summary>
    public void PrintPrompt()
    {
        ResetInput();

        _historyCursor = _history.Count;
        _savedCurrent = string.Empty;
        FeedOutput(ShellPrompt);
    }

    /// <summary>
    /// Called when the user submits a line and no <see cref="ReadLineAsync"/> is pending.
    /// Override this in derived classes to implement built-in commands; the base implementation
    /// simply prints a fresh prompt.
    /// </summary>
    /// <param name="rawCommand">The submitted line.</param>
    protected virtual Task ExecuteCommandAsync(string rawCommand)
    {
        PrintPrompt();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Copies up to <paramref name="buffer"/>.Length bytes from the output queue into <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">Destination span.</param>
    /// <returns>The number of bytes read.</returns>
    public override int Read(Span<byte> buffer)
    {
        ThrowIfDisposed();
        if (buffer.IsEmpty || _outputQueue.IsEmpty)
            return 0;

        int bytesRead = 0;
        while (bytesRead < buffer.Length && _outputQueue.TryDequeue(out byte b))
        {
            buffer[bytesRead] = b;
            bytesRead++;
        }

        return bytesRead;
    }

    /// <summary>
    /// Reads all queued output bytes into a newly allocated array.
    /// </summary>
    /// <returns>A byte array containing all queued output, or an empty array if none is available.</returns>
    public override byte[] ReadAll()
    {
        ThrowIfDisposed();
        int count = _outputQueue.Count;
        if (count == 0)
            return [];

        byte[] result = new byte[count];
        int actualRead = Read(result);

        if (actualRead < count)
            Array.Resize(ref result, actualRead);

        return result;
    }

    /// <summary>
    /// Releases resources held by this session.
    /// </summary>
    /// <param name="disposing"><c>true</c> when called from <see cref="IDisposable.Dispose"/>; otherwise <c>false</c>.</param>
    protected override void Dispose(bool disposing)
    {
        _outputQueue.Clear();
        _readLineTcs?.TrySetCanceled();
    }

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
                {
                    HistoryPrevious();
                    break;
                }

            case 'B':
                {
                    HistoryNext();
                    break;
                }

            case 'C':
                {
                    for (int i = 0; i < count; i++)
                        CursorRight();

                    break;
                }

            case 'D':
                {
                    for (int i = 0; i < count; i++)
                        CursorLeft();

                    break;
                }

            case 'H':
                {
                    CursorHome();
                    break;
                }

            case 'F':
                {
                    CursorEnd();
                    break;
                }

            case '~':
                {
                    switch (param)
                    {
                        case "1":
                        case "7":
                            {
                                CursorHome();
                                break;
                            }

                        case "4":
                        case "8":
                            {
                                CursorEnd();
                                break;
                            }

                        case "3":
                            {
                                DeleteForward();
                                break;
                            }
                    }

                    break;
                }
        }
    }

    private void HandleSs3(char code)
    {
        switch (code)
        {
            case 'A':
                {
                    HistoryPrevious();
                    break;
                }

            case 'B':
                {
                    HistoryNext();
                    break;
                }

            case 'C':
                {
                    CursorRight();
                    break;
                }

            case 'D':
                {
                    CursorLeft();
                    break;
                }
        }
    }

    private void HandleChar(char c)
    {
        switch (c)
        {
            case '\b':
            case (char)0x7F:
                {
                    Backspace();
                    break;
                }

            case '\x03': // Ctrl+C
                {
                    CancelLine();
                    break;
                }

            case '\x0C': // Ctrl+L — clear screen, keep the line
                {
                    FeedOutput("\x1b[H\x1b[2J");
                    RedrawLine();
                    break;
                }

            case '\x01': // Ctrl+A — home
                {
                    CursorHome();
                    break;
                }

            case '\x05': // Ctrl+E — end
                {
                    CursorEnd();
                    break;
                }

            case '\x15': // Ctrl+U — kill to start
                {
                    KillToStart();
                    break;
                }

            case '\x0B': // Ctrl+K — kill to end
                {
                    KillToEnd();
                    break;
                }

            case '\t':
                {
                    InsertChar('\t');
                    break;
                }

            default:
                {
                    if (!char.IsControl(c))
                        InsertChar(c);

                    break;
                }
        }
    }

    // ---- Line editing primitives ----
    private static bool IsCsiFinal(char c)
        => c >= 0x40 && c <= 0x7E;

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

    private class TerminalTextWriter(CanonicalTerminalSession session) : TextWriter
    {
        private readonly CanonicalTerminalSession _session = session;
        public override Encoding Encoding => _session.OutputEncoding;

        public override void Write(char value) => _session.FeedOutput(value.ToString());
        public override void Write(string? value) => _session.FeedOutput(value ?? string.Empty);
        public override void Write(char[] buffer, int index, int count) => _session.FeedOutput(new string(buffer, index, count));
    }

    private class TerminalTextReader(CanonicalTerminalSession session) : TextReader
    {
        private readonly CanonicalTerminalSession _session = session;

        public override string? ReadLine() => Task.Run(() => _session.ReadLineAsync()).GetAwaiter().GetResult();
        public override async Task<string?> ReadLineAsync() => await _session.ReadLineAsync();
    }
}
