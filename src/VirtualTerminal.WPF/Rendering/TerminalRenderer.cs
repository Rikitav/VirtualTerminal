using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using VirtualTerminal.Buffer;
using VirtualTerminal.Enums;
using VirtualTerminal.Model;
using VirtualTerminal.Options;
using Color = VirtualTerminal.Model.TerminalColor;
using WindowsColor = System.Windows.Media.Color;

namespace VirtualTerminal.Rendering;

/// <summary>
/// Paints terminal rows using <see cref="GlyphRun"/> and procedural box-drawing:
/// coalesces same-style cells into runs, resolves default/inverse/conceal colors,
/// draws backgrounds, rich underlines (single/double/curly/dotted/dashed),
/// strikethrough, overline, framed, encircled, and procedural box/block vectors.
/// Positioning is column-based (monospace advance assumption).
/// </summary>
public sealed class TerminalRenderer : IDisposable
{
    private readonly GlyphCache _glyphs = new();
    private readonly Dictionary<uint, Brush> _brushes = [];
    private readonly List<char> _chars = [];
    private readonly List<ushort> _glyphIndices = [];
    private readonly List<double> _advanceWidths = [];
    private readonly List<ushort> _clusterMap = [];
    private TerminalOptions _options = new();
    private double _emSize = 14;

    /// <summary>Gets a value indicating whether the renderer has been configured with a valid font.</summary>
    public bool IsValid => _glyphs.IsValid;

    /// <summary>Gets the size of one terminal cell in device-independent pixels.</summary>
    public Size CellSize => new Size(_glyphs.CellWidth, _glyphs.CellHeight);

    /// <summary>Gets the baseline offset within a cell.</summary>
    public double Baseline => _glyphs.Ascent;

    /// <summary>Gets the width of one terminal cell.</summary>
    public double CellWidth => _glyphs.CellWidth;

    /// <summary>Gets the height of one terminal cell.</summary>
    public double CellHeight => _glyphs.CellHeight;

    /// <summary>Configures the renderer with the specified font family, size, and terminal options.</summary>
    public void Configure(string family, double emSize, TerminalOptions options)
    {
        _options = options;
        _emSize = emSize;

        _glyphs.Configure(family, emSize, options.LineHeight, options.FontFallback);
        _brushes.Clear();
    }

    /// <summary>Releases the renderer resources.</summary>
    public void Dispose()
        => _glyphs.Dispose();

    /// <summary>Renders a single row into <paramref name="drawingContext"/> (local origin = row top-left).</summary>
    public void RenderRow(DrawingContext drawingContext, Span<TerminalCellInfo> row, TerminalSelection? selection, int rowIndex, float pixelsPerDip)
    {
        double cellW = _glyphs.CellWidth;
        double cellH = _glyphs.CellHeight;
        double baselineY = _glyphs.Ascent;
        int len = row.Length;

        int col = 0;
        while (col < len)
        {
            CellRenderStyle style = row[col].Style;
            bool selected = selection?.Contains(rowIndex, col) ?? false;
            int runStart = col;

            // Coalesce by both style and selection state so selected cells get the
            // highlight colors without bleeding into unselected neighbors.
            while (col < len && row[col].Style == style && (selection?.Contains(rowIndex, col) ?? false) == selected)
                col++;

            int runEnd = col;
            double x = runStart * cellW;

            (Color fg, Color bg) = ResolveColors(style, selected);
            if (!style.BackgroundIsDefault || style.Inverse || selected)
                drawingContext.DrawRectangle(Brush(bg), null, new Rect(x, 0, (runEnd - runStart) * cellW, cellH));

            if (!style.Continuation)
                DrawGlyphRun(drawingContext, row, runStart, runEnd, x, baselineY, cellH, Brush(fg), style, pixelsPerDip);
        }
    }

