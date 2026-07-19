using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using VirtualTerminal.Buffer;
using VirtualTerminal.Interfaces;

namespace VirtualTerminal.Session;

public class ScriptCommandLineSession : ITerminalSession, IDisposable
{
    private readonly TerminalDecoder _decoder;
    private readonly TerminalScreenBuffer _buffer;
    private readonly ConcurrentQueue<byte> _outputQueue = [];
    private readonly StringBuilder _inputLineBuffer = new StringBuilder();

    private TaskCompletionSource<string>? _readLineTcs;
    private string _title = "Terminal Session";
    private bool _isDisposed;

    public event EventHandler? BufferUpdated;
    public event EventHandler? Disconnected;
    public event EventHandler? InputAvailable;

    public TerminalScreenBuffer Buffer
    {
        get
        {
            ThrowIfDisposed();
            return _buffer;
        }
    }

    public ITerminalDecoder Decoder
    {
        get
        {
            ThrowIfDisposed();
            return _decoder;
        }
    }

    public string Title
    {
        get => _title;
        set => _title = value ?? throw new ArgumentNullException(nameof(value));
    }

    public Encoding InputEncoding
    {
        get => Encoding.UTF8;
    }

    public Encoding OutputEncoding
    {
        get => Encoding.UTF8;
    }

    public int AvailableDataLength
    {
        get => _outputQueue.Count;
    }

    public TextWriter Writer { get; }
    public TextReader Reader { get; }

    public ScriptCommandLineSession()
    {
        _buffer = new TerminalScreenBuffer(128, 20);
        _decoder = new TerminalDecoder(_buffer);

        Writer = new TerminalTextWriter(this);
        Reader = new TerminalTextReader(this);
    }

    public Task<string> ReadLineAsync()
    {
        ThrowIfDisposed();

        if (_readLineTcs != null)
            return _readLineTcs.Task;

        _inputLineBuffer.Clear();
        _readLineTcs = new TaskCompletionSource<string>();
        return _readLineTcs.Task;
    }

    public virtual void Write(ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();
        if (_readLineTcs == null || data.IsEmpty)
            return;

        string inputStr = InputEncoding.GetString(data);
        foreach (char c in inputStr)
        {
            if (c == '\r' || c == '\n')
            {
                FeedOutput("\r\n");

                string result = _inputLineBuffer.ToString();
                _inputLineBuffer.Clear();

                TaskCompletionSource<string> tcs = _readLineTcs;
                _readLineTcs = null;
                tcs.SetResult(result);

                break;
            }
            else if (c == '\b' || c == (char)127)
            {
                if (_inputLineBuffer.Length > 0)
                {
                    _inputLineBuffer.Remove(_inputLineBuffer.Length - 1, 1);
                    FeedOutput("\b \b");
                }
            }
            else if (!char.IsControl(c))
            {
                _inputLineBuffer.Append(c);
                FeedOutput(c.ToString());
            }
        }
    }

    public virtual void FeedOutput(ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();
        if (data.IsEmpty)
            return;

        foreach (byte b in data)
        {
            _outputQueue.Enqueue(b);
        }

        _decoder.Write(data);
        TriggerBufferUpdated();
        InputAvailable?.Invoke(this, EventArgs.Empty);
    }

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

    public virtual int Read(Span<byte> buffer)
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

    public virtual byte[] ReadAll()
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

    public void Resize(ushort columns, ushort rows)
    {
        ThrowIfDisposed();
        _decoder.Resize(columns, rows);

        TriggerBufferUpdated();
    }

    public void Disconnect()
        => Disconnected?.Invoke(this, EventArgs.Empty);

    private void TriggerBufferUpdated()
        => BufferUpdated?.Invoke(this, EventArgs.Empty);

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(GetType().Name);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _buffer.Dispose();
        _outputQueue.Clear();
        _readLineTcs?.TrySetCanceled();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    private class TerminalTextWriter(ScriptCommandLineSession session) : TextWriter
    {
        private readonly ScriptCommandLineSession _session = session;
        public override Encoding Encoding => _session.OutputEncoding;

        public override void Write(char value) => _session.FeedOutput(value.ToString());
        public override void Write(string? value) => _session.FeedOutput(value ?? string.Empty);
        public override void Write(char[] buffer, int index, int count) => _session.FeedOutput(new string(buffer, index, count));
    }

    private class TerminalTextReader(ScriptCommandLineSession session) : TextReader
    {
        private readonly ScriptCommandLineSession _session = session;

        public override string? ReadLine() => Task.Run(() => _session.ReadLineAsync()).GetAwaiter().GetResult();
        public override async Task<string?> ReadLineAsync() => await _session.ReadLineAsync();
    }
}
