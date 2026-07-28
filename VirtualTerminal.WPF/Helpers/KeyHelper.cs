using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;

namespace VirtualTerminal.Interop;

/// <summary>
/// Converts WPF keyboard input into VT/ANSI-compatible sequences (VT200 for special keys) or printable text.
/// </summary>
public static partial class KeyHelper
{
    private const string Esc = "\x1b";
    private const string Csi = "\x1b[";

    /// <summary>
    /// Converts a WPF key event into a VT/ANSI string (when possible).
    /// </summary>
    public static string? ConvertToVT(KeyEventArgs e)
    {
        return ConvertToVT(e.Key);
    }

    /// <summary>
    /// Converts a WPF <see cref="Key"/> into a VT200 control sequence (for special keys) or a printable character.
    /// </summary>
    public static string? ConvertToVT(Key key)
    {
        string? vtCode = GetVT200Code(key);
        if (vtCode != null)
            return vtCode;

        return GetCharFromKey(key);
    }

    /*
    public static string? ConvertToCmd(Key key)
    {
        string? vtCode = GetVT200Code(key);
        if (vtCode != null)
            return vtCode;

        return GetCharFromKey(key);
    }
    */

    /// <summary>
    /// Returns a VT200 escape sequence for special keys (arrows, function keys, etc.), or <c>null</c> if not mapped.
    /// </summary>
    public static string? GetVT200Code(Key key)
    {
        return key switch
        {
            Key.Up => Csi + "A",
            Key.Down => Csi + "B",
            Key.Right => Csi + "C",
            Key.Left => Csi + "D",
            Key.Home => Csi + "1~",
            Key.Insert => Csi + "2~",
            Key.Delete => Csi + "3~",
            Key.End => Csi + "4~",
            Key.PageUp => Csi + "5~",
            Key.PageDown => Csi + "6~",
            Key.F1 => Esc + "OP",
            Key.F2 => Esc + "OQ",
            Key.F3 => Esc + "OR",
            Key.F4 => Esc + "OS",
            Key.F5 => Csi + "15~",
            Key.F6 => Csi + "17~",
            Key.F7 => Csi + "18~",
            Key.F8 => Csi + "19~",
            Key.F9 => Csi + "20~",
            Key.F10 => Csi + "21~",
            Key.F11 => Csi + "23~",
            Key.F12 => Csi + "24~",
            Key.Enter => "\r", // "\n"
            Key.Tab => "\t",
            Key.Back => "\x0008", // Csi + "K",
            Key.Escape => Esc,
            _ => null,
        };
    }

    /// <summary>
    /// Attempts to translate a WPF <see cref="Key"/> into a Unicode character using Windows keyboard state APIs.
    /// </summary>
    public static string? GetCharFromKey(Key key) => GetCharFromKey(key, ignoreAlt: false);

    /// <summary>
    /// Same as above, but with <paramref name="ignoreAlt"/> set to true; this neutralizes the Alt key state
    /// before calling ToUnicode. It is used to compose the meta sequence (ESC + base character)
    /// for Left Alt + character: the xterm convention dictates sending ESC followed by the character
    /// produced without Alt (respecting Shift/Caps Lock), rather than the one produced with Alt pressed.
    /// </summary>
    public static string? GetCharFromKey(Key key, bool ignoreAlt)
    {
        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
            return null;

        byte[] keyboardState = new byte[256];

        if (!NativeMethods.GetKeyboardState(keyboardState))
            return null;

        if (ignoreAlt)
        {
            const byte VK_MENU = 0x12;    // Alt (generico)
            const byte VK_LMENU = 0xA4;   // Alt izquierdo
            keyboardState[VK_MENU] = 0;
            keyboardState[VK_LMENU] = 0;
        }

        uint scanCode = NativeMethods.MapVirtualKey((uint)virtualKey, 0);
        StringBuilder stringBuilder = new StringBuilder(5);

        int result = NativeMethods.ToUnicode(
            (uint)virtualKey,
            scanCode,
            keyboardState,
            stringBuilder,
            stringBuilder.Capacity,
            0);

        return result > 0 ? stringBuilder.ToString() : null;
    }

    /// <summary>
    /// True if the right Alt (AltGr) key is pressed. AltGr produces alternative characters in European layouts and is NOT a meta key; it is distinct from the left Alt key.
    /// </summary>
    public static bool IsRightAltPressed()
        => (NativeMethods.GetKeyState(0xA5) & 0x8000) != 0; // VK_RMENU alto = pulsado

    /// <summary>
    /// Returns <c>true</c> if the key is a modifier (Shift/Ctrl/Alt).
    /// </summary>
    public static bool IsModifier(Key key)
        => key == Key.LeftShift || key == Key.RightShift
        || key == Key.LeftCtrl || key == Key.RightCtrl
        || key == Key.LeftAlt || key == Key.RightAlt;

    private static partial class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern int ToUnicode(
            uint wVirtKey,
            uint wScanCode,
            byte[] lpKeyState,
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff,
            int cchBuff,
            uint wFlags);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetKeyboardState([Out] byte[] lpKeyState);

        [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
        public static partial uint MapVirtualKey(uint uCode, uint uMapType);

        [LibraryImport("user32.dll", EntryPoint = "GetKeyState")]
        public static partial short GetKeyState(int nVirtKey);
    }
}
