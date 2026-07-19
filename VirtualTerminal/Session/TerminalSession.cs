using System.Text;
using VirtualTerminal.Buffer;
using VirtualTerminal.Interfaces;

namespace VirtualTerminal.Session;

/// <summary>
/// Base implementation of <see cref="ITerminalSession"/> that owns a <see cref="TerminalScreenBuffer"/>
/// and exposes helper notifications for UI updates and disconnect events.
/// </summary>
public abstract class TerminalSession : ITerminalSession
{
    private readonly TerminalDecoder _decoder;
    private readonly Queue<byte> _inputQueue;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler? BufferUpdated;

    /// <inheritdoc />
    public event EventHandler? Disconnected;

    /// <inheritdoc />
    public event EventHandler? InputAvailable;

    /// <inheritdoc />
    public ITerminalDecoder Decoder => _decoder;

    /// <summary>
    /// Gets the underlying <see cref="TerminalDecoder"/> for derived sessions that need
    /// to apply local-only VT sequences (e.g. clearing the screen during ConPTY resize).
    /// </summary>
    protected TerminalDecoder InternalDecoder => _decoder;

    /// <inheritdoc />
    public TerminalScreenBuffer Buffer => _decoder.Buffer;

    /// <inheritdoc />
    public virtual Encoding InputEncoding { get; set; }

    /// <inheritdoc />
    public virtual Encoding OutputEncoding { get; set; }

    /// <inheritdoc />
    public virtual string Title
    {
        get => GetType().Name;
    }

    /// <inheritdoc />
    public int AvailableDataLength => _inputQueue.Count;

    /// <summary>
    /// Initializes a new session with a fresh <see cref="TerminalScreenBuffer"/> and default input encoding.
    /// </summary>
    protected TerminalSession()
    {
        _decoder = new TerminalDecoder();
        _inputQueue = new Queue<byte>();

        _decoder.SendOutput = AddToInputQueue;  // report responses (DSR/DA/…) reach Read()
        InputEncoding = Encoding.UTF8;
        OutputEncoding = Encoding.UTF8;
    }

    /// <summary>
    /// Initializes a new session with a fresh <see cref="TerminalScreenBuffer"/> and the specified input encoding.
    /// </summary>
    /// <param name="encoding">Encoding used for <see cref="Write(ReadOnlySpan{byte})"/>.</param>
    protected TerminalSession(Encoding encoding)
    {
        _decoder = new TerminalDecoder();
        _inputQueue = new Queue<byte>();

        _decoder.SendOutput = AddToInputQueue;  // report responses (DSR/DA/…) reach Read()
        InputEncoding = encoding;
        OutputEncoding = encoding;
    }

    /// <summary>
    /// Initializes a new session with the specified input and output encodings.
    /// </summary>
    /// <param name="inputEncoding">Encoding used for <see cref="Write(ReadOnlySpan{byte})"/>.</param>
    /// <param name="outputEncoding">Encoding used for <see cref="Read(Span{byte})"/>.</param>
    protected TerminalSession(Encoding inputEncoding, Encoding outputEncoding)
    {
        _decoder = new TerminalDecoder();
        _inputQueue = new Queue<byte>();

        _decoder.SendOutput = AddToInputQueue;  // report responses (DSR/DA/…) reach Read()
        InputEncoding = inputEncoding;
        OutputEncoding = outputEncoding;
    }

    /// <inheritdoc />
    public virtual void Resize(ushort columns, ushort rows)
        => Resize(columns, rows, pushScrollback: true);

    public virtual void Resize(ushort columns, ushort rows, bool pushScrollback)
    {
        _decoder.Resize(columns, rows, pushScrollback);
    }

    /// <inheritdoc />
    public virtual void Write(ReadOnlySpan<byte> data)
    {
        _decoder.Write(data);
        NotifyBufferUpdated();
    }

    /// <inheritdoc />
    public virtual int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
            return 0;

        int bytesRead = 0;
        lock (_inputQueue)
        {
            while (_inputQueue.Count > 0 && bytesRead < buffer.Length)
            {
                buffer[bytesRead] = _inputQueue.Dequeue();
                bytesRead++;
            }
        }

        return bytesRead;
    }

    /// <inheritdoc />
    public virtual byte[] ReadAll()
    {
        lock (_inputQueue)
        {
            if (_inputQueue.Count == 0)
                return [];

            byte[] data = _inputQueue.ToArray();
            _inputQueue.Clear();
            return data;
        }
    }

    /// <summary>
    /// Adds data to the input queue to be read by consumers.
    /// </summary>
    /// <param name="data">Data to add to the input queue.</param>
    protected void AddToInputQueue(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        bool wasEmpty;
        lock (_inputQueue)
        {
            wasEmpty = _inputQueue.Count == 0;
            foreach (byte b in data)
            {
                _inputQueue.Enqueue(b);
            }
        }

        if (wasEmpty)
        {
            NotifyInputAvailable();
        }
    }

    /// <summary>
    /// Raises <see cref="ITerminalSession.BufferUpdated"/> to notify the UI that the buffer has changed.
    /// </summary>
    protected void NotifyBufferUpdated()
    {
        BufferUpdated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raises <see cref="ITerminalSession.InputAvailable"/> to notify that input data is available for reading.
    /// </summary>
    protected void NotifyInputAvailable()
    {
        InputAvailable?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raises <see cref="ITerminalSession.Disconnected"/> to notify listeners that the session was disconnected.
    /// </summary>
    protected void NotifyDisconnected()
    {
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Releases resources held by a derived session implementation.
    /// </summary>
    /// <param name="disposing"><c>true</c> when called from <see cref="Dispose()"/>; otherwise <c>false</c>.</param>
    protected abstract void Dispose(bool disposing);

    /// <summary>
    /// Disposes the session and its underlying <see cref="TerminalScreenBuffer"/>.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _decoder.Dispose();
        Dispose(true);

        lock (_inputQueue)
        {
            _inputQueue.Clear();
        }

        GC.SuppressFinalize(this);
        _disposed = true;
    }
}
