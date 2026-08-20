using VirtualTerminal.Model;
using WindowsColor = System.Windows.Media.Color;

namespace VirtualTerminal.Helpers;

/// <summary>
/// Conversion helpers between WPF <see cref="WindowsColor"/> and the core <see cref="TerminalColor"/>.
/// </summary>
public static class ColorHelper
{
    /// <summary>Converts a core <see cref="TerminalColor"/> to a WPF <see cref="WindowsColor"/>.</summary>
    public static WindowsColor ToWindowsMediaColor(this TerminalColor color)
        => WindowsColor.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>Converts a WPF <see cref="WindowsColor"/> to a core <see cref="TerminalColor"/>.</summary>
    public static TerminalColor ToTerminalColor(this WindowsColor color)
        => TerminalColor.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>
    /// Checks whether the specified color is representable in the standard Windows console palette
    /// (channels limited to 0/128/255).
    /// </summary>
    public static bool IsValidConsoleColor(TerminalColor color)
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
