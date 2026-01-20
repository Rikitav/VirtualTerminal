using System.Windows.Media;
using VirtualTerminal.Interop;

namespace VirtualTerminal.Helpers;

/// <summary>
/// Conversion helpers between Windows console color attributes and WPF <see cref="Color"/>.
/// </summary>
public static class ColorHelper
{
    /// <summary>
    /// Converts console character attributes into a WPF color.
    /// </summary>
    /// <param name="attributes">Console character attributes.</param>
    /// <param name="isBackground"><c>true</c> to read background flags; <c>false</c> for foreground.</param>
    public static Color ConvertToColor(ConsoleCharacterAttributes attributes, bool isBackground)
    {
        bool red, green, blue, intense;
        if (isBackground)
        {
            red = attributes.HasFlag(ConsoleCharacterAttributes.BackgroundRed);
            green = attributes.HasFlag(ConsoleCharacterAttributes.BackgroundGreen);
            blue = attributes.HasFlag(ConsoleCharacterAttributes.BackgroundBlue);
            intense = attributes.HasFlag(ConsoleCharacterAttributes.BackgroundIntensity);
        }
        else
        {
            red = attributes.HasFlag(ConsoleCharacterAttributes.ForegroundRed);
            green = attributes.HasFlag(ConsoleCharacterAttributes.ForegroundGreen);
            blue = attributes.HasFlag(ConsoleCharacterAttributes.ForegroundBlue);
            intense = attributes.HasFlag(ConsoleCharacterAttributes.ForegroundIntensity);
        }

        byte r = (byte)(red ? (intense ? 255 : 128) : 0);
        byte g = (byte)(green ? (intense ? 255 : 128) : 0);
        byte b = (byte)(blue ? (intense ? 255 : 128) : 0);

        if (r == 0 && g == 0 && b == 0)
            return isBackground ? Colors.Black : Colors.White;
    
        if (r == 255 && g == 255 && b == 255)
            return intense ? Colors.White : Colors.Gray;
       
        return Color.FromRgb(r, g, b);
    }

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

    /// <summary>
    /// Converts a WPF color (console palette) into console character attribute flags.
    /// </summary>
    /// <param name="color">Console-compatible color.</param>
    /// <param name="isBackground"><c>true</c> to produce background flags; <c>false</c> for foreground.</param>
    public static ConsoleCharacterAttributes ConvertToAttributes(Color color, bool isBackground)
    {
        ConsoleCharacterAttributes attributes = ConsoleCharacterAttributes.None;

        bool red = color.R > 0;
        bool green = color.G > 0;
        bool blue = color.B > 0;
        bool intense = color.R == 255 || color.G == 255 || color.B == 255;

        if (isBackground)
        {
            if (red) attributes |= ConsoleCharacterAttributes.BackgroundRed;
            if (green) attributes |= ConsoleCharacterAttributes.BackgroundGreen;
            if (blue) attributes |= ConsoleCharacterAttributes.BackgroundBlue;
            if (intense) attributes |= ConsoleCharacterAttributes.BackgroundIntensity;
        }
        else
        {
            if (red) attributes |= ConsoleCharacterAttributes.ForegroundRed;
            if (green) attributes |= ConsoleCharacterAttributes.ForegroundGreen;
            if (blue) attributes |= ConsoleCharacterAttributes.ForegroundBlue;
            if (intense) attributes |= ConsoleCharacterAttributes.ForegroundIntensity;
        }

        return attributes;
    }
}
