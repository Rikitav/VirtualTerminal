
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace VirtualTerminal.Interop;

/// <summary>
/// Wraps a Windows console screen buffer and exposes methods to write VT/ANSI sequences and read rendered characters
/// for WPF rendering.
/// </summary>
public partial class VirtualTerminalBuffer : IDisposable
{
    private readonly Lock _writeLock = new Lock();

    private IntPtr _inputHandle;
    private IntPtr _outputHandle;
    private int _rows;
    private int _cols;
    private bool _disposed;

    /// <summary>
    /// Gets the handle to the console input device for the current process.
    /// </summary>
    public IntPtr InputHandle => _inputHandle;

    /// <summary>
    /// Gets the handle to the console screen buffer used for output.
    /// </summary>
    public IntPtr OutputHandle => _outputHandle;

    /// <summary>
    /// Gets the current number of rows in the buffer.
    /// </summary>
    public int Rows => _rows;

    /// <summary>
    /// Gets the current number of columns in the buffer.
    /// </summary>
    public int Cols => _cols;

    /// <summary>
    /// Gets whether this instance has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Gets the encoding used by the underlying Windows console buffer (UTF-16 / Unicode).
    /// </summary>
    public static Encoding Encoding
    {
        get => Encoding.Unicode;
    }

    /// <summary>
    /// Initializes a new <see cref="VirtualTerminalBuffer"/> and allocates/configures a backing console if needed.
    /// </summary>
    public VirtualTerminalBuffer()
    {
        InitializeConsoleBuffer();
        ResizeBuffer(120, 400);
    }

    /// <summary>
    /// Defines <see cref="VirtualTerminalBuffer"/>'s initialization logic
    /// </summary>
    /// <exception cref="Win32Exception"></exception>
    protected virtual void InitializeConsoleBuffer()
    {
        // Allocated console is required to work with console buffer
        ConsoleHelper.Allocate();
        ConsoleHelper.SetEncoding();

        SECURITY_ATTRIBUTES securityAttributes = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = 1, // True
            lpSecurityDescriptor = IntPtr.Zero
        };

        _inputHandle = NativeMethods.GetStdHandle(NativeMethods.STD_INPUT_HANDLE);
        _outputHandle = NativeMethods.CreateConsoleScreenBuffer(
            GenericAccess.ReadWrite,
            FileShare.ReadWrite,
            securityAttributes,
            NativeMethods.CONSOLE_TEXTMODE_BUFFER,
            IntPtr.Zero);

