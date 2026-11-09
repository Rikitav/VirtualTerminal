# Phase 3: Session Engine, Multiplexing & Transport Modernization

## 1. Executive Summary
Phase 3 elevates `VirtualTerminal` from a single-session engine to a robust terminal multiplexer and multi-transport pipeline framework. It introduces high-throughput stream pipelines via `System.IO.Pipelines`, a native split-pane / tab multiplexing model (similar to `tmux` or Windows Terminal tabs), headless daemon support, and advanced input protocols (Kitty Keyboard Protocol).

---

## 2. Key Objectives & Deliverables

### 2.1 Transport Layer Pipeline Refactoring (`System.IO.Pipelines`)
- **Problem**: `TerminalSession` uses `ConcurrentQueue<byte>` with explicit `lock` synchronization and byte-by-byte enqueueing. High-speed process outputs incur heavy CPU lock contention and allocation of queue node objects.
- **Solution**:
  - Replace queueing with `System.IO.Pipelines.Pipe`.
  - Support high-performance producer-consumer streaming between process pipes (`PseudoConsole`) and the terminal decoder.
  - Implement zero-copy buffer slicing via `ReadOnlySequence<byte>`.
  - Implement automatic backpressure: if the terminal consumer or renderer slows down, the pipe suspends reader ingestion to prevent memory runaway.

### 2.2 Built-in Session Multiplexer & Layout Engine
- **Multiplexer Architecture**:
  - Introduce `TerminalMultiplexer`: coordinates multiple active `ITerminalSession` instances.
  - **Tree-Based Layout Model**:
    - `MultiplexerNode`: Abstract base for split containers and terminal leaves.
    - `SplitPaneNode`: Horizontal or vertical split container with configurable split ratio (`0.0 .. 1.0`).
    - `TerminalLeafNode`: Hosts an individual `ITerminalSession`.
  - **Tab Management**:
    - Each tab owns an independent `MultiplexerNode` root.
    - Synchronized active session focus switching and keyboard input routing.
  - **Pane Operations**:
    - `SplitHorizontal(session, ratio = 0.5)`
    - `SplitVertical(session, ratio = 0.5)`
    - `ClosePane(paneId)`
    - `FocusNextPane()`, `FocusPreviousPane()`, `FocusDirection(Direction)`
    - `SwapPanes(paneA, paneB)`

### 2.3 Headless Daemon, Detach / Reattach & State Persistence
- **Detached Execution**:
  - Allow terminal processes and sessions to continue running in background daemon mode when UI controls are closed or detached.
  - Reattaching reconstructs the screen buffer and streams history seamlessly.
- **State Serialization**:
  - Binary and JSON snapshot serializer for screen buffers, active modes, scrollback history, and cursor position.
  - Instant session restoration and session sharing.

### 2.4 Modern Input Protocol: Kitty Keyboard Protocol
- **Feature**:
  - Implement progressive keyboard enhancement protocol (supported by Ghostty, Kitty, WezTerm, Neovim).
  - Sequence: `CSI > flags u` (Push keyboard flags), `CSI < count u` (Pop keyboard flags).
  - Distinct key-press, key-release, and repeat events.
  - Disambiguation of Escape vs Alt, Shift+Enter, Ctrl+I vs Tab, Ctrl+M vs Enter.
  - Enables rich modern CLI/TUI applications (e.g. Helix, Neovim, Micro) to operate with modern keyboard events.

---

## 3. Implementation Steps

1. **Implement `PipeTerminalTransport`**:
   - Create `ITerminalTransport` interface using `PipeReader` / `PipeWriter`.
   - Adapt `CommandLineSession` and `UnixPtySession` to use pipeline transports.
2. **Design `TerminalMultiplexer`**:
   - Create models: `TerminalTab`, `SplitPaneNode`, `TerminalLeafNode`.
   - Add focus management, resizing cascades, and unified notification dispatch.
3. **Add Kitty Keyboard Encoder**:
   - Extend `KeyboardEncoder` to support CSI `u` format with modifier bitmasks.
   - Add capability negotiation (`CSI ? u` query).
4. **Implement Session Snapshot Serializer**:
   - Add `SaveState(Stream stream)` and `RestoreState(Stream stream)`.

---

## 4. Acceptance Criteria & Metrics
- Zero lock contention in high-throughput pipe reads (`Pipelines` throughput > 500 MB/s).
- Seamless creation of nested 2x2 and 3x3 split-pane configurations with correct resize propagation.
- Passing Kitty Keyboard protocol tests under `TerminalDecoderTests`.
- Successfully detach and reattach an active interactive session without dropping output bytes.
