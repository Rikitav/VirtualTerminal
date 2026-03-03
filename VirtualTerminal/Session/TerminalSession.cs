using System.Diagnostics;
using System.Text;
using VirtualTerminal.Engine;
using VirtualTerminal.Engine.Components;

namespace VirtualTerminal.Session;

/// <summary>
/// Base implementation of <see cref="ITerminalSession"/> that owns a <see cref="TerminalScreenBuffer"/>
/// and exposes helper notifications for UI updates and disconnect events.
/// </summary>
public abstract class TerminalSession : ITerminalSession
{
    private readonly IBufferedDecoder _decoder;

    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler? BufferUpdated;

    /// <inheritdoc />
    public event EventHandler? Disconnected;

    /// <inheritdoc />
    public TerminalScreenBuffer Buffer => _decoder.Buffer; // Use decoder's buffer

    /// <inheritdoc />
    public IBufferedDecoder Decoder => _decoder;

    /// <inheritdoc />
    public virtual Encoding InputEncoding { get; set; }

    /// <inheritdoc />
    public virtual string Title
    {
        get
        {
            Process proc = Process.GetCurrentProcess();
            return proc.ProcessName + "_" + proc.Id;
        }
    }

    /// <summary>
    /// Initializes a new session with a fresh <see cref="TerminalScreenBuffer"/> and default input encoding.
    /// </summary>
    public TerminalSession()
    {
        _decoder = new BufferedDecoder();
        // AnsiDecoder creates its own buffer
        InputEncoding = Encoding.UTF8;
    }

    /// <summary>
    /// Initializes a new session with a fresh <see cref="TerminalScreenBuffer"/> and the specified input encoding.
    /// </summary>
    /// <param name="encoding">Encoding used for <see cref="WriteInput(ReadOnlySpan{byte})"/>.</param>
    public TerminalSession(Encoding encoding)
    {
        _decoder = new BufferedDecoder();
        // AnsiDecoder creates its own buffer
        InputEncoding = encoding;
    }

    /// <inheritdoc />
    public virtual void Resize(int columns, int rows)
    {
        _decoder.Buffer.ColumnsCount = columns;
        _decoder.Buffer.RowsCount = rows;
        // TODO: Implement proper resize logic in TerminalScreenBuffer
    }

    /// <inheritdoc />
    public virtual void WriteInput(ReadOnlySpan<byte> data)
    {
        _decoder.Write(data);
        NotifyBufferUpdated();
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
    /// Disposes the session and its underlying <see cref="TerminalScreenBuffer"/>.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _decoder.Buffer.Dispose();
        Dispose(true);

        GC.SuppressFinalize(this);
        _disposed = true;
    }
}