    private void DrawGlyphRun(DrawingContext drawingContext, Span<TerminalCellInfo> row, int start, int end,
        double x, double baselineY, double cellH, Brush fgBrush, CellRenderStyle style, float pixelsPerDip)
    {
        double cellW = _glyphs.CellWidth;

        GlyphTypeface? current = null;
        _chars.Clear();
        _glyphIndices.Clear();
        _advanceWidths.Clear();
        _clusterMap.Clear();

        double segX = x;
        int segCellCount = 0;
        Span<char> text = stackalloc char[2];

        void Flush()
        {
            if (current is null || _glyphIndices.Count == 0)
            {
                _chars.Clear();
                _glyphIndices.Clear();
                _advanceWidths.Clear();
                _clusterMap.Clear();
                return;
            }

            double segWidth = segCellCount * cellW;
            GlyphRun run = new(
                glyphTypeface: current,
                bidiLevel: 0,
                isSideways: false,
                renderingEmSize: _emSize,
                pixelsPerDip: pixelsPerDip,
                glyphIndices: _glyphIndices.ToArray(),
                baselineOrigin: new Point(segX, baselineY),
                advanceWidths: _advanceWidths.ToArray(),
                glyphOffsets: null,
                characters: _chars.ToArray(),
                deviceFontName: null,
                clusterMap: _clusterMap.ToArray(),
                caretStops: null,
                language: XmlLanguage.GetLanguage("en-us"));

            drawingContext.DrawGlyphRun(fgBrush, run);
            DrawDecorations(drawingContext, style, segX, segWidth, baselineY, cellH);

            segX += segWidth;
            _chars.Clear();
            _glyphIndices.Clear();
            _advanceWidths.Clear();
            _clusterMap.Clear();
            segCellCount = 0;
        }

        for (int i = start; i < end; i++)
        {
            ref readonly TerminalCellInfo cell = ref row[i];
            if (cell.Style.Continuation)
                continue;

            Rune rune = cell.Character;
            double cellPosX = x + (i - start) * cellW;

            // Procedural synthesis for box-drawing and block elements: zero font gaps
            if (BoxDrawing.IsProcedural(rune))
            {
                Flush();
                DrawProceduralGlyph(drawingContext, rune, cellPosX, cellW, cellH, fgBrush);
                DrawDecorations(drawingContext, cell.Style, cellPosX, cellW, baselineY, cellH);
                segX = cellPosX + cellW;
                continue;
            }

            (GlyphTypeface tf, ushort glyph) = _glyphs.GetGlyph(rune);

            if (current is not null && !ReferenceEquals(tf, current))
                Flush();

            current = tf;

            bool encoded = rune.TryEncodeToUtf16(text, out int charsWritten);
            Debug.Assert(encoded && charsWritten > 0);

            ushort glyphIndex = (ushort)_glyphIndices.Count;

            for (int c = 0; c < charsWritten; c++)
            {
                _chars.Add(text[c]);
                _clusterMap.Add(glyphIndex);
            }

            _glyphIndices.Add(glyph);
            _advanceWidths.Add(cellW);
            segCellCount++;
        }

        Flush();
    }

    private void DrawProceduralGlyph(DrawingContext dc, Rune rune, double x, double cellW, double cellH, Brush fgBrush)
    {
        if (BoxDrawing.TryGetBlockRects(rune, out BlockRect[] rects))
        {
            foreach (BlockRect r in rects)
            {
                Brush b = r.Alpha < 0.99 ? new SolidColorBrush(ColorWithAlpha(fgBrush, r.Alpha)) : fgBrush;
                dc.DrawRectangle(b, null, new Rect(x + r.X * cellW, r.Y * cellH, r.Width * cellW, r.Height * cellH));
            }
            return;
        }

        if (BoxDrawing.TryGetBoxArms(rune, out BoxArms arms))
        {
            double cx = x + Math.Floor(cellW / 2.0);
            double cy = Math.Floor(cellH / 2.0);
            double lightThick = Math.Max(1.0, Math.Round(cellW / 10.0));
            double heavyThick = Math.Max(2.0, lightThick * 2.0);

            // Left arm
            if (arms.Left != BoxLineStyle.None)
            {
                double t = arms.Left == BoxLineStyle.Heavy ? heavyThick : lightThick;
                dc.DrawLine(new Pen(fgBrush, t), new Point(x, cy), new Point(cx, cy));
            }
            // Right arm
            if (arms.Right != BoxLineStyle.None)
            {
                double t = arms.Right == BoxLineStyle.Heavy ? heavyThick : lightThick;
                dc.DrawLine(new Pen(fgBrush, t), new Point(cx, cy), new Point(x + cellW, cy));
            }
            // Top arm
            if (arms.Top != BoxLineStyle.None)
            {
                double t = arms.Top == BoxLineStyle.Heavy ? heavyThick : lightThick;
                dc.DrawLine(new Pen(fgBrush, t), new Point(cx, 0), new Point(cx, cy));
            }
            // Bottom arm
            if (arms.Bottom != BoxLineStyle.None)
            {
                double t = arms.Bottom == BoxLineStyle.Heavy ? heavyThick : lightThick;
                dc.DrawLine(new Pen(fgBrush, t), new Point(cx, cy), new Point(cx, cellH));
            }

            // Diagonals
            if (arms.DiagonalSlash)
            {
                dc.DrawLine(new Pen(fgBrush, lightThick), new Point(x, cellH), new Point(x + cellW, 0));
            }
            if (arms.DiagonalBackslash)
            {
                dc.DrawLine(new Pen(fgBrush, lightThick), new Point(x, 0), new Point(x + cellW, cellH));
            }
        }
    }

    private static WindowsColor ColorWithAlpha(Brush brush, double alpha)
    {
        if (brush is SolidColorBrush scb)
        {
            WindowsColor c = scb.Color;
            return WindowsColor.FromArgb((byte)Math.Clamp((int)(c.A * alpha), 0, 255), c.R, c.G, c.B);
        }
        return WindowsColor.FromArgb((byte)(255 * alpha), 255, 255, 255);
    }

