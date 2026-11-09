using System.Text;
using VirtualTerminal.Model;

namespace VirtualTerminal.Tests;

public class WcwidthTests
{
    [Theory]
    [InlineData(' ', 1)]
    [InlineData('A', 1)]
    [InlineData('z', 1)]
    [InlineData('~', 1)]
    [InlineData('0', 1)]
    [InlineData('!', 1)]
    public void Of_PrintableAscii_ReturnsWidthOne(char c, int expected)
    {
        Assert.Equal(expected, Wcwidth.Of(new Rune(c)));
    }

    [Theory]
    [InlineData('\0', -1)]
    [InlineData('\t', -1)]
    [InlineData('\n', -1)]
    [InlineData('\r', -1)]
    [InlineData((char)0x1B, -1)]
    [InlineData((char)0x7F, -1)]
    [InlineData((char)0x80, -1)]
    [InlineData((char)0x9F, -1)]
    public void Of_ControlAndDel_ReturnsNonPrintable(char c, int expected)
    {
        Assert.Equal(expected, Wcwidth.Of(new Rune(c)));
    }

    [Theory]
    [InlineData(0x00A0, 1)] // Non-breaking space
    [InlineData(0x00E9, 1)] // Latin small letter e with acute (é)
    [InlineData(0x0152, 1)] // Latin capital ligature OE (Œ)
    [InlineData(0x0250, 1)] // IPA Latin small letter turned A
    public void Of_LatinAndIpaBelow0x300_ReturnsWidthOne(int codepoint, int expected)
    {
        Assert.Equal(expected, Wcwidth.Of(new Rune(codepoint)));
    }

    [Theory]
    [InlineData(0x0300, 0)] // Combining grave accent
    [InlineData(0x0301, 0)] // Combining acute accent
    [InlineData(0x200B, 0)] // Zero width space
    public void Of_ZeroWidthCombining_ReturnsZero(int codepoint, int expected)
    {
        Assert.Equal(expected, Wcwidth.Of(new Rune(codepoint)));
    }

    [Theory]
    [InlineData(0x4E00, 2)] // CJK Unified Ideograph
    [InlineData(0x3042, 2)] // Hiragana letter A
    [InlineData(0x1F600, 2)] // Grinning face emoji
    [InlineData(0xFF01, 2)] // Fullwidth exclamation mark
    public void Of_WideAndEmoji_ReturnsTwo(int codepoint, int expected)
    {
        Assert.Equal(expected, Wcwidth.Of(new Rune(codepoint)));
    }
}
