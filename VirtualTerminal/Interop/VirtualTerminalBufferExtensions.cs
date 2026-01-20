using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VirtualTerminal.Interop;

/// <summary>
/// Extension helpers for configuring and interacting with a <see cref="VirtualTerminalBuffer"/> at the Win32 level
/// (console modes, active buffer binding, cursor visibility).
/// </summary>
public static partial class VirtualTerminalBufferExtensions
{
    /// <summary>
    /// Enables console input mode flags on <see cref="VirtualTerminalBuffer.InputHandle"/>.
    /// </summary>
    public static void EnableInputFlags(this VirtualTerminalBuffer buffer, ConsoleInputFlags flags)
    {
        if (NativeMethods.GetConsoleMode(buffer.InputHandle, out uint mode))
        {
            mode |= (uint)flags;
            NativeMethods.SetConsoleMode(buffer.InputHandle, mode);
        }
    }

    /// <summary>
    /// Disables console input mode flags on <see cref="VirtualTerminalBuffer.InputHandle"/>.
    /// </summary>
    public static void DisableInputFlags(this VirtualTerminalBuffer buffer, ConsoleInputFlags flags)
    {
        if (NativeMethods.GetConsoleMode(buffer.InputHandle, out uint mode))
        {
            mode &= ~(uint)flags;
            NativeMethods.SetConsoleMode(buffer.InputHandle, mode);
        }
    }

    /// <summary>
    /// Enables console output mode flags on <see cref="VirtualTerminalBuffer.OutputHandle"/>.
    /// </summary>
    public static void EnableOutputFlags(this VirtualTerminalBuffer buffer, ConsoleOutputFlags flags)
    {
        if (NativeMethods.GetConsoleMode(buffer.OutputHandle, out uint mode))
        {
            mode |= (uint)flags;
            NativeMethods.SetConsoleMode(buffer.OutputHandle, mode);
        }
    }

    /// <summary>
    /// Disables console output mode flags on <see cref="VirtualTerminalBuffer.OutputHandle"/>.
    /// </summary>
    public static void DisableOutputFlags(this VirtualTerminalBuffer buffer, ConsoleOutputFlags flags)
    {
        if (NativeMethods.GetConsoleMode(buffer.OutputHandle, out uint mode))
        {
            mode &= ~(uint)flags;
            NativeMethods.SetConsoleMode(buffer.OutputHandle, mode);
        }
    }

    /// <summary>
    /// Makes this buffer the active console screen buffer for the current process.
    /// </summary>
    public static void BindActive(this VirtualTerminalBuffer buffer)
    {
        if (buffer.IsDisposed)
            throw new ObjectDisposedException(nameof(VirtualTerminalBuffer));

        if (!NativeMethods.SetConsoleActiveScreenBuffer(buffer.OutputHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetConsoleActiveScreenBuffer failed");
    }

    /// <summary>
    /// Returns whether the console cursor is currently visible for this buffer.
    /// </summary>
    public static bool IsCursorVisible(this VirtualTerminalBuffer buffer)
    {
        if (buffer.IsDisposed)
            throw new ObjectDisposedException(nameof(VirtualTerminalBuffer));

        CONSOLE_CURSOR_INFO lpConsoleCursorInfo = new CONSOLE_CURSOR_INFO();
        if (!NativeMethods.GetConsoleCursorInfo(buffer.OutputHandle, ref lpConsoleCursorInfo))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetConsoleCursorInfo failed");

        return lpConsoleCursorInfo.bVisible;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetConsoleActiveScreenBuffer(IntPtr hConsoleOutput);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetConsoleMode(IntPtr hConsoleHandle, out uint dwMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetConsoleCursorInfo(IntPtr hConsoleOutput, ref CONSOLE_CURSOR_INFO lpConsoleCursorInfo);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CONSOLE_CURSOR_INFO
    {
        public uint dwSize;
        public bool bVisible;
    }
}
