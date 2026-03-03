using System.Diagnostics;
using System.Text;
using VirtualTerminal.Interop;

namespace VirtualTerminal.Session;

/// <summary>
/// Base implementation of <see cref="ITerminalSession"/> that owns a <see cref="VirtualTerminalBuffer"/>
/// and exposes helper notifications for UI updates and disconnect events.
/// </summary>
public abstract class TerminalSession : ITerminalSession
{
    private readonly VirtualTerminalBuffer _buffer;

    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler? BufferUpdated;

    /// <inheritdoc />
    public event EventHandler? Disconnected;

    /// <inheritdoc />
    public VirtualTerminalBuffer Buffer => _buffer;

    /// <inheritdoc />
    public virtual Encoding InputEncoding { get; set; }

    /// <inheritdoc />
    public virtual string Title
    {
        get => GetType().Name + " (" + _buffer.OutputHandle + ")";
    }

    /// <summary>
    /// Initializes a new session with a fresh <see cref="VirtualTerminalBuffer"/> and default input encoding.
    /// </summary>
    public TerminalSession()
    {
        _buffer = new VirtualTerminalBuffer();
        InputEncoding = VirtualTerminalBuffer.Encoding;
    }

    /// <summary>
    /// Initializes a new session with a fresh <see cref="VirtualTerminalBuffer"/> and the specified input encoding.
    /// </summary>
    /// <param name="encoding">Encoding used for <see cref="WriteInput(ReadOnlySpan{byte})"/>.</param>
    public TerminalSession(Encoding encoding)
    {
        _buffer = new VirtualTerminalBuffer();
        InputEncoding = encoding;
    }

    /// <inheritdoc />
    public virtual void Resize(int columns, int rows)
    {
        _buffer.ResizeBuffer(columns, rows);
    }

    /// <inheritdoc />
    public virtual void WriteInput(ReadOnlySpan<byte> data)
    {
        _buffer.Write(data);
    }

    /// <summary>
    /// Raises <see cref="ITerminalSession.BufferUpdated"/> to notify the UI that the buffer has changed.
    /// </summary>
    protected void NotifyBufferUpdated()
    {
        BufferUpdated?.Invoke(this, EventArgs.Empty);
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
    /// Disposes the session and its underlying <see cref="VirtualTerminalBuffer"/>.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _buffer.Dispose();
        Dispose(true);

        GC.SuppressFinalize(this);
        _disposed = true;
    }
}
