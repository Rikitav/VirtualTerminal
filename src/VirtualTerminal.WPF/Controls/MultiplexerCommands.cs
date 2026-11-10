using System.Windows.Input;

namespace VirtualTerminal.Controls;

/// <summary>
/// Standard routed commands for <see cref="MultiplexerControl"/>.
/// </summary>
public static class MultiplexerCommands
{
    /// <summary>Splits the active pane vertically (side-by-side).</summary>
    public static readonly RoutedUICommand SplitVertical = new(
        "Split Vertical",
        nameof(SplitVertical),
        typeof(MultiplexerControl),
        new InputGestureCollection { new KeyGesture(Key.E, ModifierKeys.Control | ModifierKeys.Shift) });

    /// <summary>Splits the active pane horizontally (top/bottom).</summary>
    public static readonly RoutedUICommand SplitHorizontal = new(
        "Split Horizontal",
        nameof(SplitHorizontal),
        typeof(MultiplexerControl),
        new InputGestureCollection { new KeyGesture(Key.O, ModifierKeys.Control | ModifierKeys.Shift) });

    /// <summary>Closes the currently active pane.</summary>
    public static readonly RoutedUICommand CloseActive = new(
        "Close Active Pane",
        nameof(CloseActive),
        typeof(MultiplexerControl),
        new InputGestureCollection { new KeyGesture(Key.W, ModifierKeys.Control | ModifierKeys.Shift) });

    /// <summary>Moves focus to the next pane.</summary>
    public static readonly RoutedUICommand FocusNext = new(
        "Focus Next Pane",
        nameof(FocusNext),
        typeof(MultiplexerControl),
        new InputGestureCollection { new KeyGesture(Key.Right, ModifierKeys.Alt) });

    /// <summary>Moves focus to the previous pane.</summary>
    public static readonly RoutedUICommand FocusPrevious = new(
        "Focus Previous Pane",
        nameof(FocusPrevious),
        typeof(MultiplexerControl),
        new InputGestureCollection { new KeyGesture(Key.Left, ModifierKeys.Alt) });
}
