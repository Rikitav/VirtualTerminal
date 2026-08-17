global using VirtualTerminal.Buffer;

using System.Drawing;
using System.Text;
using VirtualTerminal.Enums;
using VirtualTerminal.Model;
using VirtualTerminal.Options;

namespace VirtualTerminal.Tests;

public class SgrAndCharAttributesTests
{
    private static TerminalDecoder CreateDecoder(int columns = 80, int rows = 24, TerminalOptions? options = null)
    {
        TerminalScreenBuffer buffer = new(columns, rows);
        return new TerminalDecoder(buffer, options);
    }

    [Fact]
    public void CharAttributes_Default_ToCellStyle_HasDefaultColorsAndNoFlags()
    {
        TerminalDecoder decoder = CreateDecoder();

        CellRenderStyle style = decoder.State.Attributes.ToCellStyle(decoder.Options.Palette, decoder.Options.BoldAsBright);

        Assert.True(style.ForegroundIsDefault);
        Assert.True(style.BackgroundIsDefault);
        Assert.True(style.UnderlineColorIsDefault);
        Assert.Equal(Color.White, style.Foreground);
        Assert.Equal(Color.Black, style.Background);
        Assert.Equal(Underline.None, style.UnderlineStyle);
        Assert.Equal(Blink.None, style.Blink);
        Assert.False(style.Bold);
    }

    [Fact]
    public void CharAttributes_Reset_ReturnsToDefaultState()
    {
        TerminalDecoder decoder = CreateDecoder();
        decoder.State.Attributes.Bold = true;
        decoder.State.Attributes.ForegroundSource = ColorSource.Direct;
        decoder.State.Attributes.ForegroundRgb = Color.Red;
        decoder.State.Attributes.UnderlineStyle = Underline.Curly;

        decoder.State.Attributes.Reset();

        Assert.False(decoder.State.Attributes.Bold);
        Assert.Equal(ColorSource.Default, decoder.State.Attributes.ForegroundSource);
        Assert.Equal(Underline.None, decoder.State.Attributes.UnderlineStyle);
        Assert.Equal(Blink.None, decoder.State.Attributes.Blink);
    }

    [Fact]
    public void Sgr_Bold_AppliesFlagAndBrightensIndexedForeground()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[1;32mA"));

        CellRenderStyle style = decoder.Buffer.GetCell(0, 0).Style;
        Assert.True(style.Bold);
        Assert.Equal(Color.FromArgb(0x00, 0xFF, 0x00), style.Foreground);
    }

