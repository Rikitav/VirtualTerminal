using System.Drawing;
using VirtualTerminal.Enums;

namespace VirtualTerminal.Model;

/// <summary>
/// The render-relevant attributes of a cell, intentionally excluding the character glyph.
/// The TerminalRenderer coalesces consecutive cells that share an equal
/// <see cref="CellRenderStyle"/> into a single glyph run, so <see cref="Equals"/> and
/// <see cref="GetHashCode"/> must cover every field.
/// </summary>
public readonly struct CellRenderStyle : IEquatable<CellRenderStyle>
{
    public Color Foreground { get; init; }
    public Color Background { get; init; }
    public Color UnderlineColor { get; init; }

    /// <summary>Bitfield of boolean attributes plus default-color sentinels and width flags.</summary>
    public CellStyleFlags Flags { get; init; }

    /// <summary>Underline style (single/double/curly/dotted/dashed).</summary>
    public Underline UnderlineStyle { get; init; }

    /// <summary>Index into the buffer's hyperlink table (0 = none).</summary>
    public byte HyperlinkId { get; init; }

    public bool ForegroundIsDefault => (Flags & CellStyleFlags.ForegroundIsDefault) != 0;
    public bool BackgroundIsDefault => (Flags & CellStyleFlags.BackgroundIsDefault) != 0;
    public bool UnderlineColorIsDefault => (Flags & CellStyleFlags.UnderlineColorIsDefault) != 0;
    public bool Bold => (Flags & CellStyleFlags.Bold) != 0;
    public bool Faint => (Flags & CellStyleFlags.Faint) != 0;
    public bool Italic => (Flags & CellStyleFlags.Italic) != 0;
    public bool Conceal => (Flags & CellStyleFlags.Conceal) != 0;
    public bool Inverse => (Flags & CellStyleFlags.Inverse) != 0;
    public bool Strikethrough => (Flags & CellStyleFlags.Strikethrough) != 0;
    public bool Overline => (Flags & CellStyleFlags.Overline) != 0;
    public bool Fraktur => (Flags & CellStyleFlags.Fraktur) != 0;
    public bool Framed => (Flags & CellStyleFlags.Framed) != 0;
    public bool Encircled => (Flags & CellStyleFlags.Encircled) != 0;
    public bool DoublyUnderlined => (Flags & CellStyleFlags.DoublyUnderlined) != 0;
    public bool Wide => (Flags & CellStyleFlags.Wide) != 0;
    public bool Continuation => (Flags & CellStyleFlags.Continuation) != 0;

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
    None = 0,
    Bold = 1 << 0,
    Faint = 1 << 1,
    Italic = 1 << 2,
    Conceal = 1 << 3,
    Inverse = 1 << 4,
    Strikethrough = 1 << 5,
    Overline = 1 << 6,
    Fraktur = 1 << 7,
    Framed = 1 << 8,
    Encircled = 1 << 9,
    ForegroundIsDefault = 1 << 10,
    BackgroundIsDefault = 1 << 11,
    UnderlineColorIsDefault = 1 << 12,
    Wide = 1 << 13,
    Continuation = 1 << 14,
    BlinkSlow = 1 << 15,
    BlinkRapid = 1 << 16,
    DoublyUnderlined = 1 << 17,
}
