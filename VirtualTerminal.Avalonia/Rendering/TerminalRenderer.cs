using Avalonia;
using Avalonia.Media;
using System.Text;
using VirtualTerminal.Buffer;
using VirtualTerminal.Model;
using VirtualTerminal.Options;
using AvaloniaColor = Avalonia.Media.Color;
using Color = System.Drawing.Color;

namespace VirtualTerminal.Rendering;

/// <summary>
/// A rectangular text selection in buffer coordinates (start/end inclusive, normalized so
/// Start ≤ End). Used by <see cref="TerminalRenderer"/> to highlight selected cells.
/// </summary>
public readonly struct TerminalSelection
{
    public readonly int StartY;
    public readonly int StartX;
    public readonly int EndY;
    public readonly int EndX;

    public TerminalSelection(int x1, int y1, int x2, int y2)
    {
        if (y1 < y2 || (y1 == y2 && x1 <= x2))
        {
            StartY = y1;
            StartX = x1;
            EndY = y2;
            EndX = x2;
        }
        else
        {
            StartY = y2;
            StartX = x2;
            EndY = y1;
            EndX = x1;
        }
    }

    public bool Contains(int y, int x)
    {
        if (y < StartY || y > EndY)
            return false;

        if (y == StartY && x < StartX)
            return false;

        if (y == EndY && x > EndX)
            return false;

        return true;
    }
}

/// <summary>
/// Paints terminal rows using <see cref="GlyphRun"/>: coalesces same-style cells into runs,
/// resolves default/inverse/conceal colors, draws backgrounds, underlines, strikethrough,
/// overline, and the cursor. Positioning is column-based (monospace advance assumption);
/// wide glyphs render at single-cell width in Phase 1.
/// </summary>
public sealed class TerminalRenderer : IDisposable
{
    private readonly GlyphCache _glyphs = new();
    private readonly Dictionary<uint, IBrush> _brushes = [];
    private TerminalOptions _options = new();
    private double _emSize = 14;

    public bool IsValid => _glyphs.IsValid;
    public Size CellSize => new(_glyphs.CellWidth, _glyphs.CellHeight);
    public double Baseline => _glyphs.Ascent;
    public double CellWidth => _glyphs.CellWidth;
    public double CellHeight => _glyphs.CellHeight;

    public void Configure(string family, double emSize, TerminalOptions options)
    {
        _options = options;
        _emSize = emSize;
        _glyphs.Configure(family, emSize, options.LineHeight, options.FontFallback);
        _brushes.Clear();
    }

    public void Dispose()
        => _glyphs.Dispose();

    /// <summary>Renders the cursor shape at column <paramref name="col"/> (row-local y=0). The caller
    /// decides visibility by clearing the cursor group when the cursor should be hidden.</summary>
    public void RenderCursor(DrawingContext drawingContext, int col, CursorShape shape)
    {
        double cellW = _glyphs.CellWidth;
        double cellH = _glyphs.CellHeight;
        double x = col * cellW;

        IBrush brush = Brush(_options.DefaultCursorColor);
        Rect rect = shape switch
        {
            CursorShape.Bar => new Rect(x, 0, Math.Max(1, cellW * 0.2), cellH),
            CursorShape.Underline => new Rect(x, cellH - Math.Max(2, cellH * 0.15), cellW, Math.Max(2, cellH * 0.15)),
            _ => new Rect(x, 0, cellW, cellH),
        };

        drawingContext.DrawRectangle(brush, null, rect);
    }

    /// <summary>Renders a single row into <paramref name="dc"/> (local origin = row top-left).</summary>
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
            int runStart = col;
            while (col < len && row[col].Style == style)
                col++;

            int runEnd = col; // exclusive
            double x = runStart * cellW;
            //bool anySelected = selection.HasValue && AnyInRange(selection.Value, rowIndex, runStart, runEnd);
            bool anySelected = false;

            (Color fg, Color bg) = ResolveColors(style, anySelected);
            if (!style.BackgroundIsDefault || style.Inverse || anySelected)
                drawingContext.DrawRectangle(Brush(bg), null, new Rect(x, 0, (runEnd - runStart) * cellW, cellH));

            if (!style.Continuation)
                DrawGlyphRun(drawingContext, row, runStart, runEnd, x, baselineY, Brush(fg), style);
        }
    }

    private static bool AnyInRange(TerminalSelection sel, int row, int start, int end)
    {
        for (int x = start; x < end; x++)
        {
            if (sel.Contains(row, x))
                return true;
        }

        return false;
    }

    private void DrawGlyphRun(DrawingContext drawingContext, Span<TerminalCellInfo> row, int start, int end,
        double x, double baselineY, IBrush fgBrush, CellRenderStyle style)
    {
        double cellW = _glyphs.CellWidth;

        // Accumulate per-typeface segments; flush on typeface change.
        GlyphTypeface? current = null;
        StringBuilder chars = new();
        List<ushort> glyphs = [];
        int segCellCount = 0;
        double segX = x;

        void Flush()
        {
            if (current is null || chars.Length == 0)
            {
                segCellCount = 0;
                return;
            }

            string text = chars.ToString();
            double segWidth = segCellCount * cellW;

            // Snapshot the glyph list into an array. Avalonia may retain the collection
            // internally, and we reuse the builder lists for subsequent segments.
            GlyphRun run = new GlyphRun(current, _emSize, text.AsMemory(), glyphs.ToArray(), new Point(segX, baselineY), 0);
            drawingContext.DrawGlyphRun(fgBrush, run);
            DrawDecorations(drawingContext, style, segX, segWidth, baselineY);

            segX += segWidth;
            chars.Clear();
            glyphs.Clear();
            segCellCount = 0;
        }

        for (int i = start; i < end; i++)
        {
            ref readonly TerminalCellInfo cell = ref row[i];
            if (cell.Style.Continuation)
                continue;

            Rune rune = cell.Character;
            (GlyphTypeface tf, ushort glyph) = _glyphs.GetGlyph(rune);

            if (current is not null && !ReferenceEquals(tf, current))
                Flush();

            current = tf;
            chars.Append(rune.ToString());
            glyphs.Add(glyph);
            segCellCount++;
        }

        Flush();
    }

    private void DrawDecorations(DrawingContext drawingContext, CellRenderStyle style, double x, double width, double baselineY)
    {
        double cellH = _glyphs.CellHeight;
        Color fg = style.ForegroundIsDefault ? _options.DefaultForeground : style.Foreground;
        Color decoColor = style.UnderlineColorIsDefault ? fg : style.UnderlineColor;
        IBrush decoBrush = Brush(decoColor);
        double thickness = Math.Max(1, _glyphs.UnderlineThickness);
        Pen pen = new(decoBrush, thickness);

        if (style.UnderlineStyle != Enums.Underline.None || style.DoublyUnderlined)
        {
            double y = cellH + _glyphs.UnderlinePosition;
            drawingContext.DrawLine(pen, new Point(x, y), new Point(x + width, y));
            if (style.UnderlineStyle == Enums.Underline.Double)
                drawingContext.DrawLine(pen, new Point(x, y - thickness - 1), new Point(x + width, y - thickness - 1));
        }

        if (style.Strikethrough)
        {
            double y = baselineY + _glyphs.StrikethroughPosition;
            drawingContext.DrawLine(pen, new Point(x, y), new Point(x + width, y));
        }

        if (style.Overline)
            drawingContext.DrawLine(pen, new Point(x, 0), new Point(x + width, 0));
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
