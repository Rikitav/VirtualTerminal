using System.Text;

namespace VirtualTerminal.Interfaces;

/// <summary>
/// A VT/ANSI byte-stream decoder. Feeds raw bytes via <see cref="Write"/>; the decoder maintains
/// terminal state and mutates a screen buffer. Robust decoders never throw on malformed input.
/// </summary>
public interface ITerminalDecoder : IDisposable
{
    Encoding Encoding { get; }

    /// <summary>
    /// Processes the given data. Implementations must survive invalid/malformed bytes and
    /// remain usable after any error (recovery is internal, not via exceptions).
    /// </summary>
    void Write(ReadOnlySpan<byte> data);
}
