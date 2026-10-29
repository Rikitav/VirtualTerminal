using System.Text;
using VirtualTerminal.Buffer;

namespace VirtualTerminal.Interfaces;

/// <summary>
/// Represents a terminal backend session that can provide output via a <see cref="TerminalScreenBuffer"/>
/// and accept input as bytes (typically VT/ANSI sequences).
/// </summary>
public interface ITerminalSession : IDisposable
{
    /// <summary>
    /// Raised when the terminal buffer content has changed and the UI should re-render.
    /// </summary>
    public event EventHandler? BufferUpdated;

    /// <summary>
    /// Raised when the session becomes disconnected (for example, remote connection loss).
    /// </summary>
    public event EventHandler? Disconnected;

    /// <summary>
    /// Raised when input data is available to be read.
    /// </summary>
    public event EventHandler? InputAvailable;

    /// <summary>
    /// Gets the underlying screen buffer used for rendering.
    /// </summary>
    public TerminalScreenBuffer Buffer { get; }

    /// <summary>
    /// Gets the underlaying VT escape sequnces decoder
    /// </summary>
    public ITerminalDecoder Decoder { get; }

    /// <summary>
    /// Gets the encoding expected by <see cref="Write"/>.
    /// </summary>
    public Encoding InputEncoding { get; }

    /// <summary>
    /// Gets the encoding used by <see cref="Read"/>.
    /// </summary>
    public Encoding OutputEncoding { get; }

    /// <summary>
    /// Gets the logical session title (for tabs/windows).
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the number of bytes available to be read.
    /// </summary>
    public int AvailableDataLength { get; }

    /// <summary>
    /// Resizes the session to the specified terminal dimensions.
    /// </summary>
    /// <param name="columns">Number of columns (character cells).</param>
    /// <param name="rows">Number of rows (character cells).</param>
    /// <param name="pushScrollback"></param>
    public void Resize(ushort columns, ushort rows, bool pushScrollback = true);

    /// <summary>
    /// Writes input bytes into the session backend.
    /// </summary>
    /// <param name="data">Input bytes (encoding depends on <see cref="InputEncoding"/>).</param>
    public void Write(ReadOnlySpan<byte> data);

    /// <summary>
    /// Reads output bytes from the session.
    /// </summary>
    /// <param name="buffer">Buffer to store the read bytes.</param>
    /// <returns>The number of bytes actually read (0 if no data available).</returns>
    public int Read(Span<byte> buffer);

    /// <summary>
    /// Reads all available output bytes from the session.
    /// </summary>
    /// <returns>Array containing all available read bytes; empty if no data available.</returns>
    public byte[] ReadAll();
}
