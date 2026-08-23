using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using VirtualTerminal.Extensions;
using VirtualTerminal.Session;

namespace VirtualTerminal;

/// <summary>Local shell session backed by a Unix pseudo-terminal (PTY).</summary>
public sealed class UnixPtySession : TerminalSession
{
    private const int O_RDWR = 2;
    private const int O_NOCTTY = 0x100;
    private const int SIGTERM = 15;
    private const int SIGKILL = 9;

    private int _masterFd = -1;
    private int _childPid = -1;
    private CancellationTokenSource _readCts = new();
    private Task? _readTask;
    private bool _disposed;

    /// <summary>Starts a new Unix PTY session running the requested shell.</summary>
    /// <param name="shellPath">Path to the shell executable, or null for the user's default shell.</param>
    /// <param name="workingDirectory">Working directory for the shell, or null for the current directory.</param>
    /// <param name="environment">Optional extra environment variables merged into the child environment.</param>
    public UnixPtySession(string? shellPath = null, string? workingDirectory = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        shellPath = ResolveShellPath(shellPath);
        workingDirectory ??= Environment.CurrentDirectory;

        _masterFd = OpenPty();
        string slaveName = GetSlaveName(_masterFd);

        _childPid = ForkAndSpawnShell(_masterFd, slaveName, shellPath, workingDirectory, environment);
        _readTask = Task.Factory.StartNew(ReadOutputLoop, TaskCreationOptions.LongRunning);
    }

    private static string ResolveShellPath(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return requested;

        string? shell = Environment.GetEnvironmentVariable("SHELL");
        return !string.IsNullOrWhiteSpace(shell) ? shell : "/bin/sh";
    }

    private static int OpenPty()
    {
        int fd = posix_openpt(O_RDWR | O_NOCTTY);
        if (fd < 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "posix_openpt failed");

        if (grantpt(fd) != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "grantpt failed");

        if (unlockpt(fd) != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "unlockpt failed");

        return fd;
    }

    private static string GetSlaveName(int masterFd)
    {
        IntPtr ptr = ptsname(masterFd);
        if (ptr == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "ptsname failed");

        return Marshal.PtrToStringAnsi(ptr)
            ?? throw new InvalidOperationException("ptsname returned null");
    }

    private static int ForkAndSpawnShell(int masterFd, string slaveName, string shellPath, string workingDirectory, IReadOnlyDictionary<string, string>? extraEnv)
    {
        int pid = fork();
        if (pid < 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "fork failed");

        if (pid == 0)
        {
            // Child process.
            try
            {
                close(masterFd);

                if (setsid() < 0)
                    Environment.Exit(126);

                int slaveFd = open(slaveName, O_RDWR);
                if (slaveFd < 0)
                    Environment.Exit(125);

                _ = dup2(slaveFd, 0);
                _ = dup2(slaveFd, 1);
                _ = dup2(slaveFd, 2);

                if (slaveFd > 2)
                    close(slaveFd);

                Directory.SetCurrentDirectory(workingDirectory);

                string[] argv = [GetBaseName(shellPath), "-i"];
                string[] envp = BuildEnvironment(extraEnv);

                Exec(shellPath, argv, envp);
            }
            catch
            {
                // ignored — exit cleanly if anything goes wrong.
            }

            Environment.Exit(127);
        }

        return pid;
    }

    private static string GetBaseName(string path)
    {
        int idx = path.LastIndexOf('/');
        return idx >= 0 ? path[(idx + 1)..] : path;
    }

    private static string[] BuildEnvironment(IReadOnlyDictionary<string, string>? extra)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            variables[(string)entry.Key] = (string)entry.Value!;
        }

        if (extra is not null)
        {
            foreach (KeyValuePair<string, string> pair in extra)
            {
                variables[pair.Key] = pair.Value;
            }
        }

        return variables.Select(static kvp => $"{kvp.Key}={kvp.Value}").ToArray();
    }

    private static unsafe void Exec(string path, string[] argv, string[] envp)
    {
        // Marshal arrays into the unmanaged char** layout expected by execve.
        IntPtr[] argvPtrs = new IntPtr[argv.Length + 1];
        IntPtr[] envpPtrs = new IntPtr[envp.Length + 1];

        try
        {
            for (int i = 0; i < argv.Length; i++)
                argvPtrs[i] = Marshal.StringToHGlobalAnsi(argv[i]);

            for (int i = 0; i < envp.Length; i++)
                envpPtrs[i] = Marshal.StringToHGlobalAnsi(envp[i]);

            fixed (IntPtr* argvPtr = argvPtrs)
            fixed (IntPtr* envpPtr = envpPtrs)
            fixed (byte* pathPtr = Encoding.ASCII.GetBytes(path + '\0'))
            {
                execve(pathPtr, argvPtr, envpPtr);
            }
        }
        finally
        {
            foreach (IntPtr ptr in argvPtrs)
            {
                if (ptr != IntPtr.Zero)
                    Marshal.FreeHGlobal(ptr);
            }

            foreach (IntPtr ptr in envpPtrs)
            {
                if (ptr != IntPtr.Zero)
                    Marshal.FreeHGlobal(ptr);
            }
        }
    }

    private void ReadOutputLoop()
    {
        byte[] buffer = new byte[8192];

        try
        {
            while (!_readCts.IsCancellationRequested)
            {
                int bytesRead;
                unsafe
                {
                    fixed (byte* p = buffer)
                    {
                        bytesRead = read(_masterFd, p, buffer.Length);
                    }
                }

                if (bytesRead <= 0)
                    break;

                Decoder.WriteFromEncoding(InputEncoding, buffer.AsSpan(0, bytesRead));
                NotifyBufferUpdated();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{nameof(UnixPtySession)}] Read loop error: {ex}");
        }
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();
        if (_masterFd < 0 || data.IsEmpty)
            return;

        unsafe
        {
            fixed (byte* p = data)
            {
                nint written = 0;
                while (written < data.Length)
                {
                    nint n = write(_masterFd, p + written, (nint)(data.Length - written));
                    if (n < 0)
                    {
                        if (Marshal.GetLastWin32Error() == 4) // EINTR
                            continue;
                        break;
                    }

                    written += n;
                }
            }
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        _disposed = true;

        _readCts.Cancel();

        if (_childPid > 0)
        {
            kill(_childPid, SIGTERM);
            _ = waitpid(_childPid, IntPtr.Zero, 1); // WNOHANG = 1
        }

        _readTask?.GetAwaiter().GetResult();
        _readCts.Dispose();

        if (_childPid > 0)
        {
            kill(_childPid, SIGKILL);
            _ = waitpid(_childPid, IntPtr.Zero, 0);
        }

        if (_masterFd >= 0)
        {
            close(_masterFd);
            _masterFd = -1;
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_openpt(int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int grantpt(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int unlockpt(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr ptsname(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int fork();

    [DllImport("libc", SetLastError = true)]
    private static extern int setsid();

    [DllImport("libc", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPStr)] string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int dup2(int oldfd, int newfd);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int execve(byte* pathname, IntPtr* argv, IntPtr* envp);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int read(int fd, byte* buf, nint count);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int write(int fd, byte* buf, nint count);

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    [DllImport("libc", SetLastError = true)]
    private static extern int waitpid(int pid, IntPtr status, int options);
}
