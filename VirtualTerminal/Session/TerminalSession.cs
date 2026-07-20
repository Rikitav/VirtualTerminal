using System.Collections.Concurrent;
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
    private readonly TerminalScreenBuffer _buffer;
    private readonly ConcurrentQueue<byte> _inputQueue = [];

    private string _title;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler? BufferUpdated;

    /// <inheritdoc />
    public event EventHandler? Disconnected;

    /// <inheritdoc />
    public event EventHandler? InputAvailable;

    /// <inheritdoc />
    public ITerminalDecoder Decoder
    {
        get
        {
            ThrowIfDisposed();
            return _decoder;
        }
    }

    /// <summary>
    /// Gets the underlying <see cref="TerminalDecoder"/> for derived sessions that need
    /// to apply local-only VT sequences (e.g. clearing the screen during ConPTY resize).
    /// </summary>
    public TerminalScreenBuffer Buffer
    {
        get
        {
            ThrowIfDisposed();
            return _buffer;
        }
    }

    /// <inheritdoc />
    public Encoding InputEncoding
    {
        get => Encoding.UTF8;
    }

    /// <inheritdoc />
    public Encoding OutputEncoding
    {
        get => Encoding.UTF8;
    }

    /// <inheritdoc />
    public virtual string Title
    {
        get => _title;
        set => _title = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <inheritdoc />
    public int AvailableDataLength => _inputQueue.Count;

    /// <summary>
    /// Gets the underlying <see cref="TerminalDecoder"/> used to parse VT input for this session.
    /// </summary>
    protected TerminalDecoder InternalDecoder => _decoder;

    /// <summary>
    /// Initializes a new session with a fresh <see cref="TerminalScreenBuffer"/> and default input encoding.
    /// </summary>
    protected TerminalSession()
    {
        _decoder = new TerminalDecoder();
        _buffer = new TerminalScreenBuffer(128, 20);

        _title = GetType().Name;
        _decoder.TitleChanged += (_, title) => _title = title;
        _decoder.SendOutput = AddToInputQueue;  // report responses (DSR/DA/…) reach Read()
    }

    /// <inheritdoc />
    public virtual void Resize(ushort columns, ushort rows, bool pushScrollback = true)
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
        ThrowIfDisposed();
        if (buffer.IsEmpty)
            return 0;

        int bytesRead = 0;
        lock (_inputQueue)
        {
            while (!_inputQueue.IsEmpty && bytesRead < buffer.Length)
            {
                if (!_inputQueue.TryDequeue(out byte resultByte))
                    continue;

                buffer[bytesRead] = resultByte;
                bytesRead++;
            }
        }

        return bytesRead;
    }

    /// <inheritdoc />
    public virtual byte[] ReadAll()
    {
        ThrowIfDisposed();
        lock (_inputQueue)
        {
            if (_inputQueue.IsEmpty)
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
    /// Throws <see cref="ObjectDisposedException"/> if the session has already been disposed.
    /// </summary>
    protected void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
    }

    /// <summary>
    /// Disposes the session and its underlying <see cref="TerminalScreenBuffer"/>.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _buffer.Dispose();
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
