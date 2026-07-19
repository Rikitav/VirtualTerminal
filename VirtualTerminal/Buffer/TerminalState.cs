using System.Drawing;
using VirtualTerminal.Enums;
using VirtualTerminal.Model;

namespace VirtualTerminal.Buffer;

/// <summary>How a color attribute was specified, so <see cref="CharAttributes.ToCellStyle"/> can resolve it.</summary>
public enum ColorSource
{
    /// <summary>The default foreground/background (resolved by the renderer from options).</summary>
    Default,

    /// <summary>An ANSI palette index (0–255); brightened by bold-as-bright when applicable.</summary>
    Indexed,

    /// <summary>A direct RGB value (truecolor).</summary>
    Direct,
}

/// <summary>
/// The mutable current character-attribute state driven by SGR. Kept separate from the
/// per-cell <see cref="CellRenderStyle"/> so colors can be resolved (palette lookup,
/// bold-as-bright, default-color sentinels) at the moment a cell is written.
/// </summary>
public struct CharAttributes
{
    public ColorSource ForegroundSource;
    public int ForegroundIndex;
    public Color ForegroundRgb;

    public ColorSource BackgroundSource;
    public int BackgroundIndex;
    public Color BackgroundRgb;

    public ColorSource UnderlineSource;
    public int UnderlineIndex;
    public Color UnderlineRgb;

    public bool Bold;
    public bool Faint;
    public bool Italic;
    public bool Conceal;
    public bool Inverse;
    public bool Strikethrough;
    public bool Overline;
    public bool Fraktur;
    public bool Framed;
    public bool Encircled;
    public bool DoublyUnderlined;

    public Underline UnderlineStyle;
    public Blink Blink;
    public byte HyperlinkId;

    public static CharAttributes Default => new()
    {
        ForegroundSource = ColorSource.Default,
        BackgroundSource = ColorSource.Default,
        UnderlineSource = ColorSource.Default,
        UnderlineStyle = Underline.None,
        Blink = Blink.None,
    };

    public void Reset()
    {
        ForegroundSource = ColorSource.Default;
        BackgroundSource = ColorSource.Default;
        UnderlineSource = ColorSource.Default;
        Bold = Faint = Italic = Conceal = Inverse = Strikethrough = Overline = Fraktur = Framed = Encircled = DoublyUnderlined = false;
        UnderlineStyle = Underline.None;
        Blink = Blink.None;
        HyperlinkId = 0;
    }

    /// <summary>Resolves the current attributes into a <see cref="CellRenderStyle"/> for a cell.</summary>
    public readonly CellRenderStyle ToCellStyle(TerminalPalette palette, bool boldAsBright)
    {
        CellStyleFlags flags = 0;
        Color fg, bg, ul;

        if (ForegroundSource == ColorSource.Default)
        {
            flags |= CellStyleFlags.ForegroundIsDefault;
            fg = Color.White;
        }
        else if (ForegroundSource == ColorSource.Indexed)
        {
            int idx = boldAsBright && Bold && ForegroundIndex < 8 ? ForegroundIndex + 8 : ForegroundIndex;
            fg = palette.GetColor(idx);
        }
        else
        {
            fg = ForegroundRgb;
        }

        if (BackgroundSource == ColorSource.Default)
        {
            flags |= CellStyleFlags.BackgroundIsDefault;
            bg = Color.Black;
        }
        else if (BackgroundSource == ColorSource.Indexed)
        {
            bg = palette.GetColor(BackgroundIndex);
        }
        else
        {
            bg = BackgroundRgb;
        }

        if (UnderlineSource == ColorSource.Default)
        {
            flags |= CellStyleFlags.UnderlineColorIsDefault;
            ul = Color.White;
        }
        else if (UnderlineSource == ColorSource.Indexed)
        {
            ul = palette.GetColor(UnderlineIndex);
        }
        else
        {
            ul = UnderlineRgb;
        }

        if (Bold)
            flags |= CellStyleFlags.Bold;
        if (Faint)
            flags |= CellStyleFlags.Faint;
        if (Italic)
            flags |= CellStyleFlags.Italic;
        if (Conceal)
            flags |= CellStyleFlags.Conceal;
        if (Inverse)
            flags |= CellStyleFlags.Inverse;
        if (Strikethrough)
            flags |= CellStyleFlags.Strikethrough;
        if (Overline)
            flags |= CellStyleFlags.Overline;
        if (Fraktur)
            flags |= CellStyleFlags.Fraktur;
        if (Framed)
            flags |= CellStyleFlags.Framed;
        if (Encircled)
            flags |= CellStyleFlags.Encircled;
        if (DoublyUnderlined)
            flags |= CellStyleFlags.DoublyUnderlined;
        if (Blink == Blink.Slow)
            flags |= CellStyleFlags.BlinkSlow;
        else if (Blink == Blink.Rapid)
            flags |= CellStyleFlags.BlinkRapid;

        return new CellRenderStyle
        {
            Foreground = fg,
            Background = bg,
            UnderlineColor = ul,
            Flags = flags,
            UnderlineStyle = UnderlineStyle,
            HyperlinkId = HyperlinkId,
        };
    }
}

/// <summary>Cursor and saved-state owned by the decoder, outside the screen buffer.</summary>
public sealed class TerminalState
{
    public int CursorX;
    public int CursorY;
    public bool WrapPending;

    public CharAttributes Attributes = CharAttributes.Default;
    public TerminalModes Modes = new();

    // DECSC saved state.
    public int SavedCursorX;
    public int SavedCursorY;
    public CharAttributes SavedAttributes = CharAttributes.Default;
    public bool SavedOriginMode;
    public bool SavedWrapPending;

    public void SaveCursor()
    {
        SavedCursorX = CursorX;
        SavedCursorY = CursorY;
        SavedAttributes = Attributes;
        SavedOriginMode = Modes.OriginMode;
        SavedWrapPending = WrapPending;
    }

    public void RestoreCursor()
    {
        CursorX = SavedCursorX;
        CursorY = SavedCursorY;
        Attributes = SavedAttributes;
        Modes.OriginMode = SavedOriginMode;
        WrapPending = SavedWrapPending;
    }
}
