## VirtualTerminal

**VirtualTerminal** is a small WPF control and infrastructure for hosting an ANSI / VT-compatible terminal inside your .NET applications.  
It renders a Windows console screen buffer into a WPF UI, and lets you plug in different backends (local command line via ConPTY, SSH, custom processes, etc.) through a simple session abstraction.

This repository contains three main projects:

- **VirtualTerminal** – core WPF control, buffer interop and session abstractions.
- **VirtualTerminal.CommandLine** – addon that hosts a local command line (ConPTY).
- **VirtualTerminal.SecureShell** – addon that connects to a remote host over SSH (SSH.NET).

---

## Getting started

### Install / reference projects

1. Add the `VirtualTerminal` project to your solution (or reference the compiled `VirtualTerminal.dll`).
2. (Optional) Add `VirtualTerminal.CommandLine` and/or `VirtualTerminal.SecureShell` if you want those backends and reference them from your app.
3. Target `net10.0-windows` and enable WPF (as in the sample `VirtualTerminal.TestApp`).

### Basic XAML setup

In your WPF project, declare the namespace and drop the control onto a window:

```xml
<Window x:Class="MyApp.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vt="clr-namespace:VirtualTerminal;assembly=VirtualTerminal">

    <Grid>
        <vt:VirtualTerminalView x:Name="Terminal"
                                AllowDirectInput="True"
                                ScrollDownVisible="True"
                                ScreenBackground="Black"
                                ScreenForeground="White" />
    </Grid>
</Window>
```

### Basic code-behind: attach a session

Create a session instance and assign it to the control’s `Session` property.  
For example, using the local command line backend from `VirtualTerminal.CommandLine`:

