## VirtualTerminal.SecureShell

**VirtualTerminal.SecureShell** is an addon for the core `VirtualTerminal` library that provides an SSH-based `SecureShellSession`.  
It uses the [`SSH.NET`](https://github.com/sshnet/SSH.NET) library and connects a remote shell (e.g. `/bin/bash` on Linux) to the `TerminalControl`.

---

## Getting started

### Reference the project

1. Add a project reference from your app to:
   - `VirtualTerminal.SecureShell`
   - `VirtualTerminal` (core – already referenced by the addon).
2. Ensure your app targets `net10.0-windows` and has WPF or Avalonia enabled.

`VirtualTerminal.SecureShell` already references `SSH.NET`:

```xml
<PackageReference Include="SSH.NET" Version="2025.1.0" />
```

So you don’t need to add it manually.

### Basic usage

```xml
<Window ...
        xmlns:vt="clr-namespace:VirtualTerminal;assembly=VirtualTerminal.WPF">
    <Grid>
        <vt:TerminalControl x:Name="Terminal" AllowDirectInput="True" />
    </Grid>
</Window>
```

```csharp
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

Concrete `TerminalSession` implementation backed by SSH:

- Manages an `ISshClient` (`SshClient` in practice).
- Creates a `ShellStream` with terminal type `"xterm"`.
- Forwards user input from the terminal UI into the SSH shell.
- Forwards incoming shell output into the local `TerminalDecoder`.

### Core properties

- **`ISshClient? Client`** – the underlying SSH client.
- **`ShellStream? ShellStream`** – interactive shell stream, if connected.
- **`bool IsConnected`** – whether the client is connected.
- **`bool AutoFlush { get; set; } = true`** – flushes the shell stream after each write.

### Constructors

- **`SecureShellSession(ISshClient client, Encoding? encoding = null)`**
  - Wraps an externally-created client; does **not** own it.
- **`SecureShellSession(ConnectionInfo connectionInfo, Encoding? encoding = null)`**
  - Creates and owns a new `SshClient`.
- **`SecureShellSession(string host, int port, string username, string password, Encoding? encoding = null)`**
- **`SecureShellSession(string host, string username, string password, Encoding? encoding = null)`**
- **`SecureShellSession(string host, int port, string username, Encoding? encoding = null, params IPrivateKeySource[] keyFiles)`**
- **`SecureShellSession(string host, string username, Encoding? encoding = null, params IPrivateKeySource[] keyFiles)`**

All constructors call the `TerminalSession` base with the specified or default encoding (`UTF8`).

### Connecting and reconnecting

- **`Task ConnectAsync(CancellationToken cancellationToken = default)`**
  - Connects the client and creates the interactive `ShellStream`.
- **`Task<bool> TryConnectAsync(CancellationToken cancellationToken = default)`**
  - Same as `ConnectAsync` but returns `true`/`false` instead of throwing.
- **`Task ReconnectAsync(CancellationToken cancellationToken = default)`**
  - Attempts to reconnect up to 3 times with a 2 s delay.
- **`Task<bool> TryReconnectAsync(CancellationToken cancellationToken = default)`**
  - Non-throwing variant of `ReconnectAsync`.
- **`Task DisconnectAsync(CancellationToken cancellationToken = default)`**
  - Disconnects the client and disposes the `ShellStream`.

### Input / output

- **`override void Write(ReadOnlySpan<byte> data)`**
  - Writes input bytes to `_shellStream`. Flushes if `AutoFlush` is `true`.
- **`ValueTask WriteInputAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)`**
- **`Task WriteInputAsync(byte[] data, CancellationToken cancellationToken = default)`**
  - Asynchronous variants.
- **`virtual void Flush()`** / **`virtual Task FlushAsync(CancellationToken cancellationToken = default)`**
  - Explicit flushes.

Incoming data is handled by `DataReceived`, which writes into the local `TerminalDecoder` and calls `NotifyBufferUpdated()`.

### Resize

- **`override void Resize(ushort columns, ushort rows)`**
  - Currently resizes the local `TerminalScreenBuffer` only. Forwarding the new size to the remote shell stream is not yet implemented.

### Disposal

- **`protected override void Dispose(bool disposing)`**
  - Disconnects if connected, then disposes the owned client.

Always dispose sessions you create:

```csharp
_session?.Dispose();
```

---

## Example: key-based authentication

```csharp
using Renci.SshNet;
using VirtualTerminal;

var keyFile = new PrivateKeyFile(@"C:\Users\me\.ssh\id_rsa");

var session = new SecureShellSession(
    host: "my-server.example.com",
    username: "me",
    encoding: null,            // use default UTF-8
    keyFiles: keyFile);

Terminal.Session = session;
await session.ConnectAsync();
```

The resulting shell behaves like an xterm-compatible remote terminal inside your app.

---

## Error handling and reconnection

Use `TryConnectAsync` / `TryReconnectAsync` to handle failures without exceptions:

```csharp
if (!await session.TryConnectAsync())
{
    // show message, retry, etc.
}
```

```csharp
bool reconnected = await session.TryReconnectAsync();
if (!reconnected)
{
    // notify user and close tab
}
```

If the client is not connected when `Write` / async write / `Flush` is called, a `ClientUninitializedException` is thrown.

---

## Notes & limitations

- **Transport**: Only interactive shell via `ShellStream` is supported out of the box (no SFTP / exec channels here).
- **Encoding**: Defaults to UTF-8; you can override by passing your own `Encoding` to the constructor.
- **Remote terminal type**: The shell stream is created with name `"xterm"` because many systems expect this for full interactivity.
- **Resize**: the local buffer resizes with the control, but the remote side is not yet informed of size changes.