        if (NativeMethods.IsInvalidHandleValue(_outputHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to get console output handle");

        if (!NativeMethods.GetConsoleScreenBufferInfo(_outputHandle, out CONSOLE_SCREEN_BUFFER_INFO info))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to get console screen buffer info");

        if (!NativeMethods.SetConsoleMode(_outputHandle, ConsoleOutputFlags.EnableWrapAtEolOutput | ConsoleOutputFlags.EnableVirtualTerminalProcessing | ConsoleOutputFlags.EnableProcessedOutput | ConsoleOutputFlags.DisableNewlineAutoReturn))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to enable VT processing");

        _rows = info.dwSize.Y;
        _cols = info.dwSize.X;
    }

    /// <summary>
    /// Writes raw bytes into the console screen buffer via <c>WriteConsoleW</c>.
    /// </summary>
    /// <param name="data">Data to write (decoded using <see cref="Encoding"/>).</param>
    public void Write(ReadOnlySpan<byte> data)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VirtualTerminalBuffer));

        lock (_writeLock)
        {
            uint charsDecoded = (uint)Encoding.GetCharCount(data);
            if (charsDecoded == 0)
                return;

            if (!NativeMethods.WriteConsole(_outputHandle, data, charsDecoded, out uint written, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteConsole failed");
        }
    }

    /// <summary>
    /// Reads the visible buffer region into a <see cref="CHAR_INFO"/> array.
    /// </summary>
    /// <param name="info">Buffer info describing the current window.</param>
    /// <returns>Flat array of <see cref="CHAR_INFO"/> with size <c>width * height</c>.</returns>
    public CHAR_INFO[] ReadBuffer(CONSOLE_SCREEN_BUFFER_INFO info)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VirtualTerminalBuffer));

        lock (_writeLock)
        {
            int windowWidth = info.dwSize.X;
            int windowHeight = info.dwSize.Y;
            
            /*
            if (windowHeight > 1000)
            {
                windowHeight = 1000;
                info.srWindow.Top = (short)(info.srWindow.Bottom - windowHeight + 1);
            }
            */

            SMALL_RECT readRegion = new SMALL_RECT(0, 0, info.dwSize.X, info.dwSize.Y); //info.srWindow;
            CHAR_INFO[] buffer = new CHAR_INFO[windowWidth * windowHeight];
            COORD bufferSize = new COORD(windowWidth, windowHeight);
            COORD bufferCoord = new COORD(0, 0);

            if (!NativeMethods.ReadConsoleOutput(_outputHandle, buffer, bufferSize, bufferCoord, ref readRegion))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, "ReadConsoleOutput failed");
            }

            _rows = windowHeight;
            _cols = windowWidth;

            return buffer;
        }
    }

    /// <summary>
    /// Gets current buffer information from the OS.
    /// </summary>
    public CONSOLE_SCREEN_BUFFER_INFO GetBufferInfo()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VirtualTerminalBuffer));

        lock (_writeLock)
        {
            if (!NativeMethods.GetConsoleScreenBufferInfo(_outputHandle, out CONSOLE_SCREEN_BUFFER_INFO info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetConsoleScreenBufferInfo failed");

            return info;
        }
    }

    /// <summary>
    /// Resizes the underlying console screen buffer.
    /// </summary>
    /// <param name="cols">New number of columns.</param>
    /// <param name="rows">New number of rows.</param>
    public void ResizeBuffer(int cols, int rows)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VirtualTerminalBuffer));

        lock (_writeLock)
        {
            COORD coord = new COORD(cols, rows);
            if (!NativeMethods.SetConsoleScreenBufferSize(_outputHandle, coord))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetConsoleScreenBufferSize failed");

            _rows = rows;
            _cols = cols;
        }
    }

    /// <summary>
    /// Disposes the underlying console screen buffer handle.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        Dispose(true);
        GC.SuppressFinalize(this);
        _disposed = true;
    }

    /// <inheritdoc />
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        NativeMethods.CloseHandle(_outputHandle);
        _outputHandle = IntPtr.Zero;
        _inputHandle = IntPtr.Zero;
    }

    private static partial class NativeMethods
    {
        public const int STD_INPUT_HANDLE = -10;
        public const uint CONSOLE_TEXTMODE_BUFFER = 1;
        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        public static bool IsInvalidHandleValue(IntPtr handle)
            => handle == IntPtr.Zero || handle == INVALID_HANDLE_VALUE;

        [LibraryImport("kernel32.dll", SetLastError = true)]
        public static partial IntPtr CreateConsoleScreenBuffer(
            GenericAccess dwDesiredAccess,
            FileShare dwShareMode,
            SECURITY_ATTRIBUTES lpSecurityAttributes,
            uint dwFlags,
            IntPtr lpScreenBufferData);

        [LibraryImport("kernel32.dll", EntryPoint = "ReadConsoleOutputW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ReadConsoleOutput(
            IntPtr hConsoleOutput,
            [Out] CHAR_INFO[] lpBuffer,
            COORD dwBufferSize,
            COORD dwBufferCoord,
            ref SMALL_RECT lpReadRegion);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetConsoleScreenBufferInfo(
            IntPtr hConsoleOutput,
            out CONSOLE_SCREEN_BUFFER_INFO lpConsoleScreenBufferInfo);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetConsoleScreenBufferSize(
            IntPtr hConsoleOutput,
            COORD dwSize);

        [LibraryImport("kernel32.dll", EntryPoint = "WriteConsoleW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool WriteConsole(
            IntPtr hConsoleOutput,
            ReadOnlySpan<byte> lpBuffer,
            uint nNumberOfCharsToWrite,
            out uint lpNumberOfCharsWritten,
            IntPtr lpReserved);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CloseHandle(IntPtr hObject);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        public static partial IntPtr GetStdHandle(int nStdHandle);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetConsoleMode(IntPtr hConsoleHandle, ConsoleOutputFlags dwMode);
    }

    [Flags]
    private enum GenericAccess : uint
    {
        Read = 0x80000000,
        Write = 0x40000000,
        ReadWrite = Read | Write
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }
}
