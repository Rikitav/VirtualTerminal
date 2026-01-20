using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VirtualTerminal.Interop;

/// <summary>
/// Small wrapper around a Win32 process handle.
/// </summary>
public partial class Win32Process : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    /// <summary>
    /// Gets the underlying process handle.
    /// </summary>
    public IntPtr Handle => _handle;
    
    /// <summary>
    /// Initializes a new <see cref="Win32Process"/> wrapper for the specified process handle.
    /// </summary>
    /// <param name="handle">Process handle.</param>
    /// <exception cref="InvalidOperationException">Thrown if the handle is invalid.</exception>
    public Win32Process(IntPtr handle)
    {
        if (NativeMethods.IsInvalidHandleValue(handle))
            throw new InvalidOperationException("Process handle was invalid");

        _handle = handle;
    }

    /// <summary>
    /// Uninitializes <see cref="Win32Process"/> instance
    /// </summary>
    ~Win32Process()
    {
        Dispose(false);
    }

    /// <summary>
    /// Terminates the process.
    /// </summary>
    /// <param name="exitCode">Exit code to report.</param>
    public void Terminate(uint exitCode = 0)
    {
        if (!NativeMethods.TerminateProcess(_handle, exitCode))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to terminate process");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        Dispose(true);
        GC.SuppressFinalize(this);
        _disposed = true;
    }

    /// <inheritdoc cref="IDisposable.Dispose"/>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        //NativeMethods.CloseHandle(pInfo.hProcess);
        //NativeMethods.CloseHandle(pInfo.hThread);
        NativeMethods.CloseHandle(_handle);
        _handle = IntPtr.Zero;
    }

    private static partial class NativeMethods
    {
        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        public static bool IsInvalidHandleValue(IntPtr handle)
            => handle == IntPtr.Zero || handle == INVALID_HANDLE_VALUE;

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CloseHandle(IntPtr hObject);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool TerminateProcess(IntPtr hProcess, uint uExitCode);
    }
}
