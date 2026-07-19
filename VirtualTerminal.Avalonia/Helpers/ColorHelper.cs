using AvaloniaColor = Avalonia.Media.Color;
using Color = System.Drawing.Color;

namespace VirtualTerminal.Helpers;

public static class ColorHelper
{
    public static AvaloniaColor ToAvaloniaColor(this Color color)
        => new AvaloniaColor(color.A, color.R, color.G, color.B);

    public static Color ToDrawingColor(this AvaloniaColor color)
        => Color.FromArgb(color.A, color.R, color.G, color.B);
}
