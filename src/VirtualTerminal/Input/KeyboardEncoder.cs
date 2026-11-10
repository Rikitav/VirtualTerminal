using VirtualTerminal.Buffer;

namespace VirtualTerminal.Input;

/// <summary>
/// Encodes terminal key events into byte sequences (returned as a <see cref="string"/>).
/// Supports both legacy xterm sequences and modern Kitty Keyboard Protocol progressive enhancements
/// (disambiguation, event types: press/repeat/release, all keys as escape codes).
/// </summary>
public static class KeyboardEncoder
{
    /// <summary>
    /// Encodes a key press event. Returns <c>null</c> when the key is not a special key (let text input handle it).
    /// </summary>
    public static string? Encode(TerminalKey key, TerminalModifier modifiers, in TerminalModes modes)
        => Encode(key, modifiers, in modes, TerminalKeyEventType.Press, null);

    /// <summary>
    /// Encodes a key event with full event type (press/repeat/release) and optional text character support.
    /// Returns <c>null</c> when the key is not encoded as an escape sequence.
    /// </summary>
    public static string? Encode(
        TerminalKey key,
        TerminalModifier modifiers,
        in TerminalModes modes,
        TerminalKeyEventType eventType = TerminalKeyEventType.Press,
        char? text = null)
    {
        int mod = EncodeModifiers(modifiers);
        bool hasModifier = mod > 1;

        bool hasKitty = modes.KittyKeyboardFlags != KittyKeyboardFlags.None;
        bool disambiguate = (modes.KittyKeyboardFlags & KittyKeyboardFlags.DisambiguateEscapeCodes) != 0;
        bool reportEvents = (modes.KittyKeyboardFlags & KittyKeyboardFlags.ReportEventTypes) != 0;
        bool reportAllKeys = (modes.KittyKeyboardFlags & KittyKeyboardFlags.ReportAllKeysAsEscapeCodes) != 0;

        // Key release events are only reported when Kitty ReportEventTypes flag is active
        if (eventType == TerminalKeyEventType.Release && !reportEvents)
            return null;

        // Check if text character should be encoded as escape sequence under Kitty
        if (text.HasValue)
        {
            int code = text.Value;
            if (reportAllKeys || (disambiguate && hasModifier) || (reportEvents && eventType != TerminalKeyEventType.Press))
            {
                return EncodeKitty(code, modifiers, in modes, eventType);
            }

            if (eventType == TerminalKeyEventType.Release)
                return null;
        }

        // Functional / Special key dispatch
        switch (key)
        {
            case TerminalKey.Enter:
                if (disambiguate && (hasModifier || (reportEvents && eventType != TerminalKeyEventType.Press)))
                    return EncodeKitty(13, modifiers, in modes, eventType);
                if (reportAllKeys)
                    return EncodeKitty(13, modifiers, in modes, eventType);
                if (eventType == TerminalKeyEventType.Release)
                    return null;
                return modes.LineFeedNewLine ? "\r\n" : "\r";

            case TerminalKey.Back:
                if (disambiguate && (hasModifier || (reportEvents && eventType != TerminalKeyEventType.Press)))
                    return EncodeKitty(127, modifiers, in modes, eventType);
                if (reportAllKeys)
                    return EncodeKitty(127, modifiers, in modes, eventType);
                if (eventType == TerminalKeyEventType.Release)
                    return null;
                return modes.BackspaceSendsControlH ? "\b" : "\x7f";

            case TerminalKey.Tab:
                if (disambiguate && (hasModifier || (reportEvents && eventType != TerminalKeyEventType.Press)))
                    return EncodeKitty(9, modifiers, in modes, eventType);
                if (reportAllKeys)
                    return EncodeKitty(9, modifiers, in modes, eventType);
                if (eventType == TerminalKeyEventType.Release)
                    return null;
                return hasModifier && (modifiers & TerminalModifier.Shift) != 0 ? "\x1b[Z" : "\t";

            case TerminalKey.Escape:
                if (disambiguate && (hasModifier || (reportEvents && eventType != TerminalKeyEventType.Press)))
                    return EncodeKitty(27, modifiers, in modes, eventType);
                if (reportAllKeys)
                    return EncodeKitty(27, modifiers, in modes, eventType);
                if (eventType == TerminalKeyEventType.Release)
                    return null;
                return "\x1b";

            case TerminalKey.Up:
                return Arrow('A', mod, eventType, reportEvents, modes.ApplicationCursorKeys);
            case TerminalKey.Down:
                return Arrow('B', mod, eventType, reportEvents, modes.ApplicationCursorKeys);
            case TerminalKey.Right:
                return Arrow('C', mod, eventType, reportEvents, modes.ApplicationCursorKeys);
            case TerminalKey.Left:
                return Arrow('D', mod, eventType, reportEvents, modes.ApplicationCursorKeys);

            case TerminalKey.Home:
                return TildeKey(1, mod, eventType, reportEvents);
            case TerminalKey.Insert:
                return TildeKey(2, mod, eventType, reportEvents);
            case TerminalKey.Delete:
                return TildeKey(3, mod, eventType, reportEvents);
            case TerminalKey.End:
                return TildeKey(4, mod, eventType, reportEvents);
            case TerminalKey.PageUp:
                return TildeKey(5, mod, eventType, reportEvents);
            case TerminalKey.PageDown:
                return TildeKey(6, mod, eventType, reportEvents);

            case TerminalKey.F1:
                return FunctionSs3('P', mod, eventType, reportEvents);
            case TerminalKey.F2:
                return FunctionSs3('Q', mod, eventType, reportEvents);
            case TerminalKey.F3:
                return FunctionSs3('R', mod, eventType, reportEvents);
            case TerminalKey.F4:
                return FunctionSs3('S', mod, eventType, reportEvents);

            case TerminalKey.F5:
                return TildeKey(15, mod, eventType, reportEvents);
            case TerminalKey.F6:
                return TildeKey(17, mod, eventType, reportEvents);
            case TerminalKey.F7:
                return TildeKey(18, mod, eventType, reportEvents);
            case TerminalKey.F8:
                return TildeKey(19, mod, eventType, reportEvents);
            case TerminalKey.F9:
                return TildeKey(20, mod, eventType, reportEvents);
            case TerminalKey.F10:
                return TildeKey(21, mod, eventType, reportEvents);
            case TerminalKey.F11:
                return TildeKey(23, mod, eventType, reportEvents);
            case TerminalKey.F12:
                return TildeKey(24, mod, eventType, reportEvents);

            default:
                if (text.HasValue && eventType != TerminalKeyEventType.Release)
                    return null;
                return null;
        }
    }

