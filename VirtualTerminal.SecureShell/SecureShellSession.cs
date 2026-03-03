using Renci.SshNet;
using Renci.SshNet.Common;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using VirtualTerminal.Interop;
using VirtualTerminal.Session;

namespace VirtualTerminal;

/// <summary>
/// A <see cref="TerminalSession"/> implementation backed by SSH (via SSH.NET).
/// It creates an interactive <see cref="ShellStream"/>, forwards incoming data into the session
/// <see cref="TerminalSession.Buffer"/>, and forwards terminal input to the remote host.
/// </summary>
public class SecureShellSession : TerminalSession
{
    private const int MaxReconnectAttempts = 3;
    private const int ReconnectDelayMs = 2000;

    //private readonly Lock _connectLock;
    private readonly bool _ownsClient;

    private ISshClient? _client;
    private ShellStream? _shellStream;

    /// <summary>
    /// Gets the underlying SSH client instance, if initialized.
    /// </summary>
    public ISshClient? Client
    {
        get => _client;
    }

    /// <summary>
    /// Gets the interactive shell stream, if connected.
    /// </summary>
    public ShellStream? ShellStream
    {
        get => _shellStream;
    }

    /// <summary>
    /// Gets whether the underlying SSH client is connected.
    /// </summary>
    public bool IsConnected
    {
        get => _client is not null && _client.IsConnected; 
    }

    /// <summary>
    /// Gets or sets whether the shell stream is flushed after each write.
    /// </summary>
    public bool AutoFlush
    {
        get;
        set;
    } = true;

    /// <summary>
    /// Initializes a new SSH session using an externally created client. The session does not own the client.
    /// </summary>
    /// <param name="client">SSH client instance.</param>
    /// <param name="encoding">Input encoding (defaults to UTF-8).</param>
    public SecureShellSession(ISshClient client, Encoding? encoding = null) : base(encoding ?? Encoding.UTF8)
    {
        _client = client;
        _ownsClient = false;
    }

    /// <summary>
    /// Initializes a new SSH session by creating and owning a new client from the given connection info.
    /// </summary>
    /// <param name="connectionInfo">SSH connection info.</param>
    /// <param name="encoding">Input encoding (defaults to UTF-8).</param>
    public SecureShellSession(ConnectionInfo connectionInfo, Encoding? encoding = null) : base(encoding ?? Encoding.UTF8)
    {
        _client = new SshClient(connectionInfo);
        _ownsClient = true;
    }

    /// <summary>
    /// Initializes a new SSH session using password authentication and an explicit port.
    /// The session owns the created client.
    /// </summary>
    /// <param name="host">Remote host name or IP address.</param>
    /// <param name="port">Remote SSH port.</param>
    /// <param name="username">SSH username.</param>
    /// <param name="password">SSH password.</param>
    /// <param name="encoding">Input encoding (defaults to UTF-8).</param>
    public SecureShellSession(string host, int port, string username, string password, Encoding? encoding = null) : base(encoding ?? Encoding.UTF8)
    {
        _client = new SshClient(host, port, username, password);
        _ownsClient = true;
    }

    /// <summary>
    /// Initializes a new SSH session using password authentication on the default port (22).
    /// The session owns the created client.
    /// </summary>
    /// <param name="host">Remote host name or IP address.</param>
    /// <param name="username">SSH username.</param>
    /// <param name="password">SSH password.</param>
    /// <param name="encoding">Input encoding (defaults to UTF-8).</param>
    public SecureShellSession(string host, string username, string password, Encoding? encoding = null) : base(encoding ?? Encoding.UTF8)
    {
        _client = new SshClient(host, username, password);
        _ownsClient = true;
    }

    /// <summary>
    /// Initializes a new SSH session using key-based authentication and an explicit port.
    /// The session owns the created client.
    /// </summary>
    /// <param name="host">Remote host name or IP address.</param>
    /// <param name="port">Remote SSH port.</param>
    /// <param name="username">SSH username.</param>
    /// <param name="encoding">Input encoding (defaults to UTF-8).</param>
    /// <param name="keyFiles">Private key sources used for authentication.</param>
    public SecureShellSession(string host, int port, string username, Encoding? encoding = null, params IPrivateKeySource[] keyFiles) : base(encoding ?? Encoding.UTF8)
    {
        _client = new SshClient(host, port, username, keyFiles);
        _ownsClient = true;
    }

    /// <summary>
    /// Initializes a new SSH session using key-based authentication on the default port (22).
    /// The session owns the created client.
    /// </summary>
    /// <param name="host">Remote host name or IP address.</param>
    /// <param name="username">SSH username.</param>
    /// <param name="encoding">Input encoding (defaults to UTF-8).</param>
    /// <param name="keyFiles">Private key sources used for authentication.</param>
    public SecureShellSession(string host, string username, Encoding? encoding = null, params IPrivateKeySource[] keyFiles) : base(encoding ?? Encoding.UTF8)
    {
        _client = new SshClient(host, username, keyFiles);
        _ownsClient = true;
    }

