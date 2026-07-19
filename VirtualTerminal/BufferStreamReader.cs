using System;
using System.IO;
using System.Text;
using VirtualTerminal.Extensions;
using VirtualTerminal.Interfaces;

namespace VirtualTerminal;

/// <summary>
/// A <see cref="TextReader"/> implementation that reads character input directly from an <see cref="ITerminalSession"/>.
/// Used by <see cref="TerminalSessionExtensions.RedirectConsole"/> to redirect <see cref="Console.In"/>.
/// </summary>
public class BufferStreamReader : TextReader
{
    private readonly ITerminalSession _session;
    private readonly Decoder _decoder;

    private readonly StringBuilder _charBuffer = new();

    /// <summary>
    /// Initializes a new reader for the specified <paramref name="session"/>.
    /// </summary>
    public BufferStreamReader(ITerminalSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _decoder = _session.OutputEncoding.GetDecoder();
    }

    /// <inheritdoc />
    public override int Peek()
    {
        if (_charBuffer.Length == 0)
            FetchFromSession();

        return _charBuffer.Length > 0 ? _charBuffer[0] : -1;
    }

    /// <inheritdoc />
    public override int Read()
    {
        if (_charBuffer.Length == 0)
        {
            FetchFromSession();
        }

        if (_charBuffer.Length == 0)
            return -1;

        char c = _charBuffer[0];
        _charBuffer.Remove(0, 1);
        return c;
    }

    /// <inheritdoc />
    public override int Read(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (buffer.Length - index < count)
            throw new ArgumentException("Invalid offset and length.");

        return Read(buffer.AsSpan(index, count));
    }

    /// <inheritdoc />
    public override int Read(Span<char> buffer)
    {
        if (buffer.IsEmpty)
            return 0;

        if (_charBuffer.Length < buffer.Length)
            FetchFromSession();

        if (_charBuffer.Length == 0)
            return 0;

        int charsToCopy = Math.Min(buffer.Length, _charBuffer.Length);
        for (int i = 0; i < charsToCopy; i++)
            buffer[i] = _charBuffer[i];

        _charBuffer.Remove(0, charsToCopy);
        return charsToCopy;
    }

    /// <inheritdoc />
    public override string? ReadLine()
    {
        FetchFromSession();

        if (_charBuffer.Length == 0)
            return null;

        for (int i = 0; i < _charBuffer.Length; i++)
        {
            char c = _charBuffer[i];
            if (c == '\r' || c == '\n')
            {
                string line = _charBuffer.ToString(0, i);
                int charsToRemove = i + 1;

                if (c == '\r' && i + 1 < _charBuffer.Length && _charBuffer[i + 1] == '\n')
                    charsToRemove++;

                _charBuffer.Remove(0, charsToRemove);
                return line;
            }
        }

        string remaining = _charBuffer.ToString();
        _charBuffer.Clear();
        return remaining;
    }

    private void FetchFromSession()
    {
        int availableBytes = _session.AvailableDataLength;
        if (availableBytes == 0)
            return;

        byte[]? rentedBytes = null;
        Span<byte> byteBuffer = availableBytes <= 1024
            ? stackalloc byte[availableBytes]
            : (rentedBytes = System.Buffers.ArrayPool<byte>.Shared.Rent(availableBytes));

        char[]? rentedChars = null;
        Span<char> charBuffer = availableBytes <= 1024
            ? stackalloc char[availableBytes]
            : (rentedChars = System.Buffers.ArrayPool<char>.Shared.Rent(availableBytes));

        try
        {
            int bytesRead = _session.Read(byteBuffer[..availableBytes]);
            if (bytesRead == 0)
                return;

            int charsDecoded = _decoder.GetChars(byteBuffer[..bytesRead], charBuffer, flush: false);

            if (charsDecoded > 0)
            {
                _charBuffer.Append(charBuffer[..charsDecoded]);
            }
        }
        finally
        {
            if (rentedBytes != null)
                System.Buffers.ArrayPool<byte>.Shared.Return(rentedBytes);
            if (rentedChars != null)
                System.Buffers.ArrayPool<char>.Shared.Return(rentedChars);
        }
    }
}
