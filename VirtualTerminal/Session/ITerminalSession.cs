using System.Text;
using VirtualTerminal.Engine;
using VirtualTerminal.Engine.Components;

namespace VirtualTerminal.Session;

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
    /// Gets the underlying screen buffer used for rendering.
    /// </summary>
    public TerminalScreenBuffer Buffer { get; }

    /// <summary>
    /// Gets the underlaying VT escape sequnces decoder
    /// </summary>
    public ITerminalDecoder Decoder { get; }

    /// <summary>
    /// Gets the encoding expected by <see cref="WriteInput(ReadOnlySpan{byte})"/>.
    /// </summary>
    public Encoding InputEncoding { get; }

    /// <summary>
    /// Gets the logical session title (for tabs/windows).
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Resizes the session to the specified terminal dimensions.
    /// </summary>
    /// <param name="columns">Number of columns (character cells).</param>
    /// <param name="rows">Number of rows (character cells).</param>
    public void Resize(ushort columns, ushort rows);

    /// <summary>
    /// Writes input bytes into the session backend.
    /// </summary>
    /// <param name="data">Input bytes (encoding depends on <see cref="InputEncoding"/>).</param>
    public void WriteInput(ReadOnlySpan<byte> data);
}
