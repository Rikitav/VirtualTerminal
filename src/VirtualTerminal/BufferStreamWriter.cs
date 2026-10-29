using System.Text;
using VirtualTerminal.Buffer;
using VirtualTerminal.Extensions;
using VirtualTerminal.Interfaces;

namespace VirtualTerminal;

/// <summary>
/// A <see cref="TextWriter"/> implementation that writes text into a <see cref="TerminalScreenBuffer"/>.
/// Used by <see cref="TerminalSessionExtensions.RedirectConsole"/> to redirect
/// <see cref="Console.Out"/> into a terminal session buffer.
/// </summary>
public class BufferStreamWriter(TerminalScreenBuffer buffer, ITerminalDecoder decoder) : TextWriter()
{
    private readonly TerminalScreenBuffer _buffer = buffer;
    private readonly ITerminalDecoder _decoder = decoder;

    /// <inheritdoc />
    public override Encoding Encoding => _decoder.Encoding;

    /// <inheritdoc />
    public override string NewLine => Environment.NewLine;

    /// <summary>
    /// Initializes a new writer for the specified <paramref name="session"/>.
    /// </summary>
    /// <param name="session"></param>
    public BufferStreamWriter(ITerminalSession session)
        : this(session.Buffer, session.Decoder) { }

    /// <inheritdoc />
    public override void Write(char[] buffer, int index, int count)
    {
        Span<char> charData = buffer.AsSpan(index, count);
        Span<byte> writeData = stackalloc byte[charData.Length * 2];
        int charCount = Encoding.GetBytes(charData, writeData);

        if (charCount == 0)
            return;

        _decoder.Write(writeData);
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<char> buffer)
    {
        Span<byte> writeData = stackalloc byte[buffer.Length * 2];
        int charCount = Encoding.GetBytes(buffer, writeData);

        if (charCount == 0)
            return;

        _decoder.Write(writeData);
    }

    /// <inheritdoc />
    public override void Write(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            Write(value.AsSpan());
    }

    /// <inheritdoc />
    public override void Write(char value)
    {
        Span<byte> writeData = stackalloc byte[2];
        int charCount = Encoding.GetBytes([value], writeData);

        if (charCount == 0)
            return;

        _decoder.Write(writeData);
    }

    /// <inheritdoc />
    public override void WriteLine()
    {
        Write(NewLine);
    }

    /// <inheritdoc />
    public override void WriteLine(ReadOnlySpan<char> buffer)
    {
        Span<byte> writeData = stackalloc byte[buffer.Length * 2];
        int charCount = Encoding.GetBytes(buffer, writeData);

        if (charCount == 0)
            return;

        _decoder.Write(writeData);
        WriteLine();
    }

    /// <inheritdoc />
    public override void WriteLine(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            Write(value);

        WriteLine();
    }
}
