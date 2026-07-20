## VirtualTerminal

**VirtualTerminal** is a small, UI-agnostic VT/ANSI terminal engine for .NET with WPF and Avalonia controls.  
It parses terminal escape sequences into a cross-platform screen buffer and lets you plug in different backends (local ConPTY shell, SSH, or custom sessions) through a single `ITerminalSession` abstraction.

This repository contains:

- **VirtualTerminal** – the core engine: `TerminalDecoder`, `TerminalScreenBuffer`, `TerminalState`, options and session abstractions.
- **VirtualTerminal.WPF** – `TerminalControl` for WPF (`System.Windows.Controls.Control`).
- **VirtualTerminal.Avalonia** – `TerminalControl` for Avalonia (`Avalonia.Controls.TemplatedControl`).
- **VirtualTerminal.CommandLine** – `CommandLineSession`, a ConPTY-backed local shell. (Windows only)
- **VirtualTerminal.SecureShell** – `SecureShellSession`, an SSH.NET-backed remote shell.
- **VirtualTerminal.TestApp** – a small WPF demo application

---

## Getting started

### Install / reference projects

1. Add the `VirtualTerminal` project to your solution (or reference the compiled assembly).
2. Add `VirtualTerminal.WPF` or `VirtualTerminal.Avalonia` depending on your UI stack.
3. (Optional) Add `VirtualTerminal.CommandLine` and/or `VirtualTerminal.SecureShell` for those backends.
4. Target `net10.0` (Avalonia / engine) or `net10.0-windows` (WPF backends and test apps).

### Basic XAML setup

#### WPF

```xml
<Window x:Class="MyApp.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vt="clr-namespace:VirtualTerminal;assembly=VirtualTerminal.WPF">

    <Grid>
        <vt:TerminalControl
            x:Name="Terminal"
            AllowDirectInput="True"
            CursorBlinking="True"
            ScrollDownVisible="True"
            Background="Black"
            FontFamily="Cascadia Mono"
            FontSize="14" />
    </Grid>
</Window>
```

#### Avalonia

```xml
<Window x:Class="MyApp.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vt="clr-namespace:VirtualTerminal;assembly=VirtualTerminal.Avalonia">

    <vt:TerminalControl 
        CurrentSession="{Binding Session}"
        AllowDirectInput="True"
        CursorBlinking="True"
        Background="Black"
        FontFamily="Cascadia Mono"
        TerminalFontSize="14" />
</Window>
```

### Basic code-behind: attach a session

Create a session instance and assign it to the control:

```csharp
using VirtualTerminal;

public partial class MainWindow : Window
{
    private CommandLineSession? _session;

    public MainWindow()
    {
        InitializeComponent();

        _session = new CommandLineSession(); // defaults to cmd.exe
        Terminal.Session = _session;         // WPF
        // Terminal.CurrentSession = _session; // Avalonia
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _session?.Dispose();
    }
}
```

> **Important**: the control does **not** own or dispose the session. Dispose the session yourself when the window/control is closed.

---

## Core concepts and public API

### `TerminalControl`

The WPF and Avalonia controls render the active `ITerminalSession.Buffer` and forward keyboard input into the session.

**Key properties**

- **`Session`** / **`CurrentSession`** (`DependencyProperty` / `StyledProperty`)
  - The active `ITerminalSession`.
- **`AllowDirectInput`** (default `true`)
  - If `true`, keyboard input is encoded as VT sequences and sent to the session.
- **`CursorBlinking`**, **`CursorVisible`**, **`CursorColor`**, **`CursorShape`**
  - Cursor appearance.
- **`ScrollDownVisible`** (default `true`)
  - Shows a floating indicator when scrolled back in history.
- **`Background`** / **`Foreground`**
  - Default terminal colors.
- **`FontFamily`** / **`FontSize`** (WPF) or **`TerminalFontFamily`** / **`TerminalFontSize`** (Avalonia)
  - Rendering font.
- **`LineHeight`**
  - Line height multiplier.
- **`ScrollbackLines`**
  - Maximum number of scrollback lines to retain.
- **`AutoScrolling`** (read-only)
  - `true` when the viewport is pinned to the live bottom.

**Key behavior**

- Renders the active screen buffer using glyph-based text rendering.
- Handles `PreviewKeyDown` / `PreviewTextInput` when `AllowDirectInput == true`.
- Mouse wheel scrolls through scrollback.
- `Ctrl + Mouse Wheel` zooms the terminal font.

### `ITerminalSession`

The minimal interface any backend must implement.

**Members**

- **`event EventHandler? BufferUpdated`** – raise when the buffer changes and the UI should repaint.
- **`TerminalScreenBuffer Buffer { get; }`** – the screen buffer.
- **`ITerminalDecoder Decoder { get; }`** – the VT decoder that writes into `Buffer`.
- **`Encoding InputEncoding { get; }`** / **`Encoding OutputEncoding { get; }`**.
- **`string Title { get; }`** – logical title of the session.
- **`void Resize(ushort columns, ushort rows)`** – called when the control is resized.
- **`void Write(ReadOnlySpan<byte> data)`** – writes input bytes to the backend.
- **`int Read(Span<byte> buffer)`** / **`byte[] ReadAll()`** – reads response data from the backend (e.g. replies to DA/DSR queries).

