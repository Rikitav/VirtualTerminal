using System.Text;
using VirtualTerminal.Buffer;
using VirtualTerminal.Model;

namespace VirtualTerminal.Tests;

public class TerminalDecoderTests
{
    private static TerminalDecoder CreateDecoder(int columns = 80, int rows = 24)
    {
        TerminalScreenBuffer buffer = new(columns, rows);
        return new TerminalDecoder(buffer);
    }

    [Fact]
    public void Print_PlacesNormalCharacterAtCursor()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write("A"u8);

        Assert.Equal('A', decoder.Buffer.GetCell(0, 0).Character.Value);
        Assert.Equal(1, decoder.State.CursorX);
        Assert.False(decoder.Buffer.GetCell(0, 0).Style.Wide);
    }

    [Fact]
    public void Print_WideCharacter_MarksFirstCellWideAndSecondContinuation()
    {
        TerminalDecoder decoder = CreateDecoder();

        // U+4E00 (CJK ideograph) is wide.
        decoder.Write(Encoding.UTF8.GetBytes("\u4e00"));

        Assert.True(decoder.Buffer.GetCell(0, 0).Style.Wide);
        Assert.True(decoder.Buffer.GetCell(0, 1).Style.Continuation);
        Assert.Equal(2, decoder.State.CursorX);
    }

    [Fact]
    public void Csi_Cup_MovesCursor()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[5;10H"));

        Assert.Equal(9, decoder.State.CursorX);
        Assert.Equal(4, decoder.State.CursorY);
    }

    [Fact]
    public void Csi_EraseDisplay_ClearsScreen()
    {
        TerminalDecoder decoder = CreateDecoder();
        decoder.Write(Encoding.UTF8.GetBytes("hello\x1b[2J"));

        Span<TerminalCellInfo> row = decoder.Buffer.GetRow(0);
        foreach (ref readonly TerminalCellInfo cell in row)
            Assert.Equal(' ', cell.Character.Value);
    }

    [Fact]
    public void Sgr_Reset_ClearsAttributes()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[1;31mA\x1b[0mB"));

        Assert.True(decoder.Buffer.GetCell(0, 0).Style.Bold);
        Assert.False(decoder.Buffer.GetCell(0, 1).Style.Bold);
    }

    [Fact]
    public void Sgr_ForegroundTrueColor_AppliesColor()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[38;2;255;128;64mA"));

        var style = decoder.Buffer.GetCell(0, 0).Style;
        Assert.Equal(255, style.Foreground.R);
        Assert.Equal(128, style.Foreground.G);
        Assert.Equal(64, style.Foreground.B);
    }
}
