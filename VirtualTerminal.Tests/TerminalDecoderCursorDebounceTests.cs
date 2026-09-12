using System.Text;
using VirtualTerminal.Buffer;

namespace VirtualTerminal.Tests;

public class TerminalDecoderCursorDebounceTests
{
    private static TerminalDecoder CreateDecoder(int columns = 80, int rows = 24)
    {
        TerminalScreenBuffer buffer = new(columns, rows);
        return new TerminalDecoder(buffer);
    }

    [Fact]
    public void CrLf_DebouncedUntilFurtherBytes()
    {
        using TerminalDecoder decoder = CreateDecoder();

        decoder.Write("ab\r\n"u8);
        Assert.Equal(2, decoder.State.CursorX);
        Assert.Equal(0, decoder.State.CursorY);
        Assert.Equal(' ', decoder.Buffer.GetCell(1, 0).Character.Value);

        decoder.Write("c"u8);
        Assert.Equal(1, decoder.State.CursorX);
        Assert.Equal(1, decoder.State.CursorY);
        Assert.Equal('c', decoder.Buffer.GetCell(1, 0).Character.Value);
    }

    [Fact]
    public void LfCr_ReverseOrder_SameResult()
    {
        using TerminalDecoder decoder = CreateDecoder();

        decoder.Write("ab\n\r"u8);
        Assert.Equal(2, decoder.State.CursorX);
        Assert.Equal(0, decoder.State.CursorY);

        decoder.Write("c"u8);
        Assert.Equal(1, decoder.State.CursorX);
        Assert.Equal(1, decoder.State.CursorY);
        Assert.Equal('c', decoder.Buffer.GetCell(1, 0).Character.Value);
    }

    [Fact]
    public void ConsecutiveNewlinesCoalesce()
    {
        using TerminalDecoder decoder = CreateDecoder();

        decoder.Write("a\r\n\n"u8);
        Assert.Equal(1, decoder.State.CursorX);
        Assert.Equal(0, decoder.State.CursorY);

        decoder.Write("b"u8);
        Assert.Equal(1, decoder.State.CursorX);
        Assert.Equal(2, decoder.State.CursorY);
        Assert.Equal('b', decoder.Buffer.GetCell(2, 0).Character.Value);
    }

    [Fact]
    public void CsiFlushesPendingNewline()
    {
        using TerminalDecoder decoder = CreateDecoder();

        decoder.Write("ab\r\n"u8);
        decoder.Write("\x1b[3;3H"u8);

        Assert.Equal(2, decoder.State.CursorX);
        Assert.Equal(2, decoder.State.CursorY);
    }

    [Fact]
    public void BackspaceFlushesPendingNewline()
    {
        using TerminalDecoder decoder = CreateDecoder();

        decoder.Write("ab\r\n"u8);
        decoder.Write("\x08X"u8);

        Assert.Equal(1, decoder.State.CursorX);
        Assert.Equal(1, decoder.State.CursorY);
        Assert.Equal('X', decoder.Buffer.GetCell(1, 0).Character.Value);
    }

    [Fact]
    public void LfAtScrollBottom_DefersScroll()
    {
        using TerminalDecoder decoder = CreateDecoder(columns: 10, rows: 4);

        decoder.Write("0\r\n1\r\n2\r\n3"u8);
        Assert.Equal(1, decoder.State.CursorX);
        Assert.Equal(3, decoder.State.CursorY);

        decoder.Write("\n"u8);
        Assert.Equal(1, decoder.State.CursorX);
        Assert.Equal(3, decoder.State.CursorY);
        Assert.Equal('0', decoder.Buffer.GetCell(0, 0).Character.Value);

        decoder.Write("x"u8);
        Assert.Equal('1', decoder.Buffer.GetCell(0, 0).Character.Value);
        Assert.Equal('x', decoder.Buffer.GetCell(3, 0).Character.Value);
        Assert.Equal(1, decoder.State.CursorX);
        Assert.Equal(3, decoder.State.CursorY);
    }

    [Fact]
    public void FullReset_DiscardsPendingNewline()
    {
        using TerminalDecoder decoder = CreateDecoder();

        decoder.Write("ab\r\n"u8);
        decoder.Write("\u001bc"u8);

        Assert.Equal(0, decoder.State.CursorX);
        Assert.Equal(0, decoder.State.CursorY);

        decoder.Write("z"u8);
        Assert.Equal(1, decoder.State.CursorX);
        Assert.Equal(0, decoder.State.CursorY);
        Assert.Equal('z', decoder.Buffer.GetCell(0, 0).Character.Value);
        Assert.Equal(' ', decoder.Buffer.GetCell(1, 0).Character.Value);
    }

    [Fact]
    public void BatchedPrint_WrapAndWideChars_PreservePerRuneSemantics()
    {
        using TerminalDecoder decoder = CreateDecoder(columns: 5, rows: 3);

        // 2 narrow + wide CJK (wraps to continuation) + narrow that hits the last column
        // (arms deferred wrap) + narrow that wraps + combining mark (width 0 → 1) + wide CJK.
        decoder.Write(Encoding.UTF8.GetBytes("ab\u4e00cd\u0301\u4e00"));

        Assert.Equal(4, decoder.State.CursorX);
        Assert.Equal(1, decoder.State.CursorY);

        Assert.True(decoder.Buffer.GetCell(0, 2).Style.Wide);
        Assert.True(decoder.Buffer.GetCell(0, 3).Style.Continuation);
        Assert.Equal('d', decoder.Buffer.GetCell(1, 0).Character.Value);
        Assert.Equal('\u0301', decoder.Buffer.GetCell(1, 1).Character.Value);
        Assert.True(decoder.Buffer.GetCell(1, 2).Style.Wide);
        Assert.True(decoder.Buffer.GetCell(1, 3).Style.Continuation);
    }

    [Fact]
    public void MidWriteNewlinesApplyImmediately()
    {
        using TerminalDecoder decoder = CreateDecoder();

        // The \r\n is followed by more bytes in the same Write, so it is not trailing and
        // must be applied within that Write.
        decoder.Write("a\r\nb"u8);

        Assert.Equal(1, decoder.State.CursorX);
        Assert.Equal(1, decoder.State.CursorY);
        Assert.Equal('b', decoder.Buffer.GetCell(1, 0).Character.Value);
    }
}
