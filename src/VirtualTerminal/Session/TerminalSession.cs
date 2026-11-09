using System.Text;
using System.Threading;
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

    private readonly Lock _queueLock = new();
    private byte[] _inputBuffer = new byte[1024];
    private int _inputHead;
    private int _inputTail;
    private int _inputCount;

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
    public int AvailableDataLength
    {
        get
        {
            lock (_queueLock)
                return _inputCount;
        }
    }

    /// <summary>
    /// Gets the underlying <see cref="TerminalDecoder"/> used to parse VT input for this session.
    /// </summary>
    protected TerminalDecoder InternalDecoder => _decoder;

    /// <summary>
    /// Initializes a new session with a fresh <see cref="TerminalScreenBuffer"/> and default input encoding.
    /// </summary>
    protected TerminalSession()
    {
        _buffer = new TerminalScreenBuffer(128, 20);
        _decoder = new TerminalDecoder(_buffer);

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

        lock (_queueLock)
        {
            if (_inputCount == 0)
                return 0;

            int bytesToRead = Math.Min(buffer.Length, _inputCount);
            int firstPart = Math.Min(bytesToRead, _inputBuffer.Length - _inputHead);
            _inputBuffer.AsSpan(_inputHead, firstPart).CopyTo(buffer);
            int remaining = bytesToRead - firstPart;
            if (remaining > 0)
            {
                _inputBuffer.AsSpan(0, remaining).CopyTo(buffer[firstPart..]);
            }

            _inputHead = (_inputHead + bytesToRead) % _inputBuffer.Length;
            _inputCount -= bytesToRead;

            if (_inputCount == 0)
            {
                _inputHead = 0;
                _inputTail = 0;
            }

            return bytesToRead;
        }
    }

    /// <inheritdoc />
    public virtual byte[] ReadAll()
    {
        ThrowIfDisposed();
        lock (_queueLock)
        {
            if (_inputCount == 0)
                return [];

            byte[] result = new byte[_inputCount];
            Read(result);
            return result;
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
        lock (_queueLock)
        {
            wasEmpty = _inputCount == 0;
            EnsureInputCapacity(_inputCount + data.Length);

            int firstPart = Math.Min(data.Length, _inputBuffer.Length - _inputTail);
            data[..firstPart].CopyTo(_inputBuffer.AsSpan(_inputTail));
            int remaining = data.Length - firstPart;
            if (remaining > 0)
            {
                data.Slice(firstPart, remaining).CopyTo(_inputBuffer.AsSpan(0));
            }

            _inputTail = (_inputTail + data.Length) % _inputBuffer.Length;
            _inputCount += data.Length;
        }

        if (wasEmpty)
        {
            NotifyInputAvailable();
        }
    }

    private void EnsureInputCapacity(int required)
    {
        if (required <= _inputBuffer.Length)
            return;

        int newCapacity = Math.Max(_inputBuffer.Length * 2, required);
        byte[] newBuffer = new byte[newCapacity];
        if (_inputCount > 0)
        {
            int firstPart = Math.Min(_inputCount, _inputBuffer.Length - _inputHead);
            Array.Copy(_inputBuffer, _inputHead, newBuffer, 0, firstPart);
            int remaining = _inputCount - firstPart;
            if (remaining > 0)
            {
                Array.Copy(_inputBuffer, 0, newBuffer, firstPart, remaining);
            }
        }

        _inputBuffer = newBuffer;
        _inputHead = 0;
        _inputTail = _inputCount;
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

        lock (_queueLock)
        {
            _inputCount = 0;
            _inputHead = 0;
            _inputTail = 0;
        }

        GC.SuppressFinalize(this);
        _disposed = true;
    }
}
