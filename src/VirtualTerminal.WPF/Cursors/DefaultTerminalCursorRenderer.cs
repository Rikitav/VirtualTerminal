using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using VirtualTerminal.Enums;
using VirtualTerminal.Rendering;

namespace VirtualTerminal.Cursors;

/// <summary>
/// Default cursor visual: renders the active <see cref="TerminalCursorRenderer.Shape"/> (Block, Underline, Bar)
/// that snaps instantly to the target cell and blinks on a private 530ms timer when blinking is enabled.
/// Replaceable via <see cref="TerminalControl.CursorRenderer"/>.
/// </summary>
public sealed class DefaultTerminalCursorRenderer : TerminalCursorRenderer
{
    private static readonly SolidColorBrush CursorBrush = new(Colors.White);
    private static readonly TimeSpan BlinkInterval = TimeSpan.FromMilliseconds(530);

    private readonly DispatcherTimer _blinkTimer;
    private Rect _currentRect;
    private bool _blinkOn = true;

    /// <summary>Initializes a new instance of the <see cref="DefaultTerminalCursorRenderer"/> class.</summary>
    public DefaultTerminalCursorRenderer()
    {
        _blinkTimer = new DispatcherTimer(BlinkInterval, DispatcherPriority.Background, OnBlinkTick, Dispatcher.CurrentDispatcher);
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
        if (!IsCursorVisible || (CursorBlinkMode && !_blinkOn))
            return;

        Rect shapeRect = Shape switch
        {
            CursorShape.Block => _currentRect,
            CursorShape.Underline => new Rect(
                _currentRect.X,
                _currentRect.Y + _currentRect.Height - Math.Max(2, _currentRect.Height * 0.15),
                _currentRect.Width,
                Math.Max(2, _currentRect.Height * 0.15)),
            CursorShape.Bar => new Rect(
                _currentRect.X,
                _currentRect.Y,
                Math.Max(2, _currentRect.Width * 0.15),
                _currentRect.Height),
            _ => _currentRect,
        };

        context.DrawRectangle(CursorBrush, null, shapeRect);
    }

    private void OnBlinkTick(object? sender, EventArgs e)
    {
        _blinkOn = !_blinkOn;
        if (IsCursorVisible && CursorBlinkMode)
            TerminalControl?.InvalidateVisual();
    }
}
