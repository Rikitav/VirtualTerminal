using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace VirtualTerminal.Interop;

/// <summary>
/// Creates Win32 pipes used to connect the parent process to a child process (stdin/stdout) for ConPTY scenarios.
/// </summary>
public static partial class Win32PipeFactory
{
    /// <summary>
    /// Creates STDIN pipe for child preocess (WE write -> PROCESS reads).
    /// </summary>
    /// <param name="childReader">Handle for the child process to read from.</param>
    /// <returns>Parent-side stream used to write into child's stdin.</returns>
    public static Stream CreateChildStdInPipe(out IntPtr childReader)
    {
        CreatePipeWithInheritanceFix(out childReader, out IntPtr hWrite, parentIsReader: false);
        return new Win32HandleStream(hWrite, FileAccess.Write, ownsHandle: true);
    }

    /// <summary>
    /// Creates STDOUT/STDERR pipe for child process (PROCESS writes -> WE read).
    /// </summary>
    /// <param name="ChildWriter">Handle for the child process to write to.</param>
    /// <returns>Parent-side stream used to read child's stdout/stderr.</returns>
    public static Stream CreateChildStdOutPipe(out IntPtr ChildWriter)
    {
        CreatePipeWithInheritanceFix(out IntPtr hRead, out ChildWriter, parentIsReader: true);
        return new Win32HandleStream(hRead, FileAccess.Read, ownsHandle: true);
    }

    private static void CreatePipeWithInheritanceFix(out IntPtr hRead, out IntPtr hWrite, bool parentIsReader)
    {
        SECURITY_ATTRIBUTES sa = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = 1, // TRUE - разрешаем наследование по умолчанию
            lpSecurityDescriptor = IntPtr.Zero
        };

        if (!NativeMethods.CreatePipe(out hRead, out hWrite, sa, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create ipc pipe");
        /*
        Important point:
        We have created a pipe where both ends are inherited.
        We need to disable inheritance for the end that remains with the PARENT (us).
        Otherwise, the child process will inherit our end too, which will lead to, 
        that the pipe will never close (deadlocked when reading).
        */

        IntPtr parentHandle = parentIsReader ? hRead : hWrite;
        if (!NativeMethods.SetHandleInformation(parentHandle, NativeMethods.HANDLE_FLAG_INHERIT, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to set ipc handle info");
    }

    private static partial class NativeMethods
    {
        public const int HANDLE_FLAG_INHERIT = 0x00000001;

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetHandleInformation(
            IntPtr hObject,
            int dwMask,
            int dwFlags);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CreatePipe(
            out IntPtr hReadPipe,
            out IntPtr hWritePipe,
            SECURITY_ATTRIBUTES lpPipeAttributes,
            int nSize);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }
}
