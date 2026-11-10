using System;
using System.Buffers;
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

    private char[] _charBuffer = new char[256];
    private int _readIndex;
    private int _writeIndex;

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
        if (_writeIndex - _readIndex == 0)
            FetchFromSession();

        return _writeIndex - _readIndex > 0 ? _charBuffer[_readIndex] : -1;
    }

    /// <inheritdoc />
    public override int Read()
    {
        if (_writeIndex - _readIndex == 0)
            FetchFromSession();

        if (_writeIndex - _readIndex == 0)
            return -1;

        return _charBuffer[_readIndex++];
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

        if (_writeIndex - _readIndex < buffer.Length)
            FetchFromSession();

        int available = _writeIndex - _readIndex;
        if (available == 0)
            return 0;

        int charsToCopy = Math.Min(buffer.Length, available);
        _charBuffer.AsSpan(_readIndex, charsToCopy).CopyTo(buffer);
        _readIndex += charsToCopy;
        return charsToCopy;
    }

    /// <inheritdoc />
    public override string? ReadLine()
    {
        FetchFromSession();

        int available = _writeIndex - _readIndex;
        if (available == 0)
            return null;

        Span<char> remaining = _charBuffer.AsSpan(_readIndex, available);
        for (int i = 0; i < remaining.Length; i++)
        {
            char c = remaining[i];
            if (c == '\r' || c == '\n')
            {
                string line = new string(remaining[..i]);
                int charsToRemove = i + 1;

                if (c == '\r' && i + 1 < remaining.Length && remaining[i + 1] == '\n')
                    charsToRemove++;

                _readIndex += charsToRemove;
                return line;
            }
        }

        string allRemaining = new string(remaining);
        _readIndex = _writeIndex;
        return allRemaining;
    }

    private void FetchFromSession()
    {
        int availableBytes = _session.AvailableDataLength;
        if (availableBytes == 0)
            return;

        byte[]? rentedBytes = null;
        Span<byte> byteBuffer = availableBytes <= 1024
            ? stackalloc byte[availableBytes]
            : (rentedBytes = ArrayPool<byte>.Shared.Rent(availableBytes));

        char[]? rentedChars = null;
        Span<char> charBuffer = availableBytes <= 1024
            ? stackalloc char[availableBytes]
            : (rentedChars = ArrayPool<char>.Shared.Rent(availableBytes));

        try
        {
            int bytesRead = _session.Read(byteBuffer[..availableBytes]);
            if (bytesRead == 0)
                return;

            int charsDecoded = _decoder.GetChars(byteBuffer[..bytesRead], charBuffer, flush: false);
            if (charsDecoded > 0)
            {
                AppendChars(charBuffer[..charsDecoded]);
            }
        }
        finally
        {
            if (rentedBytes != null)
                ArrayPool<byte>.Shared.Return(rentedBytes);
            if (rentedChars != null)
                ArrayPool<char>.Shared.Return(rentedChars);
        }
    }

    private void AppendChars(ReadOnlySpan<char> chars)
    {
        if (chars.IsEmpty)
            return;

        if (_readIndex == _writeIndex)
        {
            _readIndex = 0;
            _writeIndex = 0;
        }
        else if (_readIndex > 0 && _charBuffer.Length - _writeIndex < chars.Length)
        {
            int remaining = _writeIndex - _readIndex;
            Array.Copy(_charBuffer, _readIndex, _charBuffer, 0, remaining);
            _readIndex = 0;
            _writeIndex = remaining;
        }

        int required = _writeIndex + chars.Length;
        if (required > _charBuffer.Length)
        {
            int newCap = Math.Max(_charBuffer.Length * 2, required);
            Array.Resize(ref _charBuffer, newCap);
        }

        chars.CopyTo(_charBuffer.AsSpan(_writeIndex));
        _writeIndex += chars.Length;
    }
}
