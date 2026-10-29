## VirtualTerminal.CommandLine

**VirtualTerminal.CommandLine** is an addon for the core `VirtualTerminal` library that provides a `CommandLineSession` implementation based on Windows ConPTY.  
It lets you host a real local command line (e.g. `cmd.exe`, PowerShell, custom CLI apps) inside the WPF or Avalonia `TerminalControl`.

---

## Getting started

### Reference the project

1. Add a project reference from your app to:
   - `VirtualTerminal.CommandLine`
   - `VirtualTerminal` (core – already referenced by the addon).
2. Ensure your app targets `net10.0-windows` (ConPTY is Windows-only).

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
using VirtualTerminal;

public partial class MainWindow : Window
{
    private CommandLineSession? _session;

    public MainWindow()
    {
        InitializeComponent();

        _session = new CommandLineSession(); // defaults to cmd.exe
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

Concrete `TerminalSession` implementation backed by Windows ConPTY:

- Creates a pseudoconsole and spawns a child process attached to it.
- Reads child output in a background loop and feeds it to the local `TerminalDecoder`.
- Forwards user input into the child process.

### Constructors

- **`CommandLineSession()`**
  - Starts the default Windows shell: `C:\Windows\System32\cmd.exe`.
- **`CommandLineSession(string application)`**
  - Starts `application` as the child process.
  - Uses `Encoding.UTF8` as `InputEncoding`.
- **`CommandLineSession(ProcessCreationInfo processInfo)`**
  - Full control over command line, working directory, etc.

### Properties

- **`override string Title`**
  - Derived from the started executable name.
- **`PseudoConsole PseudoConsole`**
  - Exposes the underlying `PseudoConsole` instance (process + pipes).

### Input and output pipeline

Internally, `CommandLineSession`:

- Starts a long-running background task `ReadOutputLoop()`:
  - Reads from `PseudoConsole.Reader`.
  - Converts from `InputEncoding` to the decoder encoding.
  - Writes into `Decoder` and calls `NotifyBufferUpdated()` to trigger UI re-rendering.
- Overrides **`Write(ReadOnlySpan<byte> data)`**:
  - Writes input bytes into `PseudoConsole.Writer`.

### Resize behavior

`Resize(ushort columns, ushort rows)`:

1. Resizes the local `TerminalScreenBuffer` geometry.
2. Tells ConPTY about the new size via `PseudoConsole.Resize(columns, rows)`.

ConPTY owns the actual reflow; the local buffer is resized to match the new dimensions and the control lets ConPTY repaint the visible area.

### Disposal

- **`protected override void Dispose(bool disposing)`**
  - Cancels and disposes the read loop.
  - Disposes `PseudoConsole`.

Always dispose `CommandLineSession` when you are done:

```csharp
_session?.Dispose();
```

---

## `PseudoConsole` and interop

`VirtualTerminal.CommandLine.Interop` contains the plumbing that connects the Windows pseudoconsole to the engine:

### `PseudoConsoleFactory`

- **`static PseudoConsole Start(TerminalScreenBuffer buffer, ProcessCreationInfo processInfo)`**
  - Creates anonymous pipes for stdin/stdout.
  - Calls `CreatePseudoConsole` with buffer dimensions.
  - Starts a child process via `Win32ProcessFactory.Start`.
  - Returns a `PseudoConsole` wrapping the ConPTY handle, child process, stdin writer and stdout reader.

You usually don’t need to use `PseudoConsoleFactory` directly unless you are building a custom session around it.

---

## Example: starting a custom CLI app

```csharp
// PowerShell
var psSession = new CommandLineSession(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe");
Terminal.Session = psSession;

// Python REPL
var replSession = new CommandLineSession(@"C:\python314\python.exe");
Terminal.Session = replSession;

// Any console tool
var toolSession = new CommandLineSession(@"C:\path\to\mytool.exe");
Terminal.Session = toolSession;
```

All VT-compatible output from the tool will be rendered in the `TerminalControl`.

---

## Tips & limitations

- **Windows-only**: ConPTY is available on modern Windows 10/11; the project targets `net10.0-windows`.
- **Encoding**: `CommandLineSession` uses UTF-8 by default.
- **Resizing**: the control debounces rapid resize events and suppresses intermediate renders so ConPTY can produce a clean reflow frame. A minimum grid size of 10×3 is enforced to prevent shells from resetting at extremely small window sizes.
