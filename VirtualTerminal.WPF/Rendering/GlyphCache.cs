using System.Text;
using System.Windows;
using System.Windows.Media;

namespace VirtualTerminal.Rendering;

/// <summary>
/// Resolves <see cref="GlyphTypeface"/> instances for the primary font and fallback families,
/// maps codepoints to glyph indices (with fallback), and exposes font metrics for grid layout.
/// Glyph lookups are cached per codepoint.
/// </summary>
public sealed class GlyphCache : IDisposable
{
    private static readonly FontFamily _defaultFont = new FontFamily("Consolas");

    private readonly List<GlyphTypeface> _fallbacks = [];
    private readonly Dictionary<int, (GlyphTypeface Tf, ushort Glyph)> _cache = [];

    private GlyphTypeface? _primary;
    private FontFamily _fontFamily = _defaultFont;

    private double _emSize;
    private double _scale = 1.0;

    private double _cellWidth;
    private double _cellHeight;
    private double _ascent;
    private double _underlinePos;
    private double _underlineThickness;
    private double _strikePos;
    private double _strikeThickness;

    public bool IsValid => _primary is not null;
    public double CellWidth => _cellWidth;
    public double CellHeight => _cellHeight;
    public double Ascent => _ascent;
    public double UnderlinePosition => _underlinePos;
    public double UnderlineThickness => _underlineThickness;
    public double StrikethroughPosition => _strikePos;
    public double StrikethroughThickness => _strikeThickness;
    public GlyphTypeface Primary => _primary ?? throw new InvalidOperationException("GlyphCache not configured.");

    /// <summary>Rebuilds the cache for the given primary family, size, line-height, and fallbacks.</summary>
    public void Configure(string family, double emSize, double lineHeight, IReadOnlyList<string>? fallback)
    {
        _fallbacks.Clear();
        _cache.Clear();

        _emSize = emSize;
        _fontFamily = string.IsNullOrWhiteSpace(family)
            ? _defaultFont : new FontFamily(family);

        _primary = ResolveTypeface(_fontFamily, FontWeights.Normal, FontStyles.Normal, FontStretches.Normal);

        if (fallback is { Count: > 0 })
        {
            foreach (string? f in fallback)
            {
                if (string.IsNullOrWhiteSpace(f))
                    continue;

                GlyphTypeface tf = ResolveTypeface(new FontFamily(f), FontWeights.Normal, FontStyles.Normal, FontStretches.Normal);
                _fallbacks.Add(tf);
            }
        }

        MeasureMetrics(lineHeight);
    }

    private static GlyphTypeface ResolveTypeface(FontFamily fontFamily, FontWeight weight, FontStyle style, FontStretch stretch)
    {
        Typeface typeface = new(fontFamily, style, weight, stretch);
        if (!typeface.TryGetGlyphTypeface(out GlyphTypeface? glyphTypeface) || glyphTypeface is null)
            throw new InvalidOperationException($"Unable to resolve glyph typeface for '{fontFamily.Source}'.");

        return glyphTypeface;
    }

    private void MeasureMetrics(double lineHeight)
    {
        GlyphTypeface tf = Primary;
        _scale = _emSize;

        ushort rawGlyph = GetRawGlyph(tf, 'M');
        double advance = tf.AdvanceWidths[rawGlyph];

        if (advance <= 0)
            advance = 0.6;

        double lineSpacing = _fontFamily.LineSpacing > 0 ? _fontFamily.LineSpacing : 1.0;

        _cellWidth = advance * _scale;
        _underlinePos = tf.UnderlinePosition * _scale;
        _strikePos = tf.StrikethroughPosition * _scale;

        _ascent = Math.Clamp(tf.Baseline * _scale, 0, int.MaxValue);
        _cellHeight = Math.Ceiling(lineSpacing * _scale * Math.Max(0.5, lineHeight));

        _underlineThickness = Math.Max(1, tf.UnderlineThickness * _scale);
        _strikeThickness = Math.Max(1, tf.StrikethroughThickness * _scale);
    }

    /// <summary>Resolves the (typeface, glyph) for a codepoint, trying fallbacks when absent.</summary>
    public (GlyphTypeface Tf, ushort Glyph) GetGlyph(Rune rune)
    {
        int cp = rune.Value;
        if (_cache.TryGetValue(cp, out (GlyphTypeface Tf, ushort Glyph) hit))
            return hit;

        GlyphTypeface primary = Primary;
        ushort rawGlyph = GetRawGlyph(primary, cp);
        GlyphTypeface tf = primary;

        if (rawGlyph == 0)
        {
            foreach (GlyphTypeface fb in _fallbacks)
            {
                ushort fg = GetRawGlyph(fb, cp);
                if (fg != 0)
                {
                    rawGlyph = fg;
                    tf = fb;
                    break;
                }
            }
        }

        // Defensive: a font's cmap should never return an out-of-range glyph, but if it
        // does, fall back to the primary font's missing-glyph glyph (index 0).
        if (rawGlyph >= tf.GlyphCount)
        {
            tf = primary;
            rawGlyph = 0;
        }

        (GlyphTypeface tf, ushort g) result = (tf, rawGlyph);
        _cache[cp] = result;
        return result;
    }

    /// <summary>Weight/style variant of the primary typeface for bold/italic runs.</summary>
    public GlyphTypeface GetVariant(FontWeight weight, FontStyle style)
        => ResolveTypeface(_fontFamily, weight, style, FontStretches.Normal);

    private static ushort GetRawGlyph(GlyphTypeface tf, int codepoint)
    {
        if (tf.GlyphCount == 0)
            return 0;

        if (tf.CharacterToGlyphMap.TryGetValue(codepoint, out ushort glyph))
            return glyph;

        return 0;
    }

    public void Dispose()
    {
        _primary = null;
        _fallbacks.Clear();
        _cache.Clear();
    }
}
