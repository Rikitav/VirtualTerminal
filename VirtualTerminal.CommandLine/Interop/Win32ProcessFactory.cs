using System.ComponentModel;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;

namespace VirtualTerminal.Interop;

/// <summary>
/// Minimal process creation info used by <see cref="Win32ProcessFactory"/> when starting a process attached to ConPTY.
/// </summary>
public struct ProcessCreationInfo
{
    /// <summary>
    /// Optional application name passed to <c>CreateProcess</c>.
    /// </summary>
    public string? ApplicationName;

    /// <summary>
    /// Optional command line passed to <c>CreateProcess</c>.
    /// </summary>
    public string? CommandLine;
}

/// <summary>
/// Starts a Win32 process, optionally attaching it to a ConPTY pseudo console via extended startup info.
/// </summary>
public static partial class Win32ProcessFactory
{
    /// <summary>
    /// Starts the process described by <paramref name="info"/> and returns a wrapper for its handle.
    /// </summary>
    /// <param name="info">Application and command line data.</param>
    /// <param name="pcHandle">ConPTY pseudo console handle, or -1 when not using a pseudo console.</param>
    /// <returns>A <see cref="Win32Process"/> wrapping the process handle.</returns>
    public static Win32Process Start(ProcessCreationInfo info, IntPtr pcHandle = -1)
    {
        STARTUPINFOEX startupInfo = ConfigureProcessThread(pcHandle);
        PROCESS_INFORMATION ProcInfo = new PROCESS_INFORMATION();
        
        SECURITY_ATTRIBUTES lpProcessAttributes = new SECURITY_ATTRIBUTES() { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>() };
        SECURITY_ATTRIBUTES lpThreadAttributes = new SECURITY_ATTRIBUTES() { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>() };

        bool processSuccess = NativeMethods.CreateProcess(
            info.ApplicationName,
            info.CommandLine,
            ref lpProcessAttributes,
            ref lpThreadAttributes,
            false,
            ProcessCreationFlag.EXTENDED_STARTUPINFO_PRESENT,
            null,
            null,
            ref startupInfo,
            ref ProcInfo);

        if (!processSuccess)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to create process: {info.ApplicationName}");

        NativeMethods.CloseHandle(ProcInfo.hThread);
        return new Win32Process(ProcInfo.hProcess);
    }

    /*
    private unsafe static STARTUPINFOEX ConfigureProcessThreadUnsafe(IntPtr pcHandle)
    {
        IntPtr lpSize = IntPtr.Zero;
        if (!NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref lpSize))
        {
            int errorCode = Marshal.GetLastWin32Error();
            if (errorCode != 122) // ERROR_INSUFFICIENT_BUFFER, expected
                throw new Win32Exception(errorCode, "Failed to initialize global threads' attributes list");
        }

        STARTUPINFOEX startupInfo = new STARTUPINFOEX();
        startupInfo.StartupInfo.cb = (uint)Marshal.SizeOf<STARTUPINFOEX>();
        startupInfo.lpAttributeList = Marshal.AllocHGlobal(lpSize);

        if (!NativeMethods.InitializeProcThreadAttributeList(startupInfo.lpAttributeList, 1, 0, ref lpSize))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to initialize StartupInfos' thread attributes list");

        bool updateSuccess = NativeMethods.UpdateProcThreadAttribute(
            startupInfo.lpAttributeList, 0,
            new nint(NativeMethods.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE),
            pcHandle.ToPointer(),
            IntPtr.Size,
            IntPtr.Zero,
            IntPtr.Zero
        );

        if (!updateSuccess)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to update StartupInfos' thread attribute");

        return startupInfo;
    }
    */

    private static STARTUPINFOEX ConfigureProcessThread(IntPtr pcHandle)
    {
        STARTUPINFOEX startupInfo = new STARTUPINFOEX();
        startupInfo.StartupInfo.cb = (uint)Marshal.SizeOf<STARTUPINFOEX>();

        IntPtr lpSize = IntPtr.Zero;
        if (!NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref lpSize))
        {
            int errorCode = Marshal.GetLastWin32Error();
            if (errorCode != 122) // ERROR_INSUFFICIENT_BUFFER, expected
                throw new Win32Exception(errorCode, "Failed to initialize global threads' attributes list");
        }

        startupInfo.lpAttributeList = Marshal.AllocHGlobal(lpSize);
        if (!NativeMethods.InitializeProcThreadAttributeList(startupInfo.lpAttributeList, 1, 0, ref lpSize))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to initialize StartupInfos' thread attributes list");

        bool updateSuccess = NativeMethods.UpdateProcThreadAttribute(
            startupInfo.lpAttributeList, 0,
            NativeMethods.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
            pcHandle,
            IntPtr.Size,
            IntPtr.Zero,
            IntPtr.Zero);

        if (!updateSuccess)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to update StartupInfos' thread attribute");

        return startupInfo;
    }

    private static partial class NativeMethods
    {
        public const int SW_HIDE = 0;
        public const int STARTF_USESTDHANDLES = 0x00000100;
        public const int STARTF_USESHOWWINDOW = 0x00000001;
        public const int EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
        public const nint PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = (IntPtr)131094U;
        public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

        [LibraryImport("kernel32.dll", SetLastError = true, EntryPoint = "CreateProcessW", StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CreateProcess(
            string? lpApplicationName,
            string? lpCommandLine,
            ref SECURITY_ATTRIBUTES lpProcessAttributes,
            ref SECURITY_ATTRIBUTES lpThreadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
            ProcessCreationFlag dwCreationFlags,
            string? lpEnvironment,
            string? lpCurrentDirectory,
            ref STARTUPINFOEX lpStartupInfo,
            ref PROCESS_INFORMATION lpProcessInformation);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr Attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static unsafe partial bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr Attribute, void* lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        public static partial void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CloseHandle(IntPtr hObject);
    }

    private enum ProcessCreationFlag : uint
    {
        CREATE_BREAKAWAY_FROM_JOB = 0x01000000,
        CREATE_DEFAULT_ERROR_MODE = 0x04000000,
        CREATE_NEW_CONSOLE = 0x00000010,
        CREATE_NEW_PROCESS_GROUP = 0x00000200,
        CREATE_NO_WINDOW = 0x08000000,
        CREATE_PROTECTED_PROCESS = 0x00040000,
        CREATE_PRESERVE_CODE_AUTHZ_LEVEL = 0x02000000,
        CREATE_SECURE_PROCESS = 0x00400000,
        CREATE_SEPARATE_WOW_VDM = 0x00000800,
        CREATE_SHARED_WOW_VDM = 0x00001000,
        CREATE_SUSPENDED = 0x00000004,
        CREATE_UNICODE_ENVIRONMENT = 0x00000400,
        DEBUG_ONLY_THIS_PROCESS = 0x00000002,
        DEBUG_PROCESS = 0x00000001,
        DETACHED_PROCESS = 0x00000008,
        EXTENDED_STARTUPINFO_PRESENT = 0x00080000,
        INHERIT_PARENT_AFFINITY = 0x00010000,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public uint cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }
}
