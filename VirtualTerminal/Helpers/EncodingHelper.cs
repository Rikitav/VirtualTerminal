using System.Text;

namespace VirtualTerminal.Helpers;

public static class EncodingHelper
{
    public static unsafe byte[] Convert(Encoding srcEncoding, Encoding dstEncoding, ReadOnlySpan<byte> bytes)
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
