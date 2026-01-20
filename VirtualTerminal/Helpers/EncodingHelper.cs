using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace VirtualTerminal.Helpers;

/*
public static class EncodingHelper
{
    extension(Encoding)
    {
        public static ReadOnlySpan<byte> Convert(Encoding srcEncoding, Encoding dstEncoding, ReadOnlySpan<byte> bytes)
        {
            int charsCount = srcEncoding.GetCharCount(bytes);
            int bytesCount = charsCount * dstEncoding.GetCharSize();

            Span<char> srcChars = stackalloc char[charsCount];
            byte[] dstBytes = new byte[bytesCount];

            srcEncoding.GetChars(bytes, srcChars);
            dstEncoding.GetBytes(srcChars, dstBytes);
            return dstBytes;
        }
    }
}
*/
