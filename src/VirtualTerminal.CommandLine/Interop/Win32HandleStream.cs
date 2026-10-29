using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace VirtualTerminal.Interop;

/// <summary>
/// A <see cref="Stream"/> implementation over a raw Win32 handle (typically a pipe).
/// Used by ConPTY plumbing to expose stdin/stdout pipes as .NET streams.
/// </summary>
public partial class Win32HandleStream : Stream
{
    private readonly IntPtr _handle;
    private readonly FileAccess _access;
    private readonly bool _ownsHandle;

    /// <inheritdoc/>
    public override bool CanRead
    {
        get => _access.HasFlag(FileAccess.Read);
    }

    /// <inheritdoc/>
    public override bool CanWrite
    {
        get => _access.HasFlag(FileAccess.Write);
    }

    /// <inheritdoc/>
    public override bool CanSeek
    {
        get => false;
    }

    /// <inheritdoc/>
    public override long Length
    {
        get => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// Initializes a new <see cref="Win32HandleStream"/>.
    /// </summary>
    /// <param name="handle">Win32 handle to wrap.</param>
    /// <param name="access">Allowed access for the stream.</param>
    /// <param name="ownsHandle">Whether this stream should close the handle on dispose.</param>
    public Win32HandleStream(IntPtr handle, FileAccess access, bool ownsHandle)
    {
        _handle = handle;
        _access = access;
        _ownsHandle = ownsHandle;
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        // ...
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (!CanRead)
            throw new NotSupportedException("Stream does not support reading.");

        return Read(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        if (!CanRead)
            throw new NotSupportedException("Stream does not support reading.");

        uint bytesRead = 0;
        bool success = NativeMethods.ReadFile(
            _handle, buffer, (uint)buffer.Length,
            ref bytesRead, IntPtr.Zero);

        if (success)
            return (int)bytesRead;

        int error = Marshal.GetLastWin32Error();
        if (error == 109) // child process closed connection (normal scenarion)
            return 0;

        throw new Win32Exception(error, "ReadFile failed");
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        if (!CanWrite)
            throw new NotSupportedException("Stream does not support writing.");

        Write(new ReadOnlySpan<byte>(buffer, offset, count));
    }

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (!CanWrite)
            throw new NotSupportedException("Stream does not support writing.");

        bool success = NativeMethods.WriteFile(
            _handle, buffer, (uint)buffer.Length,
            out _, IntPtr.Zero);

        if (!success)
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, "WriteFile failed");
        }
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        if (_ownsHandle)
            NativeMethods.CloseHandle(_handle);
    }

    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ReadFile(
            IntPtr hFile,
            Span<byte> lpBuffer,
            uint nNumberOfBytesToRead,
            ref uint lpNumberOfBytesRead,
            IntPtr lpOverlapped);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool WriteFile(
            IntPtr hFile,
            ReadOnlySpan<byte> lpBuffer,
            uint nNumberOfBytesToWrite,
            out uint lpNumberOfBytesWritten,
            IntPtr lpOverlapped);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CloseHandle(IntPtr hObject);
    }
}