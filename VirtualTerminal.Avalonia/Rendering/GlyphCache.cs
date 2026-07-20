using Avalonia.Media;
using Avalonia.Media.Fonts.Tables.Cmap;
using System.Text;

namespace VirtualTerminal.Rendering;

/// <summary>
/// Resolves <see cref="GlyphTypeface"/> instances for the primary font and fallback families,
/// maps codepoints to glyph indices (with fallback), and exposes font metrics for grid layout.
/// Glyph lookups are cached per codepoint.
/// </summary>
public sealed class GlyphCache : IDisposable
{
    private GlyphTypeface? _primary;
    private readonly List<GlyphTypeface> _fallbacks = [];
    private readonly Dictionary<uint, (GlyphTypeface Tf, ushort Glyph)> _cache = [];

    private double _emSize;
    private double _scale = 1.0;

    private double _cellWidth;
    private double _cellHeight;
    private double _ascent;
    private double _underlinePos;
    private double _underlineThickness;
    private double _strikePos;
    private double _strikeThickness;

    /// <summary>Gets a value indicating whether the cache has been configured with a primary typeface.</summary>
    public bool IsValid => _primary is not null;

    /// <summary>Gets the width of a single terminal cell in pixels.</summary>
    public double CellWidth => _cellWidth;

    /// <summary>Gets the height of a single terminal cell in pixels.</summary>
    public double CellHeight => _cellHeight;

    /// <summary>Gets the font ascent above the baseline in pixels.</summary>
    public double Ascent => _ascent;

    /// <summary>Gets the underline position relative to the baseline in pixels.</summary>
    public double UnderlinePosition => _underlinePos;

    /// <summary>Gets the underline thickness in pixels.</summary>
    public double UnderlineThickness => _underlineThickness;

    /// <summary>Gets the strikethrough position relative to the baseline in pixels.</summary>
    public double StrikethroughPosition => _strikePos;

    /// <summary>Gets the strikethrough thickness in pixels.</summary>
    public double StrikethroughThickness => _strikeThickness;

    /// <summary>Gets the primary configured <see cref="GlyphTypeface"/>.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the cache has not been configured.</exception>
    public GlyphTypeface Primary => _primary ?? throw new InvalidOperationException("GlyphCache not configured.");

    /// <summary>Rebuilds the cache for the given primary family, size, line-height, and fallbacks.</summary>
    public void Configure(string family, double emSize, double lineHeight, IReadOnlyList<string>? fallback)
    {
        foreach (GlyphTypeface fb in _fallbacks)
            fb.Dispose();

        _fallbacks.Clear();
        _primary?.Dispose();
        _cache.Clear();

        _emSize = emSize;
        _primary = ResolveTypeface(family, FontWeight.Normal, FontStyle.Normal, FontStretch.Normal);

        if (fallback is { Count: > 0 })
        {
            foreach (var f in fallback)
            {
                if (string.IsNullOrWhiteSpace(f))
                    continue;
                GlyphTypeface tf = ResolveTypeface(f, FontWeight.Normal, FontStyle.Normal, FontStretch.Normal);
                _fallbacks.Add(tf);
            }
        }

        MeasureMetrics(lineHeight);
    }

    private static GlyphTypeface ResolveTypeface(string family, FontWeight weight, FontStyle style, FontStretch stretch)
    {
        Typeface typeface = new Typeface(string.IsNullOrWhiteSpace(family) ? FontFamily.Default : new FontFamily(family), style, weight, stretch);
        return typeface.GlyphTypeface;
    }

    private void MeasureMetrics(double lineHeight)
    {
        GlyphTypeface tf = Primary;
        FontMetrics m = tf.Metrics;
        _scale = m.DesignEmHeight > 0 ? _emSize / m.DesignEmHeight : 1.0;

        // Reference advance (use 'M', or any present glyph) for the cell width.
        ushort refGlyph = GetRawGlyph(tf, 'M');
        if (!tf.TryGetHorizontalGlyphAdvance(refGlyph, out ushort adv) || adv == 0)
            adv = (ushort)(m.DesignEmHeight > 0 ? m.DesignEmHeight * 0.6 : 600);

        _cellWidth = adv * _scale;

        int lineSpacing = m.LineSpacing > 0 ? m.LineSpacing : (m.Ascent - m.Descent);
        _cellHeight = Math.Ceiling(lineSpacing * _scale * Math.Max(0.5, lineHeight));
        _ascent = Math.Clamp(m.Ascent * _scale, 0, int.MaxValue);
        _underlinePos = m.UnderlinePosition * _scale;
        _underlineThickness = Math.Max(1, m.UnderlineThickness * _scale);
        _strikePos = m.StrikethroughPosition * _scale;
        _strikeThickness = Math.Max(1, m.StrikethroughThickness * _scale);
    }

    /// <summary>Resolves the (typeface, glyph) for a codepoint, trying fallbacks when absent.</summary>
    public (GlyphTypeface Tf, ushort Glyph) GetGlyph(Rune rune)
    {
        uint cp = (uint)rune.Value;
        if (_cache.TryGetValue(cp, out (GlyphTypeface Tf, ushort Glyph) hit))
            return hit;

        GlyphTypeface primary = Primary;
        ushort g = GetRawGlyph(primary, cp);
        GlyphTypeface tf = primary;
        if (g == 0)
        {
            foreach (GlyphTypeface fb in _fallbacks)
            {
                ushort fg = GetRawGlyph(fb, cp);
                if (fg != 0)
                {
                    g = fg;
                    tf = fb;
                    break;
                }
            }
        }

        (GlyphTypeface tf, ushort g) result = (tf, g);
        _cache[cp] = result;
        return result;
    }

    /// <summary>Weight/style variant of the primary typeface for bold/italic runs.</summary>
    public GlyphTypeface GetVariant(FontWeight weight, FontStyle style)
        => ResolveTypeface(Primary.FamilyName, weight, style, FontStretch.Normal);

    private static ushort GetRawGlyph(GlyphTypeface tf, uint codepoint)
    {
        CharacterToGlyphMap map = tf.CharacterToGlyphMap;
        return map[(int)codepoint];
    }

    private static ushort GetRawGlyph(GlyphTypeface tf, char c)
        => GetRawGlyph(tf, (uint)c);

    /// <summary>Releases the primary and fallback typefaces and clears the glyph cache.</summary>
    public void Dispose()
    {
        _primary?.Dispose();
        foreach (GlyphTypeface fb in _fallbacks)
            fb.Dispose();

        _fallbacks.Clear();
        _cache.Clear();
    }
}
