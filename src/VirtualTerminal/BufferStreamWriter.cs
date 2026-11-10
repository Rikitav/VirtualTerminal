using System.Buffers;
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
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (buffer.Length - index < count)
            throw new ArgumentException("Invalid offset and length.");

        WriteChars(buffer.AsSpan(index, count));
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<char> buffer)
    {
        WriteChars(buffer);
    }

    /// <inheritdoc />
    public override void Write(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            WriteChars(value.AsSpan());
    }

    /// <inheritdoc />
    public override void Write(char value)
    {
        Span<char> charSpan = [value];
        WriteChars(charSpan);
    }

    /// <inheritdoc />
    public override void WriteLine()
    {
        WriteChars(NewLine.AsSpan());
    }

    /// <inheritdoc />
    public override void WriteLine(ReadOnlySpan<char> buffer)
    {
        WriteChars(buffer);
        WriteLine();
    }

    /// <inheritdoc />
    public override void WriteLine(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            WriteChars(value.AsSpan());

        WriteLine();
    }

    private void WriteChars(ReadOnlySpan<char> chars)
    {
        if (chars.IsEmpty)
            return;

        int maxByteCount = Encoding.GetMaxByteCount(chars.Length);
        const int StackAllocLimit = 512;

        if (maxByteCount <= StackAllocLimit)
        {
            Span<byte> writeData = stackalloc byte[maxByteCount];
            int bytesWritten = Encoding.GetBytes(chars, writeData);
            if (bytesWritten > 0)
                _decoder.Write(writeData[..bytesWritten]);
            return;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(maxByteCount);
        try
        {
            int bytesWritten = Encoding.GetBytes(chars, rented);
            if (bytesWritten > 0)
                _decoder.Write(rented.AsSpan(0, bytesWritten));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
