using AvaloniaColor = Avalonia.Media.Color;
using Color = System.Drawing.Color;

namespace VirtualTerminal.Helpers;

/// <summary>Provides extension methods for converting between Avalonia and System.Drawing color types.</summary>
public static class ColorHelper
{
    /// <summary>Converts a <see cref="Color"/> to an Avalonia <see cref="AvaloniaColor"/>.</summary>
    /// <param name="color">The drawing color to convert.</param>
    /// <returns>The equivalent Avalonia color.</returns>
    public static AvaloniaColor ToAvaloniaColor(this Color color)
        => new AvaloniaColor(color.A, color.R, color.G, color.B);

    /// <summary>Converts an Avalonia <see cref="AvaloniaColor"/> to a <see cref="Color"/>.</summary>
    /// <param name="color">The Avalonia color to convert.</param>
    /// <returns>The equivalent drawing color.</returns>
    public static Color ToDrawingColor(this AvaloniaColor color)
        => Color.FromArgb(color.A, color.R, color.G, color.B);
}
