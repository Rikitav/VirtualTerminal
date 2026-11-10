# Phase 6: Web & TUI Frontends, Ecosystem Expansion & Packaging

## 1. Executive Summary
Phase 6 expands `VirtualTerminal`'s reach across the entire .NET ecosystem. By modularizing the codebase into lightweight packages and building new frontends for Blazor (WebAssembly/Server HTML5 Canvas/WebGL) and TUI frameworks (Spectre.Console / Terminal.Gui), `VirtualTerminal` becomes the universal, cross-platform terminal engine for desktop, web, cloud, and embedded applications.

---

## 2. Key Objectives & Deliverables

### 2.1 Package Modularization & Architecture Splitting
Restructure the repository into clear, focused NuGet packages:
- **`VirtualTerminal.Core`**:
  - Target: `net10.0`, `netstandard2.0`, `netstandard2.1`.
  - Zero UI dependencies, Native AOT compatible, trimmable.
  - Contains: VT decoder, state machine, paged circular screen buffer, color models, multiplexer, and encoders.
- **`VirtualTerminal.Transport.Pty`**:
  - Windows ConPTY + Unix PTY (macOS/Linux) process hosting.
- **`VirtualTerminal.Transport.Ssh`**:
  - SSH.NET remote session client.
- **`VirtualTerminal.WPF`**:
  - WPF control library targeting Windows.
- **`VirtualTerminal.Avalonia`**:
  - Cross-platform desktop control (Windows, macOS, Linux).
- **`VirtualTerminal.Blazor` *(New)***:
  - WebAssembly & ASP.NET Core Blazor component using HTML5 Canvas or WebGL.
- **`VirtualTerminal.Spectre` / `VirtualTerminal.TerminalGui` *(New)***:
  - Host a virtual terminal sub-window directly inside console TUIs.

### 2.2 Blazor Web Terminal Control (`VirtualTerminal.Blazor`)
- **Architecture**:
  - Implement `<VirtualTerminalComponent />` in Razor.
  - Efficient JS-interop via `IJSInProcessRuntime` or WebAssembly unmanaged memory sharing.
  - Render terminal grid directly onto an HTML5 `<canvas>` using 2D Canvas context or WebGL shaders.
  - Support full keyboard input, mouse reporting, clipboard copy/paste, and window resizing via `ResizeObserver`.
  - Perfect for web-based SSH clients, browser IDEs, cloud shells, and embedded microservices.

### 2.3 Headless WebSocket / SSH Daemon Mode
- **Feature**:
  - Build a standalone ASP.NET Core / Minimal API middleware that exposes `VirtualTerminal` over WebSockets (compatible with `xterm.js` clients or native apps).
  - Serve interactive terminal sessions over HTTP/HTTPS with authentication and session resumption.

### 2.4 Documentation, Interactive Samples & CI Badges
- **Reference Documentation**:
  - Comprehensive API documentation generated with DocFX.
  - Sequence support matrix (detailing VT100, VT220, VT320, VT420, VT520, and xterm extensions).
- **Sample Gallery**:
  - Avalonia multi-tab split-pane demo.
  - Blazor WebAssembly offline demo.
  - Console-in-console nested session demo using Spectre.Console.
- **Continuous Conformance Reporting**:
  - Automated CI matrix running unit tests and the `esctest2` ported test suite.
  - Publish conformance score badge (e.g. `99.4% VT520 Compliance`).

---

## 3. Implementation Steps

1. **Refactor Solution Structure**:
   - Split core types from UI dependencies.
   - Configure multi-targeting in `.csproj` files (`net10.0`, `netstandard2.1`).
2. **Create `VirtualTerminal.Blazor` Project**:
   - Implement Canvas drawing engine in TypeScript / WebAssembly.
   - Wire input events and bidirectional stream pump.
3. **Build TUI Embedding Control**:
   - Implement custom view in `Terminal.Gui` and widget in `Spectre.Console`.
4. **Publish Documentation & NuGet Pipeline**:
   - Setup GitHub Actions release workflow with signed NuGet packages and symbols.

---

## 4. Acceptance Criteria & Metrics
- `VirtualTerminal.Core` has **zero external dependencies** and builds cleanly for AOT.
- `VirtualTerminal.Blazor` renders 60 FPS in Chrome, Firefox, and Safari on desktop and mobile.
- Clean NuGet package dependency graph with no circular references.
- 100% API documentation coverage on public methods with code examples.