    /// <summary>
    /// Connects the SSH client and creates an interactive <see cref="ShellStream"/> with terminal type "xterm".
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public virtual async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client is null)
            throw new ClientUninitializedException("SSH client was not initialized. Use `Connect` method to create client instance");

        _client.KeepAliveInterval = TimeSpan.FromSeconds(30);
        await _client.ConnectAsync(cancellationToken);

        // Intercativity doesnt work without this 'xterm' stream name for some reason
        _shellStream = _client.CreateShellStream("xterm", 80, 24, 800, 600, 1024);
        _shellStream.DataReceived += DataReceived;
    }

    /// <summary>
    /// Attempts to connect without throwing, returning <c>true</c> on success.
    /// </summary>
    public async Task<bool> TryConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await ConnectAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Attempts to reconnect up to a fixed number of times.
    /// </summary>
    public virtual async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; attempt <= MaxReconnectAttempts; attempt++)
        {
            try
            {
                await ConnectAsync(cancellationToken);
                return;
            }
            catch
            {
                await Task.Delay(ReconnectDelayMs, cancellationToken);
            }
        }

        throw new SshConnectionException("Failed to reconnect client");
    }

    /// <summary>
    /// Attempts to reconnect without throwing, returning <c>true</c> on success.
    /// </summary>
    public async Task<bool> TryReconnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await ReconnectAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Disconnects the SSH client and disposes the <see cref="ShellStream"/>.
    /// </summary>
    public virtual async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client is null || !_client.IsConnected)
            return;

        _client.Disconnect();
        _shellStream?.Dispose();

        await Task.Yield();
        _shellStream = null;
    }

    /// <inheritdoc />
    public override void Resize(int columns, int rows)
    {
        ValidateClient();
        Buffer.Resize(columns, rows);
        /*
        CONSOLE_SCREEN_BUFFER_INFO info = Buffer.GetBufferInfo();

        uint nHeight = (uint)(info.srWindow.Bottom - info.srWindow.Top);
        uint nWidth = (uint)(info.srWindow.Right - info.srWindow.Left);
        uint nRows = (uint)(info.dwSize.Y);
        uint nCols = (uint)(info.dwSize.X);
        */
        _shellStream.ChangeWindowSize((uint)Buffer.ColumnsCount, (uint)Buffer.RowsCount, 1200, 800);
    }

    /// <inheritdoc />
    public override void WriteInput(ReadOnlySpan<byte> data)
    {
        ValidateClient();
        if (!_shellStream.CanWrite)
            return;

        _shellStream.Write(data);
        if (AutoFlush)
            _shellStream.Flush();
    }

    /// <summary>
    /// Writes input asynchronously into the SSH shell stream.
    /// </summary>
    public ValueTask WriteInputAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ValidateClient();
        if (!_shellStream.CanWrite)
            return ValueTask.CompletedTask;

        ValueTask valueTask = _shellStream.WriteAsync(data, cancellationToken);
        if (AutoFlush)
            _shellStream.Flush();

        return valueTask;
    }

    /// <summary>
    /// Writes input asynchronously into the SSH shell stream.
    /// </summary>
    public async Task WriteInputAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        ValidateClient();
        if (!_shellStream.CanWrite)
            return;

        await _shellStream.WriteAsync(data, cancellationToken);
        if (AutoFlush)
            await _shellStream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Flushes the shell stream.
    /// </summary>
    public virtual void Flush()
    {
        ValidateClient();
        _shellStream.Flush();
    }

    /// <summary>
    /// Flushes the shell stream asynchronously.
    /// </summary>
    public virtual void FlushAsync(CancellationToken cancellationToken = default)
    {
        ValidateClient();
        _shellStream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Closes current client (if session owns it), and sets it to null
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void ClosePreviousClient()
    {
        if (_ownsClient)
            _client?.Dispose();

        _client = null;
    }

    /// <summary>
    /// Validates that the client is connected and the shell stream exists; otherwise throws.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining), MemberNotNull(nameof(_client), nameof(_shellStream))]
    protected void ValidateClient()
    {
        if (_client == null || !IsConnected || _shellStream is null)
            throw new ClientUninitializedException("SSH client was not initialized. Use `ConnectAsync` method to create and connect client instance");
    }

    /// <summary>
    /// Handles data received from the SSH shell stream by writing into the terminal buffer.
    /// </summary>
    protected virtual void DataReceived(object? sender, ShellDataEventArgs args)
    {
        if (args.Data == null || args.Data.Length == 0)
            return;

        Decoder.Write(Encoding.Convert(InputEncoding, Buffer.Encoding, args.Data));
        NotifyBufferUpdated();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        if (IsConnected)
            DisconnectAsync().Wait(5000);

        if (_ownsClient)
            _client?.Dispose();
    }
}

/// <summary>
/// Thrown when an SSH client or shell stream is not initialized/connected when required.
/// </summary>
public class ClientUninitializedException(string message) : Exception(message);