    [Fact]
    public void Sgr_BoldAsBrightFalse_KeepsNormalIndexedForeground()
    {
        TerminalOptions options = new() { BoldAsBright = false };
        TerminalDecoder decoder = CreateDecoder(options: options);

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[1;32mA"));

        CellRenderStyle style = decoder.Buffer.GetCell(0, 0).Style;
        Assert.True(style.Bold);
        Assert.Equal(Color.FromArgb(0x00, 0xCD, 0x00), style.Foreground);
    }

    [Fact]
    public void Sgr_Faint_AppliesFlag()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[2mA"));

        Assert.True(decoder.Buffer.GetCell(0, 0).Style.Faint);
    }

    [Fact]
    public void Sgr_ItalicAndFraktur_CanBeSetAndResetTogether()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[3mA\x1b[20mB\x1b[23mC"));

        Assert.True(decoder.Buffer.GetCell(0, 0).Style.Italic);
        Assert.True(decoder.Buffer.GetCell(0, 1).Style.Fraktur);
        Assert.False(decoder.Buffer.GetCell(0, 2).Style.Italic);
        Assert.False(decoder.Buffer.GetCell(0, 2).Style.Fraktur);
    }

    [Fact]
    public void Sgr_UnderlineStyles_AreAppliedAndCleared()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[4mA\x1b[21mB\x1b[4:3mC\x1b[24mD"));

        Assert.Equal(Underline.Single, decoder.Buffer.GetCell(0, 0).Style.UnderlineStyle);
        Assert.True(decoder.Buffer.GetCell(0, 1).Style.DoublyUnderlined);
        Assert.Equal(Underline.Double, decoder.Buffer.GetCell(0, 1).Style.UnderlineStyle);
        Assert.Equal(Underline.Curly, decoder.Buffer.GetCell(0, 2).Style.UnderlineStyle);
        Assert.Equal(Underline.None, decoder.Buffer.GetCell(0, 3).Style.UnderlineStyle);
        Assert.False(decoder.Buffer.GetCell(0, 3).Style.DoublyUnderlined);
    }

    [Fact]
    public void Sgr_BlinkSlowAndRapid_AreAppliedAndCleared()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[5mA\x1b[6mB\x1b[25mC"));

        Assert.Equal(Blink.Slow, decoder.Buffer.GetCell(0, 0).Style.Blink);
        Assert.Equal(Blink.Rapid, decoder.Buffer.GetCell(0, 1).Style.Blink);
        Assert.Equal(Blink.None, decoder.Buffer.GetCell(0, 2).Style.Blink);
    }

    [Fact]
    public void Sgr_InverseConcealStrikethroughOverlineFramedEncircled()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[7mA\x1b[8mB\x1b[9mC\x1b[53mD\x1b[51mE\x1b[52mF"));

        Assert.True(decoder.Buffer.GetCell(0, 0).Style.Inverse);
        Assert.True(decoder.Buffer.GetCell(0, 1).Style.Conceal);
        Assert.True(decoder.Buffer.GetCell(0, 2).Style.Strikethrough);
        Assert.True(decoder.Buffer.GetCell(0, 3).Style.Overline);
        Assert.True(decoder.Buffer.GetCell(0, 4).Style.Framed);
        Assert.True(decoder.Buffer.GetCell(0, 5).Style.Encircled);
    }

    [Fact]
    public void Sgr_IndexedForegroundAndBackground_AreResolved()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[31;42mA\x1b[91;102mB"));

        CellRenderStyle normal = decoder.Buffer.GetCell(0, 0).Style;
        Assert.Equal(Color.FromArgb(0xCD, 0x00, 0x00), normal.Foreground);
        Assert.Equal(Color.FromArgb(0x00, 0xCD, 0x00), normal.Background);

        CellRenderStyle bright = decoder.Buffer.GetCell(0, 1).Style;
        Assert.Equal(Color.FromArgb(0xFF, 0x00, 0x00), bright.Foreground);
        Assert.Equal(Color.FromArgb(0x00, 0xFF, 0x00), bright.Background);
    }

    [Fact]
    public void Sgr_DefaultForegroundAndBackground_RestoreDefaultSentinels()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[31;42mA\x1b[39;49mB"));

        CellRenderStyle style = decoder.Buffer.GetCell(0, 1).Style;
        Assert.True(style.ForegroundIsDefault);
        Assert.True(style.BackgroundIsDefault);
    }

    [Fact]
    public void Sgr_Indexed256ForegroundAndBackground_AreResolved()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[38;5;196;48;5;21mA"));

        CellRenderStyle style = decoder.Buffer.GetCell(0, 0).Style;
        Assert.Equal(Color.FromArgb(0xFF, 0x00, 0x00), style.Foreground);
        Assert.Equal(Color.FromArgb(0x00, 0x00, 0xFF), style.Background);
    }

    [Fact]
    public void Sgr_TrueColorForegroundBackgroundAndUnderline_AreResolved()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[38;2;10;20;30;48;2;40;50;60;58;2;70;80;90mA"));

        CellRenderStyle style = decoder.Buffer.GetCell(0, 0).Style;
        Assert.Equal(Color.FromArgb(10, 20, 30), style.Foreground);
        Assert.Equal(Color.FromArgb(40, 50, 60), style.Background);
        Assert.Equal(Color.FromArgb(70, 80, 90), style.UnderlineColor);
        Assert.False(style.UnderlineColorIsDefault);
    }

    [Fact]
    public void Sgr_Reset_ClearsAllAttributes()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[1;3;4;7;31;42mA\x1b[0mB\x1b[mC"));

        CellRenderStyle styled = decoder.Buffer.GetCell(0, 0).Style;
        Assert.True(styled.Bold);
        Assert.True(styled.Italic);

        CellRenderStyle reset1 = decoder.Buffer.GetCell(0, 1).Style;
        Assert.False(reset1.Bold);
        Assert.False(reset1.Italic);
        Assert.True(reset1.ForegroundIsDefault);
        Assert.True(reset1.BackgroundIsDefault);

        CellRenderStyle reset2 = decoder.Buffer.GetCell(0, 2).Style;
        Assert.Equal(Underline.None, reset2.UnderlineStyle);
        Assert.False(reset2.Inverse);
    }

    [Fact]
    public void Sgr_CombinedAttributes_AreAllApplied()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[1;3;4;7;31mA"));

        CellRenderStyle style = decoder.Buffer.GetCell(0, 0).Style;
        Assert.True(style.Bold);
        Assert.True(style.Italic);
        Assert.Equal(Underline.Single, style.UnderlineStyle);
        Assert.True(style.Inverse);
        Assert.Equal(Color.FromArgb(0xFF, 0x00, 0x00), style.Foreground);
    }
}