### `TerminalSession`

Recommended abstract base class in `VirtualTerminal.Session`.

It owns a `TerminalDecoder` and a `TerminalScreenBuffer`, wires `BufferUpdated`, and provides:

- **`void Resize(ushort columns, ushort rows)`** – resizes the local decoder buffer.
- **`void Write(ReadOnlySpan<byte> data)`** – writes to the local decoder and notifies the UI.
- **`protected void NotifyBufferUpdated()`** – raises `BufferUpdated`.
- **`IDisposable`** pattern.

Derived sessions override `Write` and `Resize` to talk to the actual backend.

### `TerminalDecoder`

Parses incoming VT/ANSI byte streams and applies them to a `TerminalScreenBuffer`.  
Key method: **`void Write(ReadOnlySpan<byte> data)`**.

### `TerminalScreenBuffer`

The engine screen buffer. It owns:

- the primary and alternate screen grids,
- the scrollback ring,
- tab stops,
- the scroll region,
- dirty-row tracking.

Public API includes `Rows`, `Columns`, `GetRow(int)`, `GetScrollback()`, `Resize(...)`, `ScrollbackCount`, `HasDirtyRows`, `MarkRowClean`, `SyncRoot`, etc.

---

## Addons

- **VirtualTerminal.CommandLine** – `CommandLineSession` for local ConPTY shells (`cmd.exe`, PowerShell, etc.).
- **VirtualTerminal.SecureShell** – `SecureShellSession` for SSH.NET remote shells.

Each addon has its own README with setup and usage:

- `VirtualTerminal.CommandLine/README.md`
- `VirtualTerminal.SecureShell/README.md`

---

## Small features & UX details

- **Clear screen**: `TerminalControl.Clear()` sends `ESC[2J ESC[H`.
- **Font zoom**: `Ctrl + Mouse Wheel` changes the render font size.
- **Scroll-to-bottom indicator**: appears when scrolled up; click or press a key to return to live output.
- **Auto-scrolling**: when at the bottom, new output keeps the view pinned.
- **Resize debounce**: both controls debounce rapid window resize events and suppress intermediate renders so backends like ConPTY have time to produce a clean reflow frame.

### Context menu and built-in commands

Both controls expose static commands for common terminal operations. If no `ContextMenu` is assigned, the right mouse button behaves in terminal style: **copy** when text is selected, **paste** otherwise. If you assign a `ContextMenu`, the control lets the framework open it and the commands can be used from XAML.

**WPF**

```xml
<vt:TerminalControl x:Name="Terminal" ...>
    <vt:TerminalControl.ContextMenu>
        <ContextMenu>
            <MenuItem Command="{x:Static vt:TerminalControl.CopyCommand}"
                      CommandTarget="{Binding PlacementTarget, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
            <MenuItem Command="{x:Static vt:TerminalControl.PasteCommand}"
                      CommandTarget="{Binding PlacementTarget, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
            <Separator />
            <MenuItem Command="{x:Static vt:TerminalControl.SelectAllCommand}"
                      CommandTarget="{Binding PlacementTarget, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
            <MenuItem Command="{x:Static vt:TerminalControl.ClearCommand}"
                      CommandTarget="{Binding PlacementTarget, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
        </ContextMenu>
    </vt:TerminalControl.ContextMenu>
</vt:TerminalControl>
```

**Avalonia**

```xml
<vt:TerminalControl x:Name="Terminal" ...>
    <vt:TerminalControl.ContextMenu>
        <ContextMenu>
            <MenuItem Command="{x:Static vt:TerminalControl.CopyCommand}" CommandParameter="{Binding #Terminal}" />
            <MenuItem Command="{x:Static vt:TerminalControl.PasteCommand}" CommandParameter="{Binding #Terminal}" />
            <Separator />
            <MenuItem Command="{x:Static vt:TerminalControl.SelectAllCommand}" CommandParameter="{Binding #Terminal}" />
            <MenuItem Command="{x:Static vt:TerminalControl.ClearCommand}" CommandParameter="{Binding #Terminal}" />
        </ContextMenu>
    </vt:TerminalControl.ContextMenu>
</vt:TerminalControl>
```

| Command | Action |
| --- | --- |
| `CopyCommand` | Copies the current selection to the clipboard. |
| `PasteCommand` | Pastes the clipboard content into the session. |
| `SelectAllCommand` | Selects the entire visible screen. |
| `ClearCommand` | Sends `ESC[2J ESC[H` to clear the screen. |

`Ctrl+C`/`Ctrl+V` and `Ctrl+Shift+C` keep working as before.

---

## Notes & limitations

- **ConPTY resize**: ConPTY handles its own reflow. The engine resizes the local geometry and lets ConPTY repaint/overwrite the visible area. A minimum grid size (10×3) is enforced to avoid shell resets at extremely small sizes.
- **Windows-only backends**: `VirtualTerminal.CommandLine` and `VirtualTerminal.SecureShell` target `net10.0-windows`. The core engine and Avalonia control target `net10.0`.
