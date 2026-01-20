using System.Runtime.InteropServices;

namespace VirtualTerminal.Interop;
#pragma warning disable CS1591

/// <summary>
/// Console input mode flags for Win32 <c>GetConsoleMode</c>/<c>SetConsoleMode</c>.
/// </summary>
[Flags]
public enum ConsoleInputFlags : uint
{
    EnableProcessedInput = 0x0001,
    EnableLineInput = 0x0002,
    EnableEchoInput = 0x0004,
    EnableWindowInput = 0x0008,
    EnableMouseInput = 0x0010,
    EnableInsertMode = 0x0020,
    EnableQuickEditMode = 0x0040,
    EnableExtendedFlags = 0x0080,
    EnableAutoPositiom = 0x0100,
}

/// <summary>
/// Console output mode flags for Win32 <c>GetConsoleMode</c>/<c>SetConsoleMode</c>.
/// </summary>
[Flags]
public enum ConsoleOutputFlags : uint
{
    EnableProcessedOutput = 0x0001,
    EnableWrapAtEolOutput = 0x0002,
    EnableVirtualTerminalProcessing = 0x0004,
    DisableNewlineAutoReturn = 0x0008,
    EnableLVBGridWorldwide = 0x0010,
}

/// <summary>
/// Console character attribute flags (foreground/background RGB + intensity) as used by Win32 console APIs.
/// </summary>
[Flags]
public enum ConsoleCharacterAttributes : ushort
{
    None = 0,

    ForegroundBlue = 0x0001,
    ForegroundGreen = 0x0002,
    ForegroundRed = 0x0004,
    ForegroundIntensity = 0x0008,

    BackgroundBlue = 0x0010,
    BackgroundGreen = 0x0020,
    BackgroundRed = 0x0040,
    BackgroundIntensity = 0x0080
}

/// <summary>
/// Win32 COORD structure used to represent buffer sizes and coordinates in console APIs.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct COORD(short x, short y)
{
    public short X = x;
    public short Y = y;

    public COORD(int x, int y)
        : this((short)x, (short)y) { }
}

/// <summary>
/// Win32 SMALL_RECT structure used to represent a rectangular region in console APIs.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SMALL_RECT
{
    public short Left;
    public short Top;
    public short Right;
    public short Bottom;
}

/// <summary>
/// Win32 CHAR_INFO structure containing a character and its attributes.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct CHAR_INFO : IEquatable<CHAR_INFO>
{
    public ushort Char;
    public ConsoleCharacterAttributes Attributes;

    public readonly bool Equals(CHAR_INFO other)
        => Char == other.Char && Attributes == other.Attributes;

    public readonly override bool Equals(object? obj)
        => obj is CHAR_INFO info && Equals(info);

    public readonly override int GetHashCode()
        => HashCode.Combine(Char.GetHashCode(), Attributes.GetHashCode());

    public static bool operator ==(CHAR_INFO left, CHAR_INFO right)
        => left.Equals(right);

    public static bool operator !=(CHAR_INFO left, CHAR_INFO right)
        => !(left == right);
}

/// <summary>
/// Win32 CONSOLE_SCREEN_BUFFER_INFO structure describing the console buffer and current window region.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct CONSOLE_SCREEN_BUFFER_INFO
{
    public COORD dwSize;
    public COORD dwCursorPosition;
    public ushort wAttributes;
    public SMALL_RECT srWindow;
    public COORD dwMaximumWindowSize;
}
