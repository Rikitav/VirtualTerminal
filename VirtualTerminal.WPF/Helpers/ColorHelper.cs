using Color = System.Drawing.Color;
using WindowsColor = System.Windows.Media.Color;

namespace VirtualTerminal.Helpers;

/// <summary>
/// Conversion helpers between Windows console color attributes and WPF <see cref="Color"/>.
/// </summary>
public static class ColorHelper
{
    public static WindowsColor ToWindowsMediaColor(this Color color)
        => WindowsColor.FromArgb(color.A, color.R, color.G, color.B);

    public static Color ToDrawingColor(this WindowsColor color)
        => Color.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>
    /// Checks whether the specified color is representable in the standard Windows console palette
    /// (channels limited to 0/128/255).
    /// </summary>
    public static bool IsValidConsoleColor(Color color)
    {
        if (color.R != 0 && color.R != 128 && color.R != 255)
            return false;

        if (color.G != 0 && color.G != 128 && color.G != 255)
            return false;

        if (color.B != 0 && color.B != 128 && color.B != 255)
            return false;

        return true;
    }
}