```csharp
using System.Windows;
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

Alternatively, for testing UI without a real backend, you can use `DummyTerminalSession`:

```csharp
Terminal.Session = new DummyTerminalSession();
Terminal.Session.AppendLine("Hello from DummyTerminalSession");
```

> **Important**: `VirtualTerminalView` does **not** own / dispose the session. You are responsible for disposing your `TerminalSession` when your window/control is closed.

---

## Core concepts and public API (VirtualTerminal project)

### `VirtualTerminalView` – WPF terminal control

Located in `VirtualTerminalView.xaml` / `VirtualTerminalView.xaml.cs`.

**Purpose**

- Displays the content of a `VirtualTerminalBuffer` in a WPF UI using `VirtualTerminalScreen`.
- Bridges user input (keyboard) to an attached `TerminalSession`.

**Key properties**

- **`TerminalSession? Session`** (`DependencyProperty`)
  - The active session. When changed, `VirtualTerminalView` subscribes to `Session.BufferUpdated` to repaint the terminal.
- **`bool AllowDirectInput`** (`DependencyProperty`, default `true`)
  - If `true`, keyboard input is sent directly to `Session` using VT key sequences.
- **`bool CursorBlinking`** (`DependencyProperty`, default `true`)
  - Reserved for cursor blinking logic (currently not fully wired).
- **`bool ScrollDownVisible`** (`DependencyProperty`, default `true`)
  - Controls visibility of the floating “scroll to bottom” button.
- **`Color ScreenBackground`** (`DependencyProperty`, default `Colors.Black`)
  - Logical background color of the terminal. Validated via `ColorHelper.IsValidConsoleColor`.
- **`Color ScreenForeground`** (`DependencyProperty`, default `Colors.White`)
  - Logical foreground color (text).
- **`string OutputText`** (`DependencyProperty`, read-only API)
  - Reserved for exposing buffer content as text (not heavily used internally).
- **`bool AutoScrolling`** (`DependencyProperty`, default `true`)
  - Indicates whether scroll is pinned to bottom (auto-scroll on new data).
- **`static Encoding Encoding`**
  - Exposes `VirtualTerminalBuffer.Encoding` – the encoding used for the screen buffer (UTF‑16 / Unicode).

**Key behavior**

- On `Session.BufferUpdated` it:
  - Reads `CONSOLE_SCREEN_BUFFER_INFO` and `CHAR_INFO[]` via `Session.Buffer`.
  - Calls `PART_Output.UpdateBuffer(info, buffer)` to re-render.
- Handles `PreviewKeyDown` when `AllowDirectInput == true`:
  - Converts `KeyEventArgs` to VT sequences via `KeyHelper.Convert`.
  - Calls `Session.Append(...)` to send the input into the session.
- Mouse wheel with `Ctrl` pressed zooms the terminal font (changes `PART_Output.FontSize`).
- A floating “scroll to bottom” button appears when not scrolled to the bottom.

### `VirtualTerminalScreen` – low-level rendering element

Located in `VirtualTerminalScreen.cs`.

**Purpose**

- A `FrameworkElement` that renders `CHAR_INFO[]` rows as text using WPF `DrawingVisual`s.
- Manages its own collection of visuals per row for efficient re-rendering.

**Key API**

- **`void UpdateBuffer(CONSOLE_SCREEN_BUFFER_INFO newInfo, CHAR_INFO[] buffer)`**
  - Updates the internal state and schedules drawing of the new buffer.
- **`Size GetCellSize()`**
  - Measures the size of one text cell (based on current font).

**Dependency properties**

- **`FontFamily FontFamily`** – default `"Consolas"`.
- **`double FontSize`** – default `14.0`.
- **`Color Foreground`** – default `LightGray`.
- **`Color Background`** – default `Black`.

These affect how characters are rendered; `ColorHelper` is used to translate console attributes into WPF colors.

### `PromptBox` – command input control (optional)

Located in `PromptBox.xaml` / `PromptBox.xaml.cs`.

**Purpose**

- A lightweight command-entry UI: shows a prompt label (e.g. `"> "`), a text box, and a submit button.
- On **Enter** (or clicking the submit button) it:
  - Raises the routed event **`CommandSubmitted`** with the submitted text.
  - If `Session` is set, writes the command into the terminal via `Session.AppendLine(command)`.

**Key properties**

- **`TerminalSession? Session`**
  - Optional target session. If set, submitted commands are appended to the terminal.
- **`string Prompt`** (default `"> "`)
  - Prompt text displayed before the input box.
- **`string InputText`**
  - Two-way bound text of the input `TextBox`.
- **`bool IsInputEnabled`** (default `true`)
  - Enables/disables the input `TextBox`.

**Events**

- **`CommandSubmitted`** (routed/bubbling)
  - Fired when the user submits a command (Enter / button click).
- **`KeyPressed`** (routed/bubbling)
  - Declared for key-level input scenarios. (Note: current implementation focuses on Enter + VT200 special keys forwarding.)

**Notes**

- `PromptBox` ignores modifier keys and Backspace in its `PreviewKeyDown` handler.
- VT200 special keys (arrows, function keys, etc.) are detected via `KeyHelper.GetVT200Code` and forwarded to `Session.Append(...)` when `Session` is set.

### `ITerminalSession` – session abstraction

Located in `Session/ITerminalSession.cs`.

**Purpose**

- Minimal interface for anything that can supply data to a `VirtualTerminalBuffer`.
- The UI (`VirtualTerminalView`) is written against `TerminalSession` but you can build other UI against `ITerminalSession` directly.

**Members**

- **`event EventHandler? BufferUpdated`**
  - Must be raised whenever the underlying buffer content changes and the UI should re-render.
- **`VirtualTerminalBuffer Buffer { get; }`**
  - Screen buffer containing the current console image.
- **`Encoding InputEncoding { get; }`**
  - Encoding expected by `WriteInput`.
- **`string Title { get; }`**
  - Logical title of the session (can be used for window/tab captions).
- **`void Resize(int columns, int rows)`**
  - Called when the UI is resized.
- **`void WriteInput(ReadOnlySpan<byte> data)`**
  - Writes input bytes (usually VT sequences) to the backend.

### `TerminalSession` – base class for sessions

Located in `Session/TerminalSession.cs`.

**Purpose**

- Base class implementing `ITerminalSession` and common logic:
  - Manages `VirtualTerminalBuffer`.
  - Implements default `Resize` and `WriteInput` that write directly into the buffer.
  - Implements `IDisposable` pattern.

**Key members**

- **`VirtualTerminalBuffer Buffer { get; }`** – created in constructor.
- **`virtual Encoding InputEncoding { get; set; }`** – defaults to `VirtualTerminalBuffer.Encoding`.
- **`virtual string Title { get; }`** – uses current process name and PID.
- **`virtual void Resize(int columns, int rows)`**
  - Calls `Buffer.ResizeBuffer(columns, rows)`.
- **`virtual void WriteInput(ReadOnlySpan<byte> data)`**
  - Writes directly to `Buffer.Write(data)`. Derived sessions often override this to send data to a process or network.
- **`protected void NotifyBufferUpdated()`**
  - Raises `BufferUpdated` event; must be called when new output is written to `Buffer`.
- **`void Dispose()` / `protected abstract void Dispose(bool disposing)`**
  - Derived classes must override `Dispose(bool)` to free their own resources.

### `DummyTerminalSession`

Located in `Session/DummyTerminalSession.cs`.

- Minimal concrete implementation of `TerminalSession` that does nothing special.
- `Title` is `"Dummy :P"`.
- `Dispose(bool)` is empty (no extra resources).
- Useful for design-time / testing scenarios.

### `TerminalSessionExtensions` – helpers and small features

Located in `Session/TerminalSessionExtensions.cs`.

**Key extension methods on `ITerminalSession`:**

- **`void Append(string text)`**
  - Encodes `text` using `session.InputEncoding` and calls `WriteInput`.
- **`void AppendLine()` / `void AppendLine(string text)`**
  - Appends a platform VT newline (`Key.Enter` mapping) optionally after writing text.
- **`void Clear()`**
  - Sends VT sequence `ESC[2J ESC[H` to clear the screen and move cursor home.
- **`TextWriter CreateBufferWriter()`**
  - Returns a `TextWriter` that writes into `session.Buffer` (`BufferStreamWriter`).
- **`void RedirectConsole()`**
  - Sets `Console.Out` to a `TextWriter` that writes into the session’s buffer.

> **RedirectConsole feature**  
> Call `session.RedirectConsole();` once, and then any `Console.Write*` in your process will be routed into the terminal buffer (and thus into the UI).

### `VirtualTerminalBuffer` – console buffer wrapper

Located in `Interop/VirtualTerminalBuffer.cs`.

**Purpose**

- Wraps a Windows console screen buffer handle, with VT processing enabled.
- Provides read/write, resize and info APIs used by sessions and the UI.

**Key members**

- **`IntPtr InputHandle` / `IntPtr OutputHandle`**
  - Raw handles to console input/output.
- **`int Rows` / `int Cols`**
  - Current logical size of the buffer.
- **`bool IsDisposed`**
  - Indicates if the buffer has been disposed.
- **`static Encoding Encoding`**
  - Encoding used by the console buffer (`Unicode`).
- **`void Write(ReadOnlySpan<byte> data)`**
  - Writes bytes to the console via `WriteConsole`.
- **`CHAR_INFO[] ReadBuffer(CONSOLE_SCREEN_BUFFER_INFO info)`**
  - Reads the visible region into a `CHAR_INFO[]` array.
- **`CONSOLE_SCREEN_BUFFER_INFO GetBufferInfo()`**
  - Returns current buffer info from the OS.
- **`void ResizeBuffer(int cols, int rows)`**
  - Changes console screen buffer size.

Internally, `VirtualTerminalBuffer` uses `ConsoleHelper` to allocate a hidden console and enable Unicode + VT processing.

### `VirtualTerminalBufferExtensions`

Located in `Interop/VirtualTerminalBufferExtensions.cs`.

**Key extension methods**

- **`EnableInputFlags(this VirtualTerminalBuffer buffer, ConsoleInputFlags flags)` / `DisableInputFlags(...)`**
  - Enables/disables specific input flags on the console input handle.
- **`EnableOutputFlags(this VirtualTerminalBuffer buffer, ConsoleOutputFlags flags)` / `DisableOutputFlags(...)`**
  - Enables/disables output flags on the output handle.
- **`void BindActive(this VirtualTerminalBuffer buffer)`**
  - Calls `SetConsoleActiveScreenBuffer` to make this buffer the active console buffer.
- **`bool IsCursorVisible(this VirtualTerminalBuffer buffer)`**
  - Returns current cursor visibility by querying `GetConsoleCursorInfo`.

These are mostly for advanced scenarios where you need more control over the underlying console.

### `ConsoleHelper`

Located in `Helpers/ConsoleHelper.cs`.

- **`void Allocate()`**
  - Allocates a console if one doesn’t exist, and hides its window.
- **`void SetEncoding()`**
  - Sets console input and output code pages to UTF‑8.

### `KeyHelper`

Located in `Helpers/KeyHelper.cs`.

**Purpose**

- Converts WPF `Key` / `KeyEventArgs` into VT200-compatible key sequences or printable characters.

**Key members**

- **`string? Convert(KeyEventArgs e)`** / **`string? Convert(Key key)`**
  - Returns VT escape sequences for arrows, function keys, etc., or printable text via `ToUnicode`.
- **`string? GetVT200Code(Key key)`**
  - Maps special keys to CSI / ESC sequences.
- **`string? GetCharFromKey(Key key)`**
  - Uses Windows APIs to get the Unicode character represented by a key.
- **`bool IsModifier(Key key)`**
  - Returns `true` for Shift/Ctrl/Alt keys.

### `ColorHelper`

Located in `Helpers/ColorHelper.cs`.

**Key members**

- **`Color ConvertToColor(ConsoleCharacterAttributes attributes, bool isBackground)`**
  - Converts console attribute flags into WPF `Color`.
- **`bool IsValidConsoleColor(Color color)`**
  - Returns whether a color is in the standard console palette (0,128,255 per channel).
- **`ConsoleCharacterAttributes ConvertToAttributes(Color color, bool isBackground)`**
  - Inverse of `ConvertToColor`.

---

## Creating your own `TerminalSession`

You can implement custom backends (e.g., your own process, REPL, game, container shell) by inheriting from `TerminalSession` (recommended) or implementing `ITerminalSession` from scratch.

### Recommended: inherit from `TerminalSession`

1. **Subclass `TerminalSession`:**

```csharp
using System.Text;
using VirtualTerminal.Session;
using VirtualTerminal.Interop;

public sealed class MyCustomSession : TerminalSession
{
    private readonly SomeClient _client;

    public override string Title => "My Custom Backend";

    public MyCustomSession(SomeClient client, Encoding? encoding = null)
        : base(encoding ?? Encoding.UTF8)
    {
        _client = client;

        // Subscribe to your backend’s output
        _client.OutputReceived += OnOutputReceived;
    }

    private void OnOutputReceived(byte[] data)
    {
        // Convert from your backend encoding into buffer encoding
        byte[] conv = Encoding.Convert(InputEncoding, VirtualTerminalBuffer.Encoding, data);
        Buffer.Write(conv);
        NotifyBufferUpdated();
    }

    public override void WriteInput(ReadOnlySpan<byte> data)
    {
        // Forward user input into backend
        _client.Send(data.ToArray());
    }

    public override void Resize(int columns, int rows)
    {
        base.Resize(columns, rows);
        _client.Resize(columns, rows);
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        _client.Dispose();
    }
}
```

2. **Attach your session to the control:**

```csharp
var client = new SomeClient(...);
var session = new MyCustomSession(client);
Terminal.Session = session;
```

3. **Wire output events and call `NotifyBufferUpdated()`**

- When your backend receives data, convert it into `VirtualTerminalBuffer.Encoding` and call:
  - `Buffer.Write(...)`
  - `NotifyBufferUpdated();`

This is what both `CommandLineSession` and `SecureShellSession` do internally.

### Implementing `ITerminalSession` manually

If you need maximum control, you can implement `ITerminalSession` directly:

- Maintain your own `VirtualTerminalBuffer` instance.
- Implement the `BufferUpdated` event.
- Implement `Resize`/`WriteInput` to talk to your backend.
- Use `VirtualTerminalBufferExtensions` for advanced console features if you choose to use a Windows console internally.

---

## Small features & UX details

- **RedirectConsole** (`ITerminalSession.RedirectConsole()`):
  - Redirects `Console.Out` to the terminal buffer via `BufferStreamWriter`.
- **Screen clear** (`ITerminalSession.Clear()`):
  - Sends ANSI sequence to clear the screen.
- **Font zoom**:
  - `Ctrl + Mouse Wheel` changes `VirtualTerminalScreen.FontSize`.
- **Scroll to bottom button**:
  - A circular button appears when you are scrolled up; clicking it scrolls to the latest output.
- **Auto-scrolling**:
  - When `AutoScrolling` is `true` and new output arrives, the view stays pinned to the bottom.

---

## Addons

The following projects live in this repository and extend the core `VirtualTerminal` functionality:

- **VirtualTerminal.CommandLine** – exposes `CommandLineSession` (ConPTY-backed local shell).
- **VirtualTerminal.SecureShell** – exposes `SecureShellSession` (SSH.NET-backed remote shell).

Each addon has its own README with setup and usage instructions:

- `VirtualTerminal.CommandLine/README.md`
- `VirtualTerminal.SecureShell/README.md`

