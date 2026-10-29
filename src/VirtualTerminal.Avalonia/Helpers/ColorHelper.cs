using AvaloniaColor = Avalonia.Media.Color;
using VirtualTerminal.Model;

namespace VirtualTerminal.Helpers;

/// <summary>Provides extension methods for converting between Avalonia and core <see cref="TerminalColor"/> types.</summary>
public static class ColorHelper
{
    /// <summary>Converts a core <see cref="TerminalColor"/> to an Avalonia <see cref="AvaloniaColor"/>.</summary>
    public static AvaloniaColor ToAvaloniaColor(this TerminalColor color)
        => new AvaloniaColor(color.A, color.R, color.G, color.B);

    /// <summary>Converts an Avalonia <see cref="AvaloniaColor"/> to a core <see cref="TerminalColor"/>.</summary>
    public static TerminalColor ToTerminalColor(this AvaloniaColor color)
        => TerminalColor.FromArgb(color.A, color.R, color.G, color.B);
}