    private void DrawDecorations(DrawingContext drawingContext, CellRenderStyle style, double x, double width, double baselineY, double cellH)
    {
        Color fg = style.ForegroundIsDefault ? _options.DefaultForeground : style.Foreground;
        Color decoColor = style.UnderlineColorIsDefault ? fg : style.UnderlineColor;
        Brush decoBrush = Brush(decoColor);
        double thickness = Math.Max(1, _glyphs.UnderlineThickness);

        if (style.UnderlineStyle != Underline.None || style.DoublyUnderlined)
        {
            double y = baselineY - _glyphs.UnderlinePosition;

            if (style.UnderlineStyle == Underline.Double || style.DoublyUnderlined)
            {
                Pen penDbl = new(decoBrush, thickness);
                drawingContext.DrawLine(penDbl, new Point(x, y), new Point(x + width, y));
                drawingContext.DrawLine(penDbl, new Point(x, y - thickness - 1), new Point(x + width, y - thickness - 1));
            }
            else switch (style.UnderlineStyle)
            {

                case Underline.Curly:
                    StreamGeometry wave = new();
                    using (StreamGeometryContext sgc = wave.Open())
                    {
                        double waveLen = Math.Max(4, _glyphs.CellWidth / 2);
                        double amplitude = Math.Max(1.5, thickness * 1.2);
                        sgc.BeginFigure(new Point(x, y), isFilled: false, isClosed: false);
                        for (double wx = x; wx < x + width; wx += waveLen)
                        {
                            double segW = Math.Min(waveLen, x + width - wx);
                            Point p1 = new(wx + segW * 0.25, y - amplitude);
                            Point p2 = new(wx + segW * 0.75, y + amplitude);
                            Point p3 = new(wx + segW, y);
                            sgc.BezierTo(p1, p2, p3, isStroked: true, isSmoothJoin: true);
                        }
                    }
                    drawingContext.DrawGeometry(null, new Pen(decoBrush, thickness), wave);
                    break;

                case Underline.Dotted:
                    Pen penDot = new(decoBrush, thickness)
                    {
                        DashStyle = new DashStyle(new double[] { 1.0, 1.5 }, 0),
                        DashCap = PenLineCap.Round
                    };
                    drawingContext.DrawLine(penDot, new Point(x, y), new Point(x + width, y));
                    break;

                case Underline.Dashed:
                    Pen penDash = new(decoBrush, thickness)
                    {
                        DashStyle = new DashStyle(new double[] { 3.0, 2.0 }, 0)
                    };
                    drawingContext.DrawLine(penDash, new Point(x, y), new Point(x + width, y));
                    break;

                default: // Single
                    Pen penSingle = new(decoBrush, thickness);
                    drawingContext.DrawLine(penSingle, new Point(x, y), new Point(x + width, y));
                    break;
            }
        }

        if (style.Strikethrough)
        {
            double y = baselineY - _glyphs.StrikethroughPosition;
            Pen pen = new(decoBrush, thickness);
            drawingContext.DrawLine(pen, new Point(x, y), new Point(x + width, y));
        }

        if (style.Overline)
        {
            Pen pen = new(decoBrush, thickness);
            drawingContext.DrawLine(pen, new Point(x, 0), new Point(x + width, 0));
        }

        if (style.Framed)
        {
            Pen pen = new(decoBrush, 1.0);
            drawingContext.DrawRectangle(null, pen, new Rect(x, 0, width, cellH));
        }

        if (style.Encircled)
        {
            Pen pen = new(decoBrush, 1.0);
            drawingContext.DrawRoundedRectangle(null, pen, new Rect(x, 0, width, cellH), 3.0, 3.0);
        }
    }

    private (Color Fg, Color Bg) ResolveColors(CellRenderStyle style, bool selected)
    {
        Color fg = style.ForegroundIsDefault ? _options.DefaultForeground : style.Foreground;
        Color bg = style.BackgroundIsDefault ? _options.DefaultBackground : style.Background;

        if (style.Inverse)
            (fg, bg) = (bg, fg);

        if (style.Conceal)
            fg = bg;

        if (selected && style.ForegroundIsDefault && style.BackgroundIsDefault && !style.Inverse)
            (fg, bg) = (_options.DefaultBackground, _options.DefaultForeground);

        return (fg, bg);
    }

    private Brush Brush(Color color)
    {
        uint key = (uint)((color.A << 24) | (color.R << 16) | (color.G << 8) | color.B);
        if (!_brushes.TryGetValue(key, out Brush? brush))
        {
            brush = new SolidColorBrush(WindowsColor.FromArgb(color.A, color.R, color.G, color.B));
            _brushes[key] = brush;
        }

        return brush;
    }
}
