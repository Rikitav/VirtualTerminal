using System.Drawing;
using VirtualTerminal.Buffer;
using VirtualTerminal.Model;

namespace VirtualTerminal.Options;

/// <summary>
/// Customization knobs for a terminal instance: palette, default colors, cursor, scrollback,
/// layout, font fallback, and consent gates for OSC features. Typically bound to a UI control
/// via styled properties and/or set directly on the session's decoder.
/// </summary>
public sealed class TerminalOptions
{
    /// <summary>The 256-entry ANSI palette (mutable; OSC 4/10/11/12 recolor entries).</summary>
    public TerminalPalette Palette { get; set; } = new();

    /// <summary>Default foreground color used when no SGR foreground is active.</summary>
    public Color DefaultForeground { get; set; } = Color.White;

    /// <summary>Default background color used when no SGR background is active.</summary>
    public Color DefaultBackground { get; set; } = Color.Black;

    /// <summary>Default cursor color.</summary>
    public Color DefaultCursorColor { get; set; } = Color.White;

    /// <summary>Cursor shape (also settable via DECSCUSR).</summary>
    public CursorShape CursorShape { get; set; } = CursorShape.Block;

    /// <summary>Whether the cursor blinks by default.</summary>
    public bool CursorBlink { get; set; } = true;

    /// <summary>Cursor blink interval in milliseconds.</summary>
    public int CursorBlinkIntervalMs { get; set; } = 530;

    /// <summary>Maximum scrollback lines retained in the ring buffer.</summary>
    public int ScrollbackMaxLines { get; set; } = 10000;

    /// <summary>Line height multiplier (1.0 = default).</summary>
    public double LineHeight { get; set; } = 1.0;

    /// <summary>Font family names tried when the primary font lacks a glyph (CJK/emoji fallback).</summary>
    public string[]? FontFallback { get; set; }

    /// <summary>SGR bold brightens ANSI 0–7 foreground to 8–15.</summary>
    public bool BoldAsBright { get; set; } = true;

    /// <summary>Allow OSC 4/10/11/12 color queries to read back current colors.</summary>
    public bool AllowOSCColorQueries { get; set; }

    /// <summary>Allow OSC 52 clipboard access.</summary>
    public bool AllowClipboard { get; set; } = true;

    /// <summary>Creates a shallow copy of these options.</summary>
    /// <returns>A new options instance with the same values.</returns>
    public TerminalOptions Clone() => new()
    {
        Palette = Palette,
        DefaultForeground = DefaultForeground,
        DefaultBackground = DefaultBackground,
        DefaultCursorColor = DefaultCursorColor,
        CursorShape = CursorShape,
        CursorBlink = CursorBlink,
        CursorBlinkIntervalMs = CursorBlinkIntervalMs,
        ScrollbackMaxLines = ScrollbackMaxLines,
        LineHeight = LineHeight,
        FontFallback = FontFallback,
        BoldAsBright = BoldAsBright,
        AllowOSCColorQueries = AllowOSCColorQueries,
        AllowClipboard = AllowClipboard,
    };
}
