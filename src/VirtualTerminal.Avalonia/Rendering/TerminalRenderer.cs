using Avalonia;
using Avalonia.Media;
using System.Diagnostics;
using System.Text;
using VirtualTerminal.Buffer;
using VirtualTerminal.Enums;
using VirtualTerminal.Model;
using VirtualTerminal.Options;
using AvaloniaColor = Avalonia.Media.Color;
using Color = VirtualTerminal.Model.TerminalColor;

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
    private readonly Dictionary<uint, IBrush> _brushes = [];
    private readonly StringBuilder _chars = new();
    private readonly List<ushort> _glyphList = [];
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
    public void RenderRow(DrawingContext drawingContext, Span<TerminalCellInfo> row, TerminalSelection? selection, int rowIndex)
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
                DrawGlyphRun(drawingContext, row, runStart, runEnd, x, baselineY, cellH, Brush(fg), style);
        }
    }

    private void DrawGlyphRun(DrawingContext drawingContext, Span<TerminalCellInfo> row, int start, int end,
        double x, double baselineY, double cellH, IBrush fgBrush, CellRenderStyle style)
    {
        double cellW = _glyphs.CellWidth;

        GlyphTypeface? current = null;
        _chars.Clear();
        _glyphList.Clear();

        double segX = x;
        int segCellCount = 0;
        Span<char> text = stackalloc char[2];

        void Flush()
        {
            if (current is null || _glyphList.Count == 0)
            {
                _chars.Clear();
                _glyphList.Clear();
                return;
            }

            string text = _chars.ToString();
            double segWidth = segCellCount * cellW;

            // Snapshot the glyph list into an array for Avalonia's GlyphRun.
            GlyphRun run = new(current, _emSize, text.AsMemory(), _glyphList.ToArray(), new Point(segX, baselineY), 0);
            drawingContext.DrawGlyphRun(fgBrush, run);
            DrawDecorations(drawingContext, style, segX, segWidth, baselineY, cellH);

            segX += segWidth;
            _chars.Clear();
            _glyphList.Clear();
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

            for (int c = 0; c < charsWritten; c++)
                _chars.Append(text[c]);

            _glyphList.Add(glyph);
            segCellCount++;
        }

        Flush();
    }

    private void DrawProceduralGlyph(DrawingContext dc, Rune rune, double x, double cellW, double cellH, IBrush fgBrush)
    {
        if (BoxDrawing.TryGetBlockRects(rune, out BlockRect[] rects))
        {
            foreach (BlockRect r in rects)
            {
                IBrush b = r.Alpha < 0.99 ? BrushWithAlpha(fgBrush, r.Alpha) : fgBrush;
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

    private static IBrush BrushWithAlpha(IBrush brush, double alpha)
    {
        if (brush is ISolidColorBrush scb)
        {
            AvaloniaColor c = scb.Color;
            return new SolidColorBrush(new AvaloniaColor((byte)Math.Clamp((int)(c.A * alpha), 0, 255), c.R, c.G, c.B));
        }
        return brush;
    }

    private void DrawDecorations(DrawingContext drawingContext, CellRenderStyle style, double x, double width, double baselineY, double cellH)
    {
        Color fg = style.ForegroundIsDefault ? _options.DefaultForeground : style.Foreground;
        Color decoColor = style.UnderlineColorIsDefault ? fg : style.UnderlineColor;
        IBrush decoBrush = Brush(decoColor);
        double thickness = Math.Max(1, _glyphs.UnderlineThickness);

        if (style.UnderlineStyle != Enums.Underline.None || style.DoublyUnderlined)
        {
            double y = baselineY + _glyphs.UnderlinePosition;

            if (style.UnderlineStyle == Enums.Underline.Double || style.DoublyUnderlined)
            {
                Pen penDbl = new(decoBrush, thickness);
                drawingContext.DrawLine(penDbl, new Point(x, y), new Point(x + width, y));
                drawingContext.DrawLine(penDbl, new Point(x, y - thickness - 1), new Point(x + width, y - thickness - 1));
            }
            else switch (style.UnderlineStyle)
            {

                case Enums.Underline.Curly:
                    PathGeometry wave = new();
                    PathFigure fig = new() { StartPoint = new Point(x, y), IsClosed = false };
                    double waveLen = Math.Max(4, _glyphs.CellWidth / 2);
                    double amplitude = Math.Max(1.5, thickness * 1.2);
                    for (double wx = x; wx < x + width; wx += waveLen)
                    {
                        double segW = Math.Min(waveLen, x + width - wx);
                        Point p1 = new(wx + segW * 0.25, y - amplitude);
                        Point p2 = new(wx + segW * 0.75, y + amplitude);
                        Point p3 = new(wx + segW, y);
                        fig.Segments?.Add(new BezierSegment { Point1 = p1, Point2 = p2, Point3 = p3 });
                    }
                    wave.Figures?.Add(fig);
                    drawingContext.DrawGeometry(null, new Pen(decoBrush, thickness), wave);
                    break;

                case Enums.Underline.Dotted:
                    Pen penDot = new(decoBrush, thickness, DashStyle.Dot, PenLineCap.Round);
                    drawingContext.DrawLine(penDot, new Point(x, y), new Point(x + width, y));
                    break;

                case Enums.Underline.Dashed:
                    Pen penDash = new(decoBrush, thickness, DashStyle.Dash);
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
            double y = baselineY + _glyphs.StrikethroughPosition;
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
            drawingContext.DrawRectangle(null, pen, new RoundedRect(new Rect(x, 0, width, cellH), 3.0, 3.0));
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

    private IBrush Brush(Color color)
    {
        AvaloniaColor aColor = new AvaloniaColor(color.A, color.R, color.G, color.B);
        if (!_brushes.TryGetValue(aColor.ToUInt32(), out IBrush? brush))
        {
            brush = new SolidColorBrush(aColor);
            _brushes[aColor.ToUInt32()] = brush;
        }

        return brush;
    }
}
