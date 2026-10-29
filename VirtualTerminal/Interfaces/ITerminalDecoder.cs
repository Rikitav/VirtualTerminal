using System.Text;

namespace VirtualTerminal.Interfaces;

/// <summary>
/// A VT/ANSI byte-stream decoder. Feeds raw bytes via <see cref="Write"/>; the decoder maintains
/// terminal state and mutates a screen buffer. Robust decoders never throw on malformed input.
/// </summary>
public interface ITerminalDecoder : IDisposable
{
    /// <summary>Gets the encoding used to interpret the byte stream.</summary>
    Encoding Encoding { get; }

    /// <summary>
    /// Gets or sets whether the decoder applies every byte immediately. When <c>true</c>,
    /// nothing is held back across <see cref="Write"/> calls (for example a trailing CR/LF moves
    /// the cursor at once). When <c>false</c> (the default), plain text is buffered and
    /// dispatched in runs, sequences and interrupting control bytes dispatch instantly, and
    /// trailing newline bytes are parked until further bytes arrive or <see cref="Flush"/> is
    /// called.
    /// </summary>
    bool AutoFlush { get; set; }

    /// <summary>
    /// Processes the given data. Implementations must survive invalid/malformed bytes and
    /// remain usable after any error (recovery is internal, not via exceptions).
    /// </summary>
    void Write(ReadOnlySpan<byte> data);

    /// <summary>
    /// Applies any internally buffered/pending state (such as parked trailing newline bytes)
    /// without waiting for further input.
    /// </summary>
    void Flush();
}
