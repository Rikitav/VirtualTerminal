namespace VirtualTerminal.Input;

/// <summary>
/// Encodes mouse events into xterm report sequences. SGR (1006) is preferred when enabled;
/// otherwise the legacy <c>CSI M Cb Cx Cy</c> encoding. Full pointer wiring in the control is a
/// Phase 2 concern, but the encoders are provided here.
/// </summary>
public static class MouseEncoder
{
    /// <summary>Encodes a button event using SGR (1006) if enabled, else the legacy form.</summary>
    public static string EncodeButton(
        TerminalMouseButton button,
        bool pressed,
        int x,
        int y,
        TerminalModifier modifiers,
        bool sgrEncoding)
    {
        int code = ButtonCode(button, pressed);
        code |= ModifierFlags(modifiers);

        if (sgrEncoding)
        {
            char suffix = pressed ? 'M' : 'm';
            return $"\x1b[<{code};{x + 1};{y + 1}{suffix}";
        }

        return LegacyEncode(code, x, y);
    }

    /// <summary>Encodes a mouse-wheel event.</summary>
    public static string EncodeWheel(bool up, int x, int y, TerminalModifier modifiers, bool sgrEncoding)
    {
        int code = (up ? 64 : 65) | ModifierFlags(modifiers);
        return sgrEncoding
            ? $"\x1b[<{code};{x + 1};{y + 1}M"
            : LegacyEncode(code, x, y);
    }

    private static string LegacyEncode(int code, int x, int y)
    {
        // Legacy encoding clamps coordinates to 1..223 (95-column limit) and offsets by 32.
        int cb = code + 32;
        int cx = ClampCoord(x + 1);
        int cy = ClampCoord(y + 1);
        return $"\x1b[M{(char)cb}{(char)cx}{(char)cy}";
    }

    private static int ClampCoord(int v) => v < 1 ? 1 : (v > 95 ? 95 : v) + 31;

    private static int ButtonCode(TerminalMouseButton button, bool pressed) => button switch
    {
        TerminalMouseButton.Left => 0,
        TerminalMouseButton.Middle => 1,
        TerminalMouseButton.Right => 2,
        TerminalMouseButton.XButton1 => 8,
        TerminalMouseButton.XButton2 => 9,
        _ => pressed ? 0 : 3,  // release without known button
    };

    private static int ModifierFlags(TerminalModifier modifiers)
    {
        int flags = 0;
        if ((modifiers & TerminalModifier.Shift) != 0) flags |= 4;
        if ((modifiers & TerminalModifier.Alt) != 0) flags |= 8;
        if ((modifiers & TerminalModifier.Control) != 0) flags |= 16;
        return flags;
    }
}
