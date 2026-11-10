# Phase 3: Session Engine & Transport Modernization

## 1. Executive Summary
Phase 3 modernizes the session, transport, and input layers of `VirtualTerminal`. The core library focuses strictly on transport abstractions (streaming, pipelines), session lifecycle (headless execution, detach/reattach, state serialization), and advanced input encoding (Kitty Keyboard Protocol). Multiplexing (split panes, tab layouts) is deliberately excluded from the core library, remaining a frontend concern.

---

## 2. Key Objectives & Deliverables

### 2.1 Transport Pipeline Modernization (`System.IO.Pipelines` & `ITerminalTransport`)
- **Transport Abstraction**:
  - Introduce `ITerminalTransport` representing bidirectional byte communication (PTY, SSH, WebSocket, Mock/In-Memory):
    - `PipeReader Input { get; }` (bytes coming from transport into terminal decoder)
    - `PipeWriter Output { get; }` (bytes sent from terminal/keyboard to transport)
  - Seamless zero-copy buffering via `System.IO.Pipelines.Pipe` with automatic backpressure: if decoder or renderer is busy, ingestion pauses to prevent unbounded RAM growth.
- **Async Streaming Pump**:
  - `ProcessInputAsync(PipeReader reader, CancellationToken ct)` reading directly into `TerminalDecoder.Write(ReadOnlySpan<byte>)` via `ReadOnlySequence<byte>`.

### 2.2 Modern Input Encoding: Kitty Keyboard Protocol
- **Specification Compliance**:
  - Progressive keyboard enhancement protocol (supported by Ghostty, Kitty, WezTerm, Neovim, Helix).
  - Capability flags query & negotiation (`CSI ? u`, `CSI > flags u`, `CSI < count u`).
  - Progressive enhancement flags:
    - Flag 1 (`1 << 0`): Disambiguate escape codes (e.g. Esc vs Alt, Shift+Enter, Tab vs Ctrl+I, Enter vs Ctrl+M).
    - Flag 2 (`1 << 1`): Report event types (press, repeat, release).
    - Flag 4 (`1 << 2`): Report alternate keys.
    - Flag 8 (`1 << 3`): Report all keys as escape codes.
    - Flag 16 (`1 << 4`): Report associated text.
  - Integration with `KeyboardEncoder` emitting `CSI unicode-key : alternate-key ; modifiers : event-type u`.

### 2.3 Session Lifecycle, Detach / Reattach & State Serialization
- **Headless Execution**:
  - Allow `TerminalSession` to execute without an attached renderer or window.
  - Thread-safe detach and reattach of `TerminalDecoder` / `TerminalScreenBuffer`.
- **State Snapshot Serialization**:
  - Structured binary and JSON state snapshots for saving and restoring session state:
    - Screen buffer dimensions, active screen, cursor position.
    - Grid cell matrix (runes, colors, attributes).
    - Terminal modes (`DECOM`, `DECAWM`, `DECLRMM`, `SynchronizedOutput`, etc.).
    - Scrollback ring buffer history.
  - Fast serialization into and out of `Stream`.

---

## 3. Implementation Steps

1. **Transport Abstraction & Pipelines**:
   - Define `ITerminalTransport` and `PipeTerminalTransport`.
   - Add async pipeline pump to `TerminalSession`.
2. **Kitty Keyboard Protocol**:
   - Add progressive keyboard enhancement mode tracking to `TerminalModes`.
   - Implement Kitty keyboard encoding in `KeyboardEncoder`.
   - Add CSI `u` query response (`CSI ? u`) in `TerminalDecoder`.
3. **Session State Snapshot Serialization**:
   - Implement `TerminalSnapshotSerializer` (`Save(Stream)` / `Load(Stream)`).
   - Add unit tests validating round-trip session state restoration.

---

## 4. Acceptance Criteria & Metrics
- Zero lock contention during stream pipeline reading.
- Full Kitty Keyboard Protocol compliance for modifier combinations and event types.
- 100% round-trip fidelity when saving and restoring session state snapshots.
- All existing tests pass without regressions.
