using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using VirtualTerminal.Buffer;
using VirtualTerminal.Interfaces;

namespace VirtualTerminal.Session;

/// <summary>
/// A completely in-memory implementation of <see cref="ITerminalSession"/>.
/// Useful for testing, embedding local sub-shells, or mocking terminal behavior.
/// </summary>
public class InMemoryTerminalSession : ITerminalSession
{
    private readonly ConcurrentQueue<byte> _outputQueue = [];
    private readonly TerminalScreenBuffer _buffer;
    private readonly TerminalDecoder _decoder;
    private string _title = "In-Memory Session";
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

    public Encoding InputEncoding => Encoding.UTF8;
    public Encoding OutputEncoding => Encoding.UTF8;

    public string Title
    {
        get => _title;
        set => _title = value ?? throw new ArgumentNullException(nameof(value));
    }

    public int AvailableDataLength => _outputQueue.Count;

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryTerminalSession"/>.
    /// </summary>
    /// <param name="initialCols">Initial columns count.</param>
    /// <param name="initialRows">Initial rows count.</param>
    /// <param name="decoderFactory">A factory to construct the VT/ANSI decoder linked to this buffer.</param>
    public InMemoryTerminalSession()
    {
        _buffer = new TerminalScreenBuffer(128, 20);
        _decoder = new TerminalDecoder(_buffer);
    }

    public void Resize(ushort columns, ushort rows)
    {
        ThrowIfDisposed();
        _decoder.Resize(columns, rows);
        TriggerBufferUpdated();
    }

    /// <summary>
    /// Writes user input (e.g. keyboard events) into the session.
    /// In this in-memory implementation, writing input directly feeds the decoder
    /// to update the screen buffer, simulating immediate local echo/processing.
    /// </summary>
    public virtual void Write(ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();
        if (data.IsEmpty)
            return;

        _decoder.Write(data);
        TriggerBufferUpdated();
    }

    /// <summary>
    /// Simulates program/backend output generation.
    /// Call this method to feed data that the terminal application *prints* to the console.
    /// </summary>
    public virtual void FeedOutput(ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();
        if (data.IsEmpty)
            return;

        foreach (byte b in data)
        {
            _outputQueue.Enqueue(b);
        }

        // Также скармливаем это декодеру, чтобы текст появился на экране
        _decoder.Write(data);

        TriggerBufferUpdated();
        InputAvailable?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Simulates program/backend output generation using a string.
    /// </summary>
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

    /// <summary>
    /// Forces a disconnect state from the backend side.
    /// </summary>
    public void Disconnect()
    {
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void TriggerBufferUpdated()
    {
        BufferUpdated?.Invoke(this, EventArgs.Empty);
    }

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
        _isDisposed = true;

        GC.SuppressFinalize(this);
    }
}
