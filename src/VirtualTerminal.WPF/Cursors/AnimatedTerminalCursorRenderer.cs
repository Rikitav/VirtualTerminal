using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using VirtualTerminal.Rendering;

namespace VirtualTerminal.Cursors;

/// <summary>
/// Cursor visual that glides from its previous cell to the new one. Near moves (typing, arrow
/// keys) animate: the bar is drawn at both the previous cell (A) and the target cell (B),
/// connected by a transition quadrilateral, and A interpolates toward B with a cosine ease-in-out
/// over <see cref="AnimationDuration"/>. Far jumps (newline, prompt redraws, absolute cursor
/// positioning) teleport, so the cursor never sweeps through transient positions. The cursor is
/// drawn in the color inverse of <see cref="TerminalCursorRenderer.BackgroundColor"/>
/// (difference-style; per-pixel invert blending is not available on <see cref="DrawingContext"/>).
/// Steady (no blink); combine with a private blink timer if wanted — see
/// <see cref="DefaultTerminalCursorRenderer"/>.
/// </summary>
public sealed class AnimatedTerminalCursorRenderer : TerminalCursorRenderer
{
    private readonly DispatcherTimer _timer;

    private Rect _start;          // A: where the glide started (or current position while chaining)
    private Rect _target;         // B: destination cell
    private DateTime _animationStartUtc;
    private bool _hasTarget;
    private bool _animating;

    /// <summary>
    /// How long the glide from A to B takes.
    /// </summary>
    public TimeSpan AnimationDuration { get; set; } = TimeSpan.FromMilliseconds(120);

    /// <summary>
    /// How often does timer kicks the renderer.
    /// </summary>
    public TimeSpan FrameInterval { get; set; } = TimeSpan.FromMilliseconds(32);

    /// <summary>
    /// Maximum cell distance that still glides. Moves farther than this (in cells, Manhattan
    /// metric) teleport instead: newline + prompt redraws arrive as separate decode chunks and
    /// would otherwise make the cursor sweep through transient positions (line start) on its way
    /// to the new prompt end. Typing stays a stream of small glides. No timer involved.
    /// </summary>
    public double MaxGlideCells { get; set; } = -1;

    // Inverse-of-background brush, rebuilt only when the reported background changes.
    private Color _brushSource;
    private SolidColorBrush? _brush;

    /// <summary>Initializes a new instance of the <see cref="AnimatedTerminalCursorRenderer"/> class.</summary>
    public AnimatedTerminalCursorRenderer()
    {
        _timer = new DispatcherTimer(FrameInterval, DispatcherPriority.Background, OnTick, Dispatcher.CurrentDispatcher);
    }

    /// <inheritdoc/>
    public override void Attach(TerminalControl control)
    {
        base.Attach(control);
        _hasTarget = false;
        _animating = false;
    }

    /// <inheritdoc/>
    public override void Detach()
    {
        _timer.Stop();
        base.Detach();
    }

    /// <inheritdoc/>
    public override void MoveTo(Rect cellRect)
    {
        if (!_hasTarget)
        {
            // First positioning: snap, nothing to glide from.
            _start = _target = cellRect;
            _hasTarget = true;
            return;
        }

        if (MaxGlideCells > 0)
        {
            // Far jump (newline, prompt redraw, absolute cursor positioning): teleport straight to
            // the destination instead of sweeping through transient intermediate positions.
            double cellW = _target.Width;
            double cellH = _target.Height;
            double dxCells = cellW > 0 ? Math.Abs(cellRect.X - _target.X) / cellW : double.MaxValue;
            double dyCells = cellH > 0 ? Math.Abs(cellRect.Y - _target.Y) / cellH : double.MaxValue;

            if (dxCells + dyCells > MaxGlideCells)
            {
                _timer.Stop();
                _start = _target = cellRect;
                _animating = false;
                TerminalControl?.InvalidateVisual();
                return;
            }
        }

        // Chained move mid-glide: start the new animation from the current interpolated
        // position so the cursor never jumps backwards.
        _start = _animating ? Interpolate(_start, _target, Ease(Progress())) : _target;
        _target = cellRect;
        _animationStartUtc = DateTime.UtcNow;
        _animating = true;
        _timer.Start();

        // Draw A, B and the quadrilateral immediately rather than waiting for the next tick.
        TerminalControl?.InvalidateVisual();
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        if (!IsCursorVisible || !_hasTarget)
            return;

        Brush brush = CursorBrush();

        if (_animating)
        {
            Rect current = Interpolate(_start, _target, Ease(Progress()));
            DrawQuadrilateral(context, current, _target, brush);
            DrawBar(context, current, brush);
            DrawBar(context, _target, brush);
        }
        else
        {
            DrawBar(context, _target, brush);
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (Progress() >= 1.0)
        {
            _animating = false;
            _timer.Stop();
        }

        TerminalControl?.InvalidateVisual();
    }

    private double Progress() =>
        _animating ? Math.Min(1.0, (DateTime.UtcNow - _animationStartUtc) / AnimationDuration) : 1.0;

    private static double Ease(double t) =>
        (1 - Math.Cos(Math.PI * t)) / 2;   // cosine ease-in-out, t ∈ [0,1]

    private static Rect Interpolate(Rect a, Rect b, double t) => new(
        a.X + (b.X - a.X) * t,
        a.Y + (b.Y - a.Y) * t,
        a.Width + (b.Width - a.Width) * t,
        a.Height + (b.Height - a.Height) * t);

    private Brush CursorBrush()
    {
        if (_brush is null || _brushSource != BackgroundColor)
        {
            _brushSource = BackgroundColor;
            _brush = new SolidColorBrush(Invert(BackgroundColor));
        }

        return _brush;
    }

    private static Color Invert(Color c) =>
        Color.FromArgb(255, (byte)~c.R, (byte)~c.G, (byte)~c.B);

    private static double BarWidth(Rect cellRect) => Math.Max(2, cellRect.Width * 0.15);

    private static void DrawBar(DrawingContext context, Rect cellRect, Brush brush)
    {
        context.DrawRectangle(brush, null, new Rect(cellRect.X, cellRect.Y, BarWidth(cellRect), cellRect.Height));
    }

    /// <summary>Fills the quadrilateral swept between the A and B bar rectangles.</summary>
    private static void DrawQuadrilateral(DrawingContext context, Rect a, Rect b, Brush brush)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(a.X, a.Y), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(b.X, b.Y), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(b.X + BarWidth(b), b.Y + b.Height), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(a.X + BarWidth(a), a.Y + a.Height), isStroked: true, isSmoothJoin: false);
        }

        context.DrawGeometry(brush, null, geometry);
    }
}
