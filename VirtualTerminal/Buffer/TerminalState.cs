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
    /// <summary>How the foreground color was specified.</summary>
    public ColorSource ForegroundSource;

    /// <summary>The ANSI palette index for the foreground when <see cref="ForegroundSource"/> is <see cref="ColorSource.Indexed"/>.</summary>
    public int ForegroundIndex;

    /// <summary>The direct RGB foreground color when <see cref="ForegroundSource"/> is <see cref="ColorSource.Direct"/>.</summary>
    public Color ForegroundRgb;

    /// <summary>How the background color was specified.</summary>
    public ColorSource BackgroundSource;

    /// <summary>The ANSI palette index for the background when <see cref="BackgroundSource"/> is <see cref="ColorSource.Indexed"/>.</summary>
    public int BackgroundIndex;

    /// <summary>The direct RGB background color when <see cref="BackgroundSource"/> is <see cref="ColorSource.Direct"/>.</summary>
    public Color BackgroundRgb;

    /// <summary>How the underline color was specified.</summary>
    public ColorSource UnderlineSource;

    /// <summary>The ANSI palette index for the underline color when <see cref="UnderlineSource"/> is <see cref="ColorSource.Indexed"/>.</summary>
    public int UnderlineIndex;

    /// <summary>The direct RGB underline color when <see cref="UnderlineSource"/> is <see cref="ColorSource.Direct"/>.</summary>
    public Color UnderlineRgb;

    /// <summary>SGR 1: bold (bright).</summary>
    public bool Bold;

    /// <summary>SGR 2: faint (dim).</summary>
    public bool Faint;

    /// <summary>SGR 3: italic.</summary>
    public bool Italic;

    /// <summary>SGR 8: conceal (hide text).</summary>
    public bool Conceal;

    /// <summary>SGR 7: inverse video (swap foreground and background).</summary>
    public bool Inverse;

    /// <summary>SGR 9: strikethrough.</summary>
    public bool Strikethrough;

    /// <summary>SGR 53: overline.</summary>
    public bool Overline;

    /// <summary>SGR 20: Fraktur typeface.</summary>
    public bool Fraktur;

    /// <summary>SGR 51: framed.</summary>
    public bool Framed;

    /// <summary>SGR 52: encircled.</summary>
    public bool Encircled;

    /// <summary>SGR 21: doubly underlined.</summary>
    public bool DoublyUnderlined;

    /// <summary>The underline style.</summary>
    public Underline UnderlineStyle;

    /// <summary>The blink mode.</summary>
    public Blink Blink;

    /// <summary>Identifier of the active hyperlink, or 0 if none.</summary>
    public byte HyperlinkId;

    /// <summary>Gets the default attribute state.</summary>
    public static CharAttributes Default => new()
    {
        ForegroundSource = ColorSource.Default,
        BackgroundSource = ColorSource.Default,
        UnderlineSource = ColorSource.Default,
        UnderlineStyle = Underline.None,
        Blink = Blink.None,
    };

    /// <summary>Resets all attributes to their default values.</summary>
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
    /// <summary>The zero-based cursor column.</summary>
    public int CursorX;

    /// <summary>The zero-based cursor row.</summary>
    public int CursorY;

    /// <summary>Whether the cursor wrapped to the next line and the next print should start there.</summary>
    public bool WrapPending;

    /// <summary>The current character attributes applied to newly written cells.</summary>
    public CharAttributes Attributes = CharAttributes.Default;

    /// <summary>The current terminal modes.</summary>
    public TerminalModes Modes = new();

    // DECSC saved state.
    /// <summary>The saved cursor column from <see cref="SaveCursor"/>.</summary>
    public int SavedCursorX;

    /// <summary>The saved cursor row from <see cref="SaveCursor"/>.</summary>
    public int SavedCursorY;

    /// <summary>The saved character attributes from <see cref="SaveCursor"/>.</summary>
    public CharAttributes SavedAttributes = CharAttributes.Default;

    /// <summary>The saved origin mode from <see cref="SaveCursor"/>.</summary>
    public bool SavedOriginMode;

    /// <summary>The saved wrap-pending flag from <see cref="SaveCursor"/>.</summary>
    public bool SavedWrapPending;

    /// <summary>Saves the current cursor position, attributes, origin mode, and wrap state.</summary>
    public void SaveCursor()
    {
        SavedCursorX = CursorX;
        SavedCursorY = CursorY;
        SavedAttributes = Attributes;
        SavedOriginMode = Modes.OriginMode;
        SavedWrapPending = WrapPending;
    }

    /// <summary>Restores the cursor position, attributes, origin mode, and wrap state previously saved by <see cref="SaveCursor"/>.</summary>
    public void RestoreCursor()
    {
        CursorX = SavedCursorX;
        CursorY = SavedCursorY;
        Attributes = SavedAttributes;
        Modes.OriginMode = SavedOriginMode;
        WrapPending = SavedWrapPending;
    }
}
