using VirtualTerminal.Buffer;
using VirtualTerminal.Model;
using VirtualTerminal.Options;

namespace VirtualTerminal.Tests;

public class TerminalPaletteAndOptionsTests
{
    #region TerminalPalette

    [Fact]
    public void Palette_Constructor_InitializesXtermDefaults()
    {
        TerminalPalette palette = new();

        Assert.Equal(TerminalColor.FromArgb(0x00, 0x00, 0x00), palette[0]);
        Assert.Equal(TerminalColor.FromArgb(0xFF, 0xFF, 0xFF), palette[15]);
        Assert.Equal(TerminalColor.FromArgb(0x00, 0x00, 0x00), palette[16]);
        Assert.Equal(TerminalColor.FromArgb(0x00, 0x00, 0x5F), palette[17]);
        Assert.Equal(TerminalColor.FromArgb(0xFF, 0xFF, 0xFF), palette[231]);
        Assert.Equal(TerminalColor.FromArgb(0x08, 0x08, 0x08), palette[232]);
        Assert.Equal(TerminalColor.FromArgb(0xEE, 0xEE, 0xEE), palette[255]);
    }

    [Fact]
    public void Palette_Indexer_GetAndSet_WithinRange()
    {
        TerminalPalette palette = new();
        TerminalColor expected = TerminalColor.FromArgb(0x12, 0x34, 0x56);

        palette[42] = expected;

        Assert.Equal(expected, palette[42]);
    }

    [Fact]
    public void Palette_Indexer_Get_Negative_ThrowsArgumentOutOfRange()
    {
        TerminalPalette palette = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => palette[-1]);
    }

    [Fact]
    public void Palette_Indexer_Get_Above255_ThrowsArgumentOutOfRange()
    {
        TerminalPalette palette = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => palette[256]);
    }

    [Fact]
    public void Palette_Indexer_Set_Negative_ThrowsArgumentOutOfRange()
    {
        TerminalPalette palette = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => palette[-1] = TerminalColor.Red);
    }

    [Fact]
    public void Palette_Indexer_Set_Above255_ThrowsArgumentOutOfRange()
    {
        TerminalPalette palette = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => palette[256] = TerminalColor.Red);
    }

    [Fact]
    public void Palette_SetAnsiColor_UpdatesColor()
    {
        TerminalPalette palette = new();
        TerminalColor expected = TerminalColor.FromArgb(0xAB, 0xCD, 0xEF);

        palette.SetAnsiColor(100, expected);

        Assert.Equal(expected, palette[100]);
    }

    [Fact]
    public void Palette_GetColor_ReturnsColorForValidIndex()
    {
        TerminalPalette palette = new();

        TerminalColor color = palette.GetColor(7);

        Assert.Equal(palette[7], color);
    }

    [Fact]
    public void Palette_GetColor_ClampsNegativeToZero()
    {
        TerminalPalette palette = new();

        TerminalColor color = palette.GetColor(-5);

        Assert.Equal(palette[0], color);
    }

    [Fact]
    public void Palette_GetColor_ClampsAbove255To255()
    {
        TerminalPalette palette = new();

        TerminalColor color = palette.GetColor(500);

        Assert.Equal(palette[255], color);
    }

    [Fact]
    public void Palette_AsSpan_ReturnsAll256Colors()
    {
        TerminalPalette palette = new();

        Span<TerminalColor> span = palette.AsSpan();

        Assert.Equal(256, span.Length);
        Assert.Equal(palette[0], span[0]);
        Assert.Equal(palette[255], span[255]);
    }

    [Fact]
    public void Palette_LoadXtermDefaults_ResetsModifiedColors()
    {
        TerminalPalette palette = new();
        TerminalColor original = palette[4];

        palette[4] = TerminalColor.HotPink;
        Assert.NotEqual(original, palette[4]);

        palette.LoadXtermDefaults();

        Assert.Equal(original, palette[4]);
    }

    #endregion

    #region TerminalOptions

    [Fact]
    public void Options_Defaults_AreExpectedValues()
    {
        TerminalOptions options = new();

        Assert.NotNull(options.Palette);
        Assert.Equal(TerminalColor.White, options.DefaultForeground);
        Assert.Equal(TerminalColor.Black, options.DefaultBackground);
        Assert.Equal(10000, options.ScrollbackMaxLines);
        Assert.Equal(1.0, options.LineHeight);
        Assert.Null(options.FontFallback);
        Assert.True(options.BoldAsBright);
        Assert.False(options.AllowOSCColorQueries);
        Assert.True(options.AllowClipboard);
    }

    [Fact]
    public void Options_Clone_CopiesAllPropertyValues()
    {
        TerminalOptions original = new()
        {
            DefaultForeground = TerminalColor.Red,
            DefaultBackground = TerminalColor.Blue,
            ScrollbackMaxLines = 5000,
            LineHeight = 1.25,
            FontFallback = ["Arial", "Consolas"],
            BoldAsBright = false,
            AllowOSCColorQueries = true,
            AllowClipboard = false,
        };

        TerminalOptions clone = original.Clone();

        Assert.Equal(original.DefaultForeground, clone.DefaultForeground);
        Assert.Equal(original.DefaultBackground, clone.DefaultBackground);
        Assert.Equal(original.ScrollbackMaxLines, clone.ScrollbackMaxLines);
        Assert.Equal(original.LineHeight, clone.LineHeight);
        Assert.Equal(original.FontFallback, clone.FontFallback);
        Assert.Equal(original.BoldAsBright, clone.BoldAsBright);
        Assert.Equal(original.AllowOSCColorQueries, clone.AllowOSCColorQueries);
        Assert.Equal(original.AllowClipboard, clone.AllowClipboard);
    }

    [Fact]
    public void Options_Clone_SharesPaletteReference()
    {
        TerminalOptions original = new();
        TerminalOptions clone = original.Clone();

        Assert.Same(original.Palette, clone.Palette);
    }

    [Fact]
    public void Options_Clone_SharesFontFallbackReference()
    {
        string[] fallback = ["Arial", "Consolas"];
        TerminalOptions original = new() { FontFallback = fallback };
        TerminalOptions clone = original.Clone();

        Assert.Same(original.FontFallback, clone.FontFallback);
    }

    [Fact]
    public void Options_SettingProperties_UpdatesValues()
    {
        TerminalPalette palette = new();
        TerminalOptions options = new();

        options.Palette = palette;
        options.DefaultForeground = TerminalColor.Red;
        options.DefaultBackground = TerminalColor.Blue;
        options.ScrollbackMaxLines = 2500;
        options.LineHeight = 1.5;
        options.FontFallback = ["Segoe UI"];
        options.BoldAsBright = false;
        options.AllowOSCColorQueries = true;
        options.AllowClipboard = false;

        Assert.Same(palette, options.Palette);
        Assert.Equal(TerminalColor.Red, options.DefaultForeground);
        Assert.Equal(TerminalColor.Blue, options.DefaultBackground);
        Assert.Equal(2500, options.ScrollbackMaxLines);
        Assert.Equal(1.5, options.LineHeight);
        Assert.Equal(["Segoe UI"], options.FontFallback);
        Assert.False(options.BoldAsBright);
        Assert.True(options.AllowOSCColorQueries);
        Assert.False(options.AllowClipboard);
    }

    #endregion
}
