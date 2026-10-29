using System.Text;

namespace VirtualTerminal.Helpers;

/// <summary>Helpers for converting data between character encodings.</summary>
public static class EncodingHelper
{
    /// <summary>
    /// Converts the provided <paramref name="bytes"/> from <paramref name="srcEncoding"/> to
    /// <paramref name="dstEncoding"/>.
    /// </summary>
    /// <returns>A byte array containing the encoded result.</returns>
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
