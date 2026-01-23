using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace VirtualTerminal.Interop;

/// <summary>
/// Extension helpers for configuring and interacting with a <see cref="VirtualTerminalBuffer"/> at the Win32 level
/// (console modes, active buffer binding, cursor visibility).
/// </summary>
public static partial class VirtualTerminalBufferExtensions
{
    /// <summary>
    /// Writes raw bytes encoded from <paramref name="encoding"/> into the console screen buffer via <c>WriteConsoleW</c>.
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="encoding"></param>
    /// <param name="data"></param>
    public static void WriteFromEncoding(this VirtualTerminalBuffer buffer, Encoding encoding, ReadOnlySpan<byte> data)
    {
        int maxCharCount = encoding.GetMaxCharCount(data.Length);
        Span<char> charBuffer = stackalloc char[maxCharCount];

        int charsWritten = encoding.GetChars(data, charBuffer);
        if (charsWritten == 0)
            return;

        buffer.Write(charBuffer.Slice(0, charsWritten));
        return;
    }

    /// <summary>
    /// Writes character into the console screen buffer via <c>WriteConsoleW</c> in <see cref="VirtualTerminalBuffer.Encoding"/>.
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="data"></param>
    public static void Write(this VirtualTerminalBuffer buffer, ReadOnlySpan<char> data)
    {
        int bytesCount = VirtualTerminalBuffer.Encoding.GetMaxByteCount(data.Length);
        Span<byte> bytes = stackalloc byte[bytesCount];
        
        int bytesWritten = VirtualTerminalBuffer.Encoding.GetBytes(data, bytes);
        if (bytesCount == 0)
            return;

        buffer.Write(bytes.Slice(0, bytesWritten));
    }

    /// <summary>
    /// Writes character into the console screen buffer via <c>WriteConsoleW</c> in <see cref="VirtualTerminalBuffer.Encoding"/>.
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="data"></param>
    /// <param name="offset"></param>
    /// <param name="length"></param>
    public static void Write(this VirtualTerminalBuffer buffer, ReadOnlySpan<char> data, int offset, int length)
    {
        buffer.Write(data.Slice(offset, length));
    }

    /// <summary>
    /// Writes raw bytes into the console screen buffer via <c>WriteConsoleW</c>.
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="data"></param>
    /// <param name="offset"></param>
    /// <param name="length"></param>
    public static void Write(this VirtualTerminalBuffer buffer, ReadOnlySpan<byte> data, int offset, int length)
    {
        buffer.Write(data.Slice(offset, length));
    }

    /// <summary>
    /// Writes raw bytes into the console screen buffer via <c>WriteConsoleW</c>.
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="encoding"></param>
    /// <param name="data"></param>
    /// <param name="offset"></param>
    /// <param name="length"></param>
    public static void WriteFromEncoding(this VirtualTerminalBuffer buffer, Encoding encoding, ReadOnlySpan<byte> data, int offset, int length)
    {
        buffer.WriteFromEncoding(encoding, data.Slice(offset, length));
    }

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

    /// <summary>
    /// Returns console cursor position for this buffer
    /// </summary>
    /// <param name="buffer"></param>
    /// <returns></returns>
    public static COORD GetCursorPosition(this VirtualTerminalBuffer buffer)
    {
        return buffer.GetBufferInfo().dwCursorPosition;
    }

    /// <summary>
    /// Sets console cursor position for this buffer
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <exception cref="ObjectDisposedException"></exception>
    /// <exception cref="Win32Exception"></exception>
    public static void SetCursorPosition(this VirtualTerminalBuffer buffer, int x, int y)
    {
        if (buffer.IsDisposed)
            throw new ObjectDisposedException(nameof(VirtualTerminalBuffer));

        if (!NativeMethods.SetConsoleCursorPosition(buffer.OutputHandle, new COORD(x, y)))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetConsoleCursorInfo failed");
    }

    /// <summary>
    /// Sets console cursor position for this buffer
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="coord"></param>
    /// <exception cref="ObjectDisposedException"></exception>
    /// <exception cref="Win32Exception"></exception>
    public static void SetCursorPosition(this VirtualTerminalBuffer buffer, COORD coord)
    {
        if (buffer.IsDisposed)
            throw new ObjectDisposedException(nameof(VirtualTerminalBuffer));

        if (!NativeMethods.SetConsoleCursorPosition(buffer.OutputHandle, coord))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetConsoleCursorInfo failed");
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

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetConsoleCursorPosition(IntPtr hConsoleOutput, COORD dwCursorPosition);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CONSOLE_CURSOR_INFO
    {
        public uint dwSize;
        public bool bVisible;
    }
}
