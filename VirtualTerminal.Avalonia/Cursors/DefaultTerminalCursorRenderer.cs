using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using VirtualTerminal.Rendering;

namespace VirtualTerminal.Cursors;

/// <summary>
/// Default cursor visual: a thin vertical bar (15% of the cell width, at least 2 device units)
/// that snaps instantly to the target cell and blinks on a private 530ms timer. Replaceable via
/// <see cref="TerminalControl.CursorRenderer"/>.
/// </summary>
public sealed class DefaultTerminalCursorRenderer : TerminalCursorRenderer
{
    private static readonly IBrush CursorBrush = new SolidColorBrush(Colors.White);
    private static readonly TimeSpan BlinkInterval = TimeSpan.FromMilliseconds(530);

    private readonly DispatcherTimer _blinkTimer;
    private Rect _currentRect;
    private bool _blinkOn = true;

    /// <summary>Initializes a new instance of the <see cref="DefaultTerminalCursorRenderer"/> class.</summary>
    public DefaultTerminalCursorRenderer()
    {
        _blinkTimer = new DispatcherTimer(BlinkInterval, DispatcherPriority.Background, Dispatcher.UIThread, OnBlinkTick);
    }

    /// <inheritdoc/>
    public override void Attach(TerminalControl control)
    {
        base.Attach(control);
        _blinkOn = true;
        _blinkTimer.Start();
    }

    /// <inheritdoc/>
    public override void Detach()
    {
        _blinkTimer.Stop();
        base.Detach();
    }

    /// <inheritdoc/>
    public override void MoveTo(Rect cellRect) => _currentRect = cellRect;

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        if (!IsCursorVisible || !_blinkOn)
            return;

        double width = Math.Max(2, _currentRect.Width * 0.15);
        context.DrawRectangle(CursorBrush, null, new Rect(_currentRect.X, _currentRect.Y, width, _currentRect.Height));
    }

    private void OnBlinkTick(object? sender, EventArgs e)
    {
        _blinkOn = !_blinkOn;
        if (IsCursorVisible)
            TerminalControl?.InvalidateVisual();
    }
}