    /// <summary>
    /// Encodes a Unicode codepoint directly using Kitty Keyboard Protocol format: <c>CSI unicode [; mod [: event]] u</c>.
    /// </summary>
    public static string EncodeKitty(
        int unicodeKey,
        TerminalModifier modifiers,
        in TerminalModes modes,
        TerminalKeyEventType eventType = TerminalKeyEventType.Press)
    {
        int mod = EncodeModifiers(modifiers);
        bool reportEvents = (modes.KittyKeyboardFlags & KittyKeyboardFlags.ReportEventTypes) != 0;
        string modPart = FormatModEvent(mod, eventType, reportEvents);
        return $"\x1b[{unicodeKey}{modPart}u";
    }

    /// <summary>The bracketed-paste envelope (active only when <c>?2004</c> is set).</summary>
    public static string WrapBracketedPaste(string text, bool bracketedPaste)
        => bracketedPaste ? $"\x1b[200~{text}\x1b[201~" : text;

    private static string Arrow(char letter, int mod, TerminalKeyEventType eventType, bool reportEvents, bool applicationCursor)
    {
        if (mod > 1 || (reportEvents && eventType != TerminalKeyEventType.Press))
            return $"\x1b[1{FormatModEvent(mod, eventType, reportEvents)}{letter}";

        return applicationCursor ? $"\x1bO{letter}" : $"\x1b[{letter}";
    }

    private static string TildeKey(int code, int mod, TerminalKeyEventType eventType, bool reportEvents)
    {
        if (mod > 1 || (reportEvents && eventType != TerminalKeyEventType.Press))
            return $"\x1b[{code}{FormatModEvent(mod, eventType, reportEvents)}~";

        return $"\x1b[{code}~";
    }

    private static string FunctionSs3(char letter, int mod, TerminalKeyEventType eventType, bool reportEvents)
    {
        if (mod > 1 || (reportEvents && eventType != TerminalKeyEventType.Press))
            return $"\x1b[1{FormatModEvent(mod, eventType, reportEvents)}{letter}";

        return $"\x1bO{letter}";
    }

    private static string FormatModEvent(int mod, TerminalKeyEventType eventType, bool reportEvents)
    {
        if (reportEvents && eventType != TerminalKeyEventType.Press)
            return $";{mod}:{(int)eventType}";
        if (mod > 1)
            return $";{mod}";
        return "";
    }

    /// <summary>xterm modifier code: 1 + shift + 2*alt + 4*ctrl (+ 8*meta).</summary>
    public static int EncodeModifiers(TerminalModifier modifiers)
    {
        int mod = 1;
        if ((modifiers & TerminalModifier.Shift) != 0)
            mod += 1;

        if ((modifiers & TerminalModifier.Alt) != 0)
            mod += 2;

        if ((modifiers & TerminalModifier.Control) != 0)
            mod += 4;

        if ((modifiers & TerminalModifier.Meta) != 0)
            mod += 8;

        return mod;
    }
}
