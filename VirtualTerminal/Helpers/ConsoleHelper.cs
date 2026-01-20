using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace VirtualTerminal.Interop;

/// <summary>
/// Helpers for allocating and configuring a Windows console that backs <see cref="VirtualTerminalBuffer"/>.
/// </summary>
public static partial class ConsoleHelper
{
    /// <summary>
    /// Allocates a console for the current process if one doesn't exist, and hides its window.
    /// This is required to work with console screen buffers.
    /// </summary>
    public static void Allocate()
    {
        const int ERROR_ACCESS_DENIED = 5;
        const int SW_HIDE = 0;

        if (NativeMethods.GetConsoleWindow() == IntPtr.Zero)
        {
            ALLOC_CONSOLE_OPTIONS options = new ALLOC_CONSOLE_OPTIONS()
            {
                Mode = AllocConsoleMode.NoWindow,
                UseShowWindow = 1, // TRUE
                showWindow = SW_HIDE,
            };

            if (!NativeMethods.AllocConsole())
            {
                // if access denied, that means, console already allocated
                int error = Marshal.GetLastWin32Error();
                if (error != ERROR_ACCESS_DENIED)
                    throw new Win32Exception(error, "Warning: AllocConsole failed");
            }
            else
            {
                IntPtr hwnd = NativeMethods.GetConsoleWindow();
                if (hwnd != IntPtr.Zero)
                    NativeMethods.ShowWindow(hwnd, SW_HIDE);
            }
        }
    }

    /// <summary>
    /// Sets console input and output code pages to UTF-8.
    /// </summary>
    public static void SetEncoding()
    {
        uint encoding = (uint)Encoding.UTF8.CodePage;

        if (!NativeMethods.SetConsoleOutputCP(encoding))
        {
            int error = Marshal.GetLastWin32Error();
            Debug.WriteLine($"Warning: SetConsoleOutputCP failed. Error: {error}");
        }

        if (!NativeMethods.SetConsoleCP(encoding))
        {
            int error = Marshal.GetLastWin32Error();
            Debug.WriteLine($"Warning: SetConsoleCP failed. Error: {error}");
        }
    }

    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool AllocConsole();

        [LibraryImport("kernel32.dll")]
        public static partial IntPtr GetConsoleWindow();

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetConsoleOutputCP(uint wCodePageID);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetConsoleCP(uint wCodePageID);
    }

    private enum AllocConsoleMode
    {
        Default = 0,
        NewWindow = 1,
        NoWindow = 2
    }

    private struct ALLOC_CONSOLE_OPTIONS
    {
        public AllocConsoleMode Mode;
        public int UseShowWindow;
        public ushort showWindow;
    }
}
