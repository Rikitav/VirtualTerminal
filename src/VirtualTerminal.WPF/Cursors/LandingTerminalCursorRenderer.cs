using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using VirtualTerminal.Rendering;

namespace VirtualTerminal.Cursors;

/// <summary>
/// Cursor visual with a takeoff-landing transition on every move. The cursor at the source cell
/// expands in width and height while fading to transparency (the "takeoff" — the cursor appears
/// to fly up and away), while at the target cell an oversized transparent cursor shrinks back to
/// its normal size and color (the "landing"). Both phases run concurrently over
/// <see cref="AnimationDuration"/> with a cosine ease; there is no sweep between cells, so the
/// effect works equally well for far jumps (newline, prompt redraws) and single-cell typing.
/// The cursor is drawn in the color inverse of <see cref="TerminalCursorRenderer.BackgroundColor"/>
/// (difference-style; per-pixel invert blending is not available on <see cref="DrawingContext"/>).
/// Steady (no blink); combine with a private blink timer if wanted — see
/// <see cref="DefaultTerminalCursorRenderer"/>.
/// </summary>
public sealed class LandingTerminalCursorRenderer : TerminalCursorRenderer
{
    private readonly DispatcherTimer _timer;

    private Rect _takeoff;            // source cell the cursor lifts off from
    private Rect _target;             // destination cell (steady position once landed)
    private DateTime _animationStartUtc;
    private bool _hasTarget;
    private bool _animating;

    /// <summary>
    /// How long the takeoff-landing effect takes.
    /// </summary>
    public TimeSpan AnimationDuration { get; set; } = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// How often the timer kicks the renderer.
    /// </summary>
    public TimeSpan FrameInterval { get; set; } = TimeSpan.FromMilliseconds(16);

    /// <summary>
    /// Size multiplier of the cursor at the peak of the effect: the takeoff grows toward this
    /// factor while fading out, and the landing starts at this factor while transparent and
    /// shrinks back to the normal cell size.
    /// </summary>
    public double ExpandFactor { get; set; } = 2.5;

    /// <summary>
    /// Maximum cell distance (Manhattan metric) that still moves instantly. Single-cell moves —
    /// typing, arrow keys — snap straight to the new cell; only longer jumps play the
    /// takeoff-landing effect. Set to 0 to animate every move.
    /// </summary>
    public double MaxInstantCells { get; set; } = 1;

    // Inverse-of-background brush for the steady cursor, rebuilt only when the reported background changes.
    private Color _brushSource;
    private SolidColorBrush? _brush;

    /// <summary>Initializes a new instance of the <see cref="LandingTerminalCursorRenderer"/> class.</summary>
    public LandingTerminalCursorRenderer()
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
            // First positioning: snap, nothing to take off from.
            _takeoff = _target = cellRect;
            _hasTarget = true;
            return;
        }

        if (MaxInstantCells > 0)
        {
            // Single-cell move (typing, arrow key): snap instantly, no takeoff-landing —
            // the effect reads as flicker at this distance.
            double cellW = _target.Width;
            double cellH = _target.Height;
            double dxCells = cellW > 0 ? Math.Abs(cellRect.X - _target.X) / cellW : double.MaxValue;
            double dyCells = cellH > 0 ? Math.Abs(cellRect.Y - _target.Y) / cellH : double.MaxValue;
            if (dxCells + dyCells <= MaxInstantCells)
            {
                _timer.Stop();
                _takeoff = _target = cellRect;
                _animating = false;
                TerminalControl?.InvalidateVisual();
                return;
            }
        }

        // Lift off from wherever the cursor currently sits (a steady cell or a landing in
        // progress) and start landing at the new cell.
        _takeoff = _target;
        _target = cellRect;
        _animationStartUtc = DateTime.UtcNow;
        _animating = true;
        _timer.Start();

        // Draw both phases immediately rather than waiting for the next tick.
        TerminalControl?.InvalidateVisual();
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        if (!IsCursorVisible || !_hasTarget)
            return;

        if (_animating)
        {
            double raw = Progress();
            double t = Ease(raw);

            // Takeoff: the cursor at the source cell grows and fades out.
            Rect lift = ExpandAroundCenter(_takeoff, 1 + (ExpandFactor - 1) * t);
            DrawBar(context, lift, CursorBrush(Alpha(1 - raw)));

            // Landing: an oversized transparent cursor at the target shrinks into place.
            Rect land = ExpandAroundCenter(_target, 1 + (ExpandFactor - 1) * (1 - t));
            DrawBar(context, land, CursorBrush(Alpha(raw)));
            return;
        }

        DrawBar(context, _target, CursorBrush(255));
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

    private static byte Alpha(double opacity) =>
        (byte)(255 * Math.Clamp(opacity, 0, 1));

    private static Rect ExpandAroundCenter(Rect r, double scale) => new(
        r.X - r.Width * (scale - 1) / 2,
        r.Y - r.Height * (scale - 1) / 2,
        r.Width * scale,
        r.Height * scale);

    private Brush CursorBrush(byte alpha)
    {
        if (alpha == 255)
        {
            // Steady cursor: cache the full-alpha brush, rebuilt only when the background changes.
            if (_brush is null || _brushSource != BackgroundColor)
            {
                _brushSource = BackgroundColor;
                _brush = new SolidColorBrush(Invert(BackgroundColor, 255));
            }

            return _brush;
        }

        return new SolidColorBrush(Invert(BackgroundColor, alpha));
    }

    private static Color Invert(Color c, byte alpha) =>
        Color.FromArgb(alpha, (byte)~c.R, (byte)~c.G, (byte)~c.B);

    private static double BarWidth(Rect cellRect) => Math.Max(2, cellRect.Width * 0.15);

    private static void DrawBar(DrawingContext context, Rect cellRect, Brush brush)
    {
        context.DrawRectangle(brush, null, new Rect(cellRect.X, cellRect.Y, BarWidth(cellRect), cellRect.Height));
    }
}
