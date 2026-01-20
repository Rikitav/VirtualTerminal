## VirtualTerminal.SecureShell

**VirtualTerminal.SecureShell** is an addon for the core `VirtualTerminal` library that provides an SSH-based `SecureShellSession`.  
It uses the [`SSH.NET`](https://github.com/sshnet/SSH.NET) library under the hood and connects a remote shell (e.g. `/bin/bash` on Linux) to the `VirtualTerminalView` control.

---

## Getting started

### Reference the project

1. Add a project reference from your WPF app to:
   - `VirtualTerminal.SecureShell`
   - `VirtualTerminal` (core – already referenced by the addon).
2. Ensure your app targets `net10.0-windows` and has WPF enabled.

`VirtualTerminal.SecureShell` already references `SSH.NET` via:

```xml
<PackageReference Include="SSH.NET" Version="2025.1.0" />
```

So you don’t need to add it manually.

### Basic usage with `VirtualTerminalView`

XAML (same as core README):

```xml
<Window x:Class="MyApp.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vt="clr-namespace:VirtualTerminal;assembly=VirtualTerminal">

    <Grid>
        <vt:VirtualTerminalView x:Name="Terminal"
                                AllowDirectInput="True"
                                ScrollDownVisible="True" />
    </Grid>
</Window>
```

Code-behind example connecting with password authentication:

```csharp
using System;
using System.Threading.Tasks;
using System.Windows;
using VirtualTerminal;

public partial class MainWindow : Window
{
    private SecureShellSession? _session;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _session = new SecureShellSession("my-server.example.com", "user", "password");
        Terminal.Session = _session;

        bool connected = await _session.TryConnectAsync();
        if (!connected)
        {
            MessageBox.Show("Failed to connect SSH session.");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _session?.Dispose();
    }
}
```

> **Note**: For real applications you should never hardcode credentials; use secure storage / UI prompts.

---

## `SecureShellSession` API

Located in `SecureShellSession.cs` (namespace `VirtualTerminal`).

### Purpose

- Concrete `TerminalSession` implementation backed by SSH:
  - Manages an `ISshClient` (`SshClient` in practice).
  - Creates a `ShellStream` and forwards its output to `VirtualTerminalBuffer`.
  - Forwards user input from the terminal UI into the SSH shell.

### Core properties

- **`ISshClient? Client`**
  - The underlying `SSH.NET` client.
- **`ShellStream? ShellStream`**
  - Interactive shell stream created from the client.
- **`bool IsConnected`**
  - Indicates whether the client is currently connected.
- **`bool AutoFlush { get; set; } = true`**
  - If `true`, flushes the shell stream after each write to ensure prompt output.

### Constructors

`SecureShellSession` has multiple overloads for flexibility:

- **`SecureShellSession(ISshClient client, Encoding? encoding = null)`**
  - Wraps an externally-created client.
  - Does **not** own the client (won’t dispose it automatically).
- **`SecureShellSession(ConnectionInfo connectionInfo, Encoding? encoding = null)`**
  - Creates and owns a new `SshClient` from a `ConnectionInfo`.
- **`SecureShellSession(string host, int port, string username, string password, Encoding? encoding = null)`**
- **`SecureShellSession(string host, string username, string password, Encoding? encoding = null)`**
- **`SecureShellSession(string host, int port, string username, Encoding? encoding = null, params IPrivateKeySource[] keyFiles)`**
- **`SecureShellSession(string host, string username, Encoding? encoding = null, params IPrivateKeySource[] keyFiles)`**

These overloads cover password and key-based authentication, with or without custom ports.

All constructors call the `TerminalSession` base with the specified or default encoding (`UTF8`).

### Connecting and reconnecting

- **`Task ConnectAsync(CancellationToken cancellationToken = default)`**
  - Validates `_client`, sets `KeepAliveInterval = 30s`, connects the client.
  - Creates a `ShellStream` with stream name `"xterm"` and subscribes to `DataReceived`.
- **`Task<bool> TryConnectAsync(CancellationToken cancellationToken = default)`**
  - Wraps `ConnectAsync` in a try/catch, returning `true` on success and `false` on failure.
- **`Task ReconnectAsync(CancellationToken cancellationToken = default)`**
  - Attempts to reconnect up to `MaxReconnectAttempts` times (default: 3) with a delay (`ReconnectDelayMs`, default: 2000ms).
- **`Task<bool> TryReconnectAsync(CancellationToken cancellationToken = default)`**
  - Same as `ReconnectAsync` but returns `bool` instead of throwing.
- **`Task DisconnectAsync(CancellationToken cancellationToken = default)`**
  - Disconnects the SSH client, disposes the `ShellStream`, then nulls out the reference.

If you need to fully reset the client, use the internal helper:

- **`protected void ClosePreviousClient()`**
  - Disposes `_client` only if `_ownsClient` is `true`, then clears the field.

### Input / output and resizing

- **`override void Resize(int columns, int rows)`**
  - Calls `Buffer.ResizeBuffer(columns, rows)` to update the local buffer.
  - Derives window and buffer sizes from `CONSOLE_SCREEN_BUFFER_INFO`.
  - Calls `_shellStream.ChangeWindowSize(nCols, nRows, nWidth, nHeight)` to inform the remote side.
- **`override void WriteInput(ReadOnlySpan<byte> data)`**
  - Writes directly to `_shellStream` and flushes if `AutoFlush` is `true`.
- **`ValueTask WriteInputAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)`**
  - Asynchronous variant using `ShellStream.WriteAsync`.
- **`Task WriteInputAsync(byte[] data, CancellationToken cancellationToken = default)`**
  - Another async variant with `byte[]` parameter.
- **`virtual void Flush()` / `virtual void FlushAsync(CancellationToken cancellationToken = default)`**
  - Explicit flush operations on the `ShellStream`.

### DataReceived handler

`SecureShellSession` subscribes to `ShellStream.DataReceived`:

- **`protected virtual void DataReceived(object? sender, ShellDataEventArgs args)`**
  - Converts received bytes from `InputEncoding` into `VirtualTerminalBuffer.Encoding`.
  - Writes to `Buffer` and calls `NotifyBufferUpdated()` to trigger UI re-render.

### Disposal

- **`protected override void Dispose(bool disposing)`**
  - If still connected, calls `DisconnectAsync().Wait(5000)`.
  - Disposes `_client` if `_ownsClient` is `true`.

Always dispose sessions you create:

```csharp
_session?.Dispose();
```

---

## Example: key-based authentication

```csharp
using Renci.SshNet;
using VirtualTerminal;

// Private key from file
var keyFile = new PrivateKeyFile(@"C:\Users\me\.ssh\id_rsa");

var session = new SecureShellSession(
    host: "my-server.example.com",
    username: "me",
    encoding: null,            // use default UTF-8
    keyFiles: keyFile);

Terminal.Session = session;
await session.ConnectAsync();
```

The resulting shell behaves like an xterm-compatible remote terminal inside your WPF app.

---

## Error handling and reconnection

- Use **`TryConnectAsync`** when you want to handle connection failures gracefully without exceptions:

```csharp
if (!await session.TryConnectAsync())
{
    // show message, retry, etc.
}
```

- Use **`TryReconnectAsync`** when you want a best-effort reconnection:

```csharp
bool reconnected = await session.TryReconnectAsync();
if (!reconnected)
{
    // maybe notify user and close tab
}
```

If `ValidateClient()` detects that the SSH client is not connected (or `_shellStream` is null), it throws `ClientUninitializedException`.  
This typically means you forgot to call `ConnectAsync` / `TryConnectAsync`, or the connection was lost and not re-established.

---

## Notes & limitations

- **Transport**: Only interactive shell via `ShellStream` is supported out of the box (no SFTP / exec channels here).
- **Encoding**: Defaults to UTF‑8; you can override by passing your own `Encoding` to the constructor.
- **Remote terminal type**: The shell stream is created with name `"xterm"` because many systems expect this for full interactivity.

