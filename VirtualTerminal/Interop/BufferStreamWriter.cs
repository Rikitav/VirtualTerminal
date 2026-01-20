using System.IO;
using System.Text;
using System.Windows.Input;
using VirtualTerminal.Session;

namespace VirtualTerminal.Interop;

/// <summary>
/// A <see cref="TextWriter"/> implementation that writes text into a <see cref="VirtualTerminalBuffer"/>.
/// Used by <see cref="TerminalSessionExtensions.RedirectConsole"/> to redirect
/// <see cref="Console.Out"/> into a terminal session buffer.
/// </summary>
public class BufferStreamWriter : TextWriter
{
    private readonly VirtualTerminalBuffer _buffer;

    /// <inheritdoc />
    public override Encoding Encoding => VirtualTerminalBuffer.Encoding;

    /// <summary>
    /// Initializes a new writer for the specified <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">Target terminal buffer.</param>
    public BufferStreamWriter(VirtualTerminalBuffer buffer)
    {
        _buffer = buffer;

        NewLine = KeyHelper.Convert(Key.Enter);
    }

    /// <inheritdoc />
    public override void Write(char[] buffer, int index, int count)
    {
        Span<char> charData = buffer.AsSpan(index, count);
        Span<byte> writeData = stackalloc byte[charData.Length * 2];
        int charCount = Encoding.GetBytes(charData, writeData);

        if (charCount == 0)
            return;

        _buffer.Write(writeData);
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<char> buffer)
    {
        Span<byte> writeData = stackalloc byte[buffer.Length * 2];
        int charCount = Encoding.GetBytes(buffer, writeData);

        if (charCount == 0)
            return;

        _buffer.Write(writeData);
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

        _buffer.Write(writeData);
    }

    /// <summary>
    /// Writes a <see cref="Key"/> as a VT sequence (when supported) into the buffer.
    /// </summary>
    /// <param name="value">Key to encode.</param>
    public void Write(Key value)
    {
        string? result = KeyHelper.Convert(value);
        if (result == null)
            return;

        Write(result);
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

        _buffer.Write(writeData);
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
