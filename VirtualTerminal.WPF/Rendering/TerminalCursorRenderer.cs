using System.Windows;
using System.Windows.Media;

namespace VirtualTerminal.Rendering;

/// <summary>
/// Pluggable cursor visual for <see cref="TerminalControl"/>. The control drives the renderer:
/// it notifies the target cell rectangle via <see cref="MoveTo"/> whenever the cursor position
/// changes, and calls <see cref="Render"/> once per render pass with the current state in
/// <see cref="IsCursorVisible"/>/<see cref="CursorBlinkMode"/>. The renderer owns its own visual
/// behavior — blinking, animation, transition effects. The default implementation blinks on a
/// private timer; custom implementations can run storyboard-driven interpolation from
/// <see cref="MoveTo"/>, ignore <see cref="CursorBlinkMode"/>, or anything else.
/// </summary>
public abstract class TerminalCursorRenderer
{
    /// <summary>Gets the control this renderer is attached to, or <c>null</c> when detached.</summary>
    public TerminalControl? TerminalControl { get; protected set; }

    /// <summary>
    /// Gets or sets whether the cursor would be visible this frame (control focused, scrolled to
    /// the bottom, DECTCEM on, master <c>CursorVisible</c> on). Set by the owning control
    /// immediately before each <see cref="Render"/> call.
    /// </summary>
    public bool IsCursorVisible { get; set; }

    /// <summary>
    /// Gets or sets whether the application enabled the DEC ?12 (att610) cursor-blink mode.
    /// Set by the owning control immediately before each <see cref="Render"/> call. The renderer
    /// decides whether and how to honor it (the default renderer blinks regardless, matching
    /// conventional terminal behavior).
    /// </summary>
    public bool CursorBlinkMode { get; set; }

    /// <summary>
    /// Gets or sets the control's current background color. Set by the owning control immediately
    /// before each <see cref="Render"/> call; renderers that draw a difference-style cursor use it
    /// to pick a contrasting (inverted) color.
    /// </summary>
    public Color BackgroundColor { get; set; } = Colors.Black;

    /// <summary>Called when the renderer is attached to a control (control entered the visual tree).</summary>
    /// <param name="control">The owning terminal control.</param>
    public virtual void Attach(TerminalControl control) => TerminalControl = control;

    /// <summary>Called when the renderer is detached from its control (control left the visual tree).</summary>
    public virtual void Detach() => TerminalControl = null;

    /// <summary>Notifies the renderer that the cursor target cell changed to <paramref name="cellRect"/>.</summary>
    /// <param name="cellRect">The cursor cell rectangle in control content coordinates (the padding transform is already applied by the control).</param>
    public abstract void MoveTo(Rect cellRect);

    /// <summary>Draws the cursor into <paramref name="context"/> for the current state.</summary>
    /// <param name="context">The drawing context, with the padding transform already applied.</param>
    public abstract void Render(DrawingContext context);
}
