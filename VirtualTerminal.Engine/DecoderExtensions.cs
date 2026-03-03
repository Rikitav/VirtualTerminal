using System.Text;
using VirtualTerminal.Engine.Components;

namespace VirtualTerminal.Engine;

public static class DecoderExtensions
{
    /// <summary>
    /// Writes raw bytes encoded from <paramref name="encoding"/> into the console screen buffer via <c>WriteConsoleW</c>.
    /// </summary>
    /// <param name="decoder"></param>
    /// <param name="encoding"></param>
    /// <param name="data"></param>
    public static void WriteFromEncoding(this IDecoder decoder, Encoding encoding, ReadOnlySpan<byte> data)
    {
        if (decoder.Encoding == encoding)
        {
            decoder.Write(data);
            return;
        }

        int maxCharCount = encoding.GetMaxCharCount(data.Length);
        Span<char> charBuffer = stackalloc char[maxCharCount];

        int charsWritten = encoding.GetChars(data, charBuffer);
        if (charsWritten == 0)
            return;

        decoder.Write(charBuffer.Slice(0, charsWritten));
        return;
    }

    /// <summary>
    /// Writes character into the console screen buffer via <c>WriteConsoleW</c> in <see cref="TerminalScreenBuffer.Encoding"/>.
    /// </summary>
    /// <param name="decoder"></param>
    /// <param name="data"></param>
    public static void Write(this IDecoder decoder, ReadOnlySpan<char> data)
    {
        int bytesCount = decoder.Encoding.GetMaxByteCount(data.Length);
        Span<byte> bytes = stackalloc byte[bytesCount];

        int bytesWritten = decoder.Encoding.GetBytes(data, bytes);
        if (bytesCount == 0)
            return;

        decoder.Write(bytes.Slice(0, bytesWritten));
    }

    /// <summary>
    /// Writes character into the console screen buffer via <c>WriteConsoleW</c> in <see cref="TerminalScreenBuffer.Encoding"/>.
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="data"></param>
    /// <param name="offset"></param>
    /// <param name="length"></param>
    public static void Write(this IDecoder decoder, ReadOnlySpan<char> data, int offset, int length)
    {
        decoder.Write(data.Slice(offset, length));
    }

    /// <summary>
    /// Writes raw bytes into the console screen buffer via <c>WriteConsoleW</c>.
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="data"></param>
    /// <param name="offset"></param>
    /// <param name="length"></param>
    public static void Write(this IDecoder decoder, ReadOnlySpan<byte> data, int offset, int length)
    {
        decoder.Write(data.Slice(offset, length));
    }

    /// <summary>
    /// Writes raw bytes into the console screen buffer via <c>WriteConsoleW</c>.
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="encoding"></param>
    /// <param name="data"></param>
    /// <param name="offset"></param>
    /// <param name="length"></param>
    public static void WriteFromEncoding(this IDecoder decoder, Encoding encoding, ReadOnlySpan<byte> data, int offset, int length)
    {
        decoder.WriteFromEncoding(encoding, data.Slice(offset, length));
    }
}
