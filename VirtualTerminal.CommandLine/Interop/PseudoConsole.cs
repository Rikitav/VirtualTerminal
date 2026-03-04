using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace VirtualTerminal.Interop;

/// <summary>
/// Wraps a Windows ConPTY pseudo console handle and its associated process and pipes.
/// </summary>
public partial class PseudoConsole : IDisposable
{
    private IntPtr _handle = IntPtr.Zero;
    private Win32Process _process = null!;
    private Stream _writer = null!;
    private Stream _reader = null!;
    private bool _disposed;

    /// <summary>
    /// Writer stream, that transfer data to child process' STD_INPUT
    /// </summary>
    public Stream? Writer => _writer;

    /// <summary>
    /// Reader stream, that receives data from child process' STD_OUTPUT
    /// </summary>
    public Stream? Reader => _reader;

    /// <summary>
    /// Initializes a new <see cref="PseudoConsole"/>.
    /// </summary>
    /// <param name="pseudoConsoleHandle">ConPTY handle.</param>
    /// <param name="associatedProcesss">Process attached to this pseudo console.</param>
    /// <param name="writer">Stream used to write input to the child process.</param>
    /// <param name="reader">Stream used to read output from the child process.</param>
    public PseudoConsole(IntPtr pseudoConsoleHandle, Win32Process associatedProcesss, Stream writer, Stream reader)
    {
        _handle = pseudoConsoleHandle;
        _process = associatedProcesss ?? throw new ArgumentNullException(nameof(associatedProcesss));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <summary>
    /// Resizes the pseudo console buffer.
    /// </summary>
    /// <param name="cols">Columns.</param>
    /// <param name="rows">Rows.</param>
    public void Resize(ushort cols, ushort rows)
    {
        if (NativeMethods.ResizePseudoConsole(_handle, new COORD(cols, rows)) != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Faile to resize pseudo consoles' buffer");
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

        if (_handle != IntPtr.Zero)
        {
            NativeMethods.ClosePseudoConsole(_handle);
            _handle = IntPtr.Zero;
        }

        if (_process != null)
        {
            _process.Dispose();
            _process = null!;
        }

        if (_writer != null)
        {
            _writer.Dispose();
            _writer = null!;
        }

        if (_reader != null)
        {
            _reader.Dispose();
            _reader = null!;
        }
    }

    private static partial class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        public static partial void ClosePseudoConsole(IntPtr hPC);
    }
}
