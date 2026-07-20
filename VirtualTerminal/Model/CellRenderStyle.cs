using System.Drawing;
using VirtualTerminal.Enums;

namespace VirtualTerminal.Model;

/// <summary>
/// The render-relevant attributes of a cell, intentionally excluding the character glyph.
/// The terminal renderer coalesces consecutive cells that share an equal
/// <see cref="CellRenderStyle"/> into a single glyph run, so equality and
/// hashing must cover every field.
/// </summary>
public readonly struct CellRenderStyle : IEquatable<CellRenderStyle>
{
    /// <summary>Gets the foreground color.</summary>
    public Color Foreground { get; init; }

    /// <summary>Gets the background color.</summary>
    public Color Background { get; init; }

    /// <summary>Gets the underline color.</summary>
    public Color UnderlineColor { get; init; }

    /// <summary>Bitfield of boolean attributes plus default-color sentinels and width flags.</summary>
    public CellStyleFlags Flags { get; init; }

    /// <summary>Underline style (single/double/curly/dotted/dashed).</summary>
    public Underline UnderlineStyle { get; init; }

    /// <summary>Index into the buffer's hyperlink table (0 = none).</summary>
    public byte HyperlinkId { get; init; }

    /// <summary>Gets a value indicating whether the foreground uses the terminal default color.</summary>
    public bool ForegroundIsDefault => (Flags & CellStyleFlags.ForegroundIsDefault) != 0;

    /// <summary>Gets a value indicating whether the background uses the terminal default color.</summary>
    public bool BackgroundIsDefault => (Flags & CellStyleFlags.BackgroundIsDefault) != 0;

    /// <summary>Gets a value indicating whether the underline color uses the terminal default color.</summary>
    public bool UnderlineColorIsDefault => (Flags & CellStyleFlags.UnderlineColorIsDefault) != 0;

    /// <summary>Gets a value indicating whether the text is bold (or intense).</summary>
    public bool Bold => (Flags & CellStyleFlags.Bold) != 0;

    /// <summary>Gets a value indicating whether the text is faint (dim).</summary>
    public bool Faint => (Flags & CellStyleFlags.Faint) != 0;

    /// <summary>Gets a value indicating whether the text is italic.</summary>
    public bool Italic => (Flags & CellStyleFlags.Italic) != 0;

    /// <summary>Gets a value indicating whether the text is concealed (hidden).</summary>
    public bool Conceal => (Flags & CellStyleFlags.Conceal) != 0;

    /// <summary>Gets a value indicating whether foreground and background colors are swapped.</summary>
    public bool Inverse => (Flags & CellStyleFlags.Inverse) != 0;

    /// <summary>Gets a value indicating whether the text is struck through.</summary>
    public bool Strikethrough => (Flags & CellStyleFlags.Strikethrough) != 0;

    /// <summary>Gets a value indicating whether the text has an overline.</summary>
    public bool Overline => (Flags & CellStyleFlags.Overline) != 0;

    /// <summary>Gets a value indicating whether the text uses a Fraktur (blackletter) typeface.</summary>
    public bool Fraktur => (Flags & CellStyleFlags.Fraktur) != 0;

    /// <summary>Gets a value indicating whether the cell is framed.</summary>
    public bool Framed => (Flags & CellStyleFlags.Framed) != 0;

    /// <summary>Gets a value indicating whether the cell is encircled.</summary>
    public bool Encircled => (Flags & CellStyleFlags.Encircled) != 0;

    /// <summary>Gets a value indicating whether the text is doubly underlined.</summary>
    public bool DoublyUnderlined => (Flags & CellStyleFlags.DoublyUnderlined) != 0;

    /// <summary>Gets a value indicating whether this cell is the first half of a full-width character.</summary>
    public bool Wide => (Flags & CellStyleFlags.Wide) != 0;

    /// <summary>Gets a value indicating whether this cell is the second (empty) half of a full-width character.</summary>
    public bool Continuation => (Flags & CellStyleFlags.Continuation) != 0;

    /// <summary>Gets the blink style (slow, rapid, or none).</summary>
    public Blink Blink => (Flags & CellStyleFlags.BlinkSlow, Flags & CellStyleFlags.BlinkRapid) switch
    {
        (_, CellStyleFlags.BlinkRapid) => Blink.Rapid,
        (CellStyleFlags.BlinkSlow, _) => Blink.Slow,
        _ => Blink.None,
    };

    /// <summary>A clean default: default foreground/background, no attributes.</summary>
    public static CellRenderStyle Default { get; } = new()
    {
        Foreground = Color.White,
        Background = Color.Black,
        UnderlineColor = Color.White,
        Flags = CellStyleFlags.ForegroundIsDefault | CellStyleFlags.BackgroundIsDefault | CellStyleFlags.UnderlineColorIsDefault,
        UnderlineStyle = Underline.None,
        HyperlinkId = 0,
    };

    /// <inheritdoc/>
    public bool Equals(CellRenderStyle other) =>
        Foreground == other.Foreground
        && Background == other.Background
        && UnderlineColor == other.UnderlineColor
        && Flags == other.Flags
        && UnderlineStyle == other.UnderlineStyle
        && HyperlinkId == other.HyperlinkId;

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is CellRenderStyle other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(Foreground, Background, UnderlineColor, Flags, UnderlineStyle, HyperlinkId);

    /// <inheritdoc/>
    public static bool operator ==(CellRenderStyle left, CellRenderStyle right)
        => left.Equals(right);

    /// <inheritdoc/>
    public static bool operator !=(CellRenderStyle left, CellRenderStyle right)
        => !left.Equals(right);
}

/// <summary>Boolean cell attributes, default-color sentinels, blink, and width flags.</summary>
[Flags]
public enum CellStyleFlags : uint
{
    /// <summary>No style flags set.</summary>
    None = 0,

    /// <summary>SGR bold (increased weight / bright color mapping).</summary>
    Bold = 1 << 0,

    /// <summary>SGR faint (decreased intensity).</summary>
    Faint = 1 << 1,

    /// <summary>SGR italic.</summary>
    Italic = 1 << 2,

    /// <summary>SGR conceal (hidden text).</summary>
    Conceal = 1 << 3,

    /// <summary>SGR inverse video (foreground and background swapped).</summary>
    Inverse = 1 << 4,

    /// <summary>SGR strikethrough.</summary>
    Strikethrough = 1 << 5,

    /// <summary>SGR overline.</summary>
    Overline = 1 << 6,

    /// <summary>SGR Fraktur (blackletter).</summary>
    Fraktur = 1 << 7,

    /// <summary>SGR framed.</summary>
    Framed = 1 << 8,

    /// <summary>SGR encircled.</summary>
    Encircled = 1 << 9,

    /// <summary>Foreground uses the terminal default color.</summary>
    ForegroundIsDefault = 1 << 10,

    /// <summary>Background uses the terminal default color.</summary>
    BackgroundIsDefault = 1 << 11,

    /// <summary>Underline color uses the terminal default color.</summary>
    UnderlineColorIsDefault = 1 << 12,

    /// <summary>Cell is the first half of a full-width character.</summary>
    Wide = 1 << 13,

    /// <summary>Cell is the continuation (second half) of a full-width character.</summary>
    Continuation = 1 << 14,

    /// <summary>SGR slow blink.</summary>
    BlinkSlow = 1 << 15,

    /// <summary>SGR rapid blink.</summary>
    BlinkRapid = 1 << 16,

    /// <summary>SGR doubly underlined.</summary>
    DoublyUnderlined = 1 << 17,
}
