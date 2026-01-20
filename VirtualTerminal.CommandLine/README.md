## VirtualTerminal.CommandLine

**VirtualTerminal.CommandLine** is an addon for the core `VirtualTerminal` library that provides a `CommandLineSession` implementation based on Windows ConPTY.  
It lets you host a real local command line (e.g. `cmd.exe`, PowerShell, custom CLI apps) inside the `VirtualTerminalView` WPF control.

---

## Getting started

### Reference the project

1. Add a project reference from your WPF app to:
   - `VirtualTerminal.CommandLine`
   - `VirtualTerminal` (core – already referenced by the addon).
2. Ensure your app targets `net10.0-windows` with WPF enabled.

### Basic usage with `VirtualTerminalView`

In XAML (same as core README):

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

In code-behind:

```csharp
using System;
using System.Windows;
using VirtualTerminal;

public partial class MainWindow : Window
{
    private CommandLineSession? _session;

    public MainWindow()
    {
        InitializeComponent();

        // Start default cmd.exe
        _session = new CommandLineSession();
        Terminal.Session = _session;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _session?.Dispose();
    }
}
```

You now have an interactive command prompt embedded directly in your window.

---

## `CommandLineSession` API

Located in `CommandLineSession.cs` (namespace `VirtualTerminal`).

### Purpose

- Concrete `TerminalSession` implementation backed by Windows ConPTY:
  - Connects a `VirtualTerminalBuffer` to a pseudoconsole.
  - Spawns a child process (e.g. `cmd.exe`) attached to that pseudoconsole.
  - Forwards user input into the process, and process output into the buffer.

### Constructors

- **`CommandLineSession(string application)`**
  - Starts `application` as the child process.
  - Uses `Encoding.UTF8` as `InputEncoding`.
  - Internally calls `PseudoConsoleFactory.Start(Buffer, new ProcessCreationInfo { CommandLine = application })`.
- **`CommandLineSession()`**
  - Convenience overload that starts the default Windows shell:
    - `C:\Windows\System32\cmd.exe`

### Properties

- **`override string Title`**
  - Returns `"ConPTY"`.
- **`PseudoConsole PseudoConsole`**
  - Exposes the underlying `PseudoConsole` instance (process + pipes).

### Input and output pipeline

Internally, `CommandLineSession`:

- Starts a long-running background task `ReadOutputLoop()`:
  - Reads from `PseudoConsole.Reader`.
  - Converts from `InputEncoding` to `VirtualTerminalBuffer.Encoding`.
  - Writes into `Buffer` and calls `NotifyBufferUpdated()` to trigger UI re-rendering.
- Overrides **`WriteInput(ReadOnlySpan<byte> data)`**:
  - Writes input bytes into `PseudoConsole.Writer`.
  - Calls `NotifyBufferUpdated()` to ensure UI flush if needed.

### Disposal

- **`protected override void Dispose(bool disposing)`**
  - Cancels and disposes the read loop token.
  - Disposes `PseudoConsole`.

Always dispose `CommandLineSession` when you are done:

```csharp
_session?.Dispose();
```

---

## PseudoConsole and interop

`VirtualTerminal.CommandLine.Interop` contains the plumbing that connects the Windows pseudoconsole to the `VirtualTerminalBuffer`:

### `PseudoConsoleFactory`

Located in `Interop/PseudoConsoleFactory.cs`.

- **`static PseudoConsole Start(VirtualTerminalBuffer buffer, ProcessCreationInfo processInfo)`**
  - Creates anonymous pipes for stdin/stdout.
  - Calls `CreatePseudoConsole` with buffer dimensions.
  - Starts a child process via `Win32ProcessFactory.Start(processInfo, handle)`.
  - Returns a `PseudoConsole` that wraps:
    - ConPTY handle
    - Child process
    - `Stream` writer (stdin)
    - `Stream` reader (stdout)

You usually don’t need to use `PseudoConsoleFactory` directly unless you are building your own custom session around it.

---

## Example: starting a custom CLI app

You can point `CommandLineSession` at any console application:

```csharp
// Run PowerShell
var psSession = new CommandLineSession(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe");
Terminal.Session = psSession;

// Or Python REPL
var replSession = new CommandLineSession(@"C:\python314\python.exe");
Terminal.Session = replSession;

// Or some custom tool
var toolSession = new CommandLineSession(@"C:\path\to\mytool.exe");
Terminal.Session = toolSession;
```

All VT-compatible output from the tool will be rendered in the `VirtualTerminalView`.

---

## Tips & limitations

- **Windows-only**: ConPTY is available on modern Windows 10+; the library targets `net10.0-windows`.
- **Encoding**: `CommandLineSession` uses UTF‑8 for input and converts output into the Unicode buffer encoding.
- **Resizing**: When the WPF control resizes and calls `Resize`, the underlying `VirtualTerminalBuffer` size is updated. The child process will see the new console dimensions via ConPTY.

