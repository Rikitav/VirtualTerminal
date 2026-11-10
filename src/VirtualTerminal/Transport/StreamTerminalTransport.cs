using System.IO.Pipelines;
using VirtualTerminal.Interfaces;

namespace VirtualTerminal.Transport;

/// <summary>
/// An <see cref="ITerminalTransport"/> implementation backed by .NET <see cref="Stream"/> instances
/// and high-performance <see cref="PipeReader"/> / <see cref="PipeWriter"/> pipelines.
/// </summary>
public sealed class StreamTerminalTransport : ITerminalTransport
{
    private readonly Stream? _readStream;
    private readonly Stream? _writeStream;
    private readonly bool _leaveOpen;
    private bool _isConnected;
    private bool _disposed;

    /// <inheritdoc />
    public PipeReader Input { get; }

    /// <inheritdoc />
    public PipeWriter Output { get; }

    /// <inheritdoc />
    public bool IsConnected => _isConnected && !_disposed;

    /// <summary>
    /// Initializes a transport wrapping a single bidirectional duplex stream (such as a NetworkStream or NamedPipeStream).
    /// </summary>
    /// <param name="stream">The bidirectional stream.</param>
    /// <param name="leaveOpen">Whether to leave the underlying stream open when disposing.</param>
    public StreamTerminalTransport(Stream stream, bool leaveOpen = false)
        : this(stream, stream, leaveOpen)
    {
    }

    /// <summary>
    /// Initializes a transport wrapping separate read and write streams (such as PTY in/out pipes).
    /// </summary>
    /// <param name="readStream">The input stream.</param>
    /// <param name="writeStream">The output stream.</param>
    /// <param name="leaveOpen">Whether to leave underlying streams open when disposing.</param>
    public StreamTerminalTransport(Stream readStream, Stream writeStream, bool leaveOpen = false)
    {
        _readStream = readStream ?? throw new ArgumentNullException(nameof(readStream));
        _writeStream = writeStream ?? throw new ArgumentNullException(nameof(writeStream));
        _leaveOpen = leaveOpen;

        Input = PipeReader.Create(_readStream, new StreamPipeReaderOptions(leaveOpen: true));
        Output = PipeWriter.Create(_writeStream, new StreamPipeWriterOptions(leaveOpen: true));
        _isConnected = true;
    }

    /// <summary>
    /// Initializes a transport directly from pre-existing <see cref="PipeReader"/> and <see cref="PipeWriter"/>.
    /// </summary>
    public StreamTerminalTransport(PipeReader reader, PipeWriter writer)
    {
        Input = reader ?? throw new ArgumentNullException(nameof(reader));
        Output = writer ?? throw new ArgumentNullException(nameof(writer));
        _isConnected = true;
    }

    /// <summary>
    /// Creates a connected pair of in-memory duplex transports for testing or in-process communication.
    /// </summary>
    public static (StreamTerminalTransport Client, StreamTerminalTransport Server) CreateInMemoryPair(PipeOptions? options = null)
    {
        options ??= PipeOptions.Default;
        Pipe pipeAtoB = new(options);
        Pipe pipeBtoA = new(options);

        StreamTerminalTransport transportA = new(pipeBtoA.Reader, pipeAtoB.Writer);
        StreamTerminalTransport transportB = new(pipeAtoB.Reader, pipeBtoA.Writer);

        return (transportA, transportB);
    }

    /// <inheritdoc />
    public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(StreamTerminalTransport));

        _isConnected = true;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
            return;

        _isConnected = false;
        await Input.CompleteAsync().ConfigureAwait(false);
        await Output.CompleteAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _isConnected = false;

        Input.Complete();
        Output.Complete();

        if (!_leaveOpen)
        {
            _readStream?.Dispose();
            if (!ReferenceEquals(_readStream, _writeStream))
                _writeStream?.Dispose();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _isConnected = false;

        await Input.CompleteAsync().ConfigureAwait(false);
        await Output.CompleteAsync().ConfigureAwait(false);

        if (!_leaveOpen)
        {
            if (_readStream != null)
                await _readStream.DisposeAsync().ConfigureAwait(false);
            if (!ReferenceEquals(_readStream, _writeStream) && _writeStream != null)
                await _writeStream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
