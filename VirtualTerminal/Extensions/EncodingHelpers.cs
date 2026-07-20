using System.Text;

namespace VirtualTerminal.Extensions;

/// <summary>
/// Helpers for converting between character encodings.
/// </summary>
public static class EncodingHelpers
{
    /// <summary>
    /// Converts <paramref name="bytes"/> from <paramref name="srcEncoding"/> to <paramref name="dstEncoding"/>.
    /// </summary>
    /// <param name="srcEncoding">Encoding of the input bytes.</param>
    /// <param name="dstEncoding">Encoding to produce.</param>
    /// <param name="bytes">Input bytes in <paramref name="srcEncoding"/>.</param>
    /// <returns>The re-encoded byte array.</returns>
    public static byte[] Convert(Encoding srcEncoding, Encoding dstEncoding, ReadOnlySpan<byte> bytes)
    {
        if (srcEncoding == dstEncoding)
            return bytes.ToArray();

        int charCount = srcEncoding.GetCharCount(bytes);
        Span<char> chars = stackalloc char[charCount];

        srcEncoding.GetChars(bytes, chars);
        int dstByteCount = dstEncoding.GetByteCount(chars.Slice(0, charCount));

        byte[] result = new byte[dstByteCount];
        dstEncoding.GetBytes(chars.Slice(0, charCount), result);
        return result;
    }
}
