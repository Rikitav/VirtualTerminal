# Phase 2: Escape Sequence Parity & `esctest2` Conformance Harness

## 1. Executive Summary
Phase 2 establishes full VT520 and modern xterm specification compliance using [ThomasDickey/esctest2](https://github.com/ThomasDickey/esctest2) as the verification benchmark. By porting `esctest2` tests to .NET and addressing missing or incomplete ANSI/DEC sequences, `VirtualTerminal` will attain theoretical conformance with DEC STD 070 video systems standards.

---

## 2. Key Objectives & Deliverables

### 2.1 Adaptation of `esctest2` to .NET Test Suite
- **Architecture**:
  - Implement an in-memory transport harness (`EscTestDriver`) that feeds VT sequences directly to `ITerminalDecoder` and inspects the resulting `TerminalScreenBuffer` state without needing real PTY/OS processes.
  - Wire up `TerminalDecoder.SendOutput` to receive and assert asynchronous terminal responses (e.g. CPR, DA1/DA2, DECRQSS, DECRQCRA).
- **Core Assertion Primitives**:
  - `AssertCursorPosition(int expectedRow, int expectedCol)`
  - `AssertScreenCharsInRectEqual(TerminalRect rect, string[] expectedLines)`
  - `AssertCellAttributes(int row, int col, Action<CellRenderStyle> assertions)`
  - `AssertResponse(string expectedResponse, TimeSpan timeout)`
- **Ported Test Modules from `esctest2`**:
  - `test_cup` (Cursor Position)
  - `test_ed` (Erase in Display - ED 0, 1, 2, 3)
  - `test_el` (Erase in Line - EL 0, 1, 2)
  - `test_decstbm` & `test_decslrm` (Top/Bottom & Left/Right Margins)
  - `test_decom` (Origin Mode relative positioning)
  - `test_decrqcra` (DEC Rectangle Checksum)
  - `test_decrqss` (DEC Request Selection/Setting)
  - `test_sgr` (Character attributes, truecolor, indexed color, underlines)

### 2.2 Left and Right Scrolling Margins (DECSLRM)
- **Feature**:
  - Enable DECSLRM mode (`CSI ? 69 h`).
  - Implement `CSI Pl ; Pr s` (Set Left and Right Margins).
  - All cursor horizontal movements (`CHA`, `CUF`, `CUB`, `HPA`), character insertions (`ICH`), and deletions (`DCH`) respect active margins.
  - In Origin Mode (`DECOM`), coordinates `(1, 1)` refer to `(TopMargin, LeftMargin)`.

### 2.3 DEC Rectangle Operations
- **Rectangle Checksum (DECRQCRA)**:
  - Sequence: `CSI Pid ; Pn ; Pt ; Pl ; Pb ; Pr * y`
  - Compute checksum over the defined rectangular area and respond with:
    `DCS Pid ! ~ xxxx ST`
  - Critical for automated test suites like `esctest2` to verify screen contents without OCR or screenshot comparison.
- **Erase / Fill Rectangle (DECERA, DECFRA)**:
  - Fill rectangular sub-region with blank cells or a specific character/attribute.

### 2.4 Complete Query & Report Protocols
- **Status String Inquiries (DECRQSS)**:
  - Sequence: `DCS $ q <command> ST`
  - Implement query and valid response reporting for:
    - `"m"`: SGR (Current attributes)
    - `"r"`: DECSTBM (Top/Bottom margins)
    - `"s"`: DECSLRM (Left/Right margins)
    - `"q"`: DECSCUSR (Cursor style)
  - Respond with `DCS 1 $ r <data> ST` (valid) or `DCS 0 $ r ST` (invalid).
- **OSC Color Queries**:
  - OSC 4 (Palette query): `OSC 4 ; c ; ? BEL` $\rightarrow$ replies with `OSC 4 ; c ; rgb:RRRR/GGGG/BBBB ST`.
  - OSC 10 / 11 / 12 (Default FG / BG / Cursor color query):
    `OSC 10 ; ? BEL` $\rightarrow$ replies with current RGB colors.

### 2.5 Character Set Designation & G0–G3 Mappings
- **Feature**:
  - Support `ESC ( <charset>` (G0), `ESC ) <charset>` (G1), `ESC * <charset>` (G2), `ESC + <charset>` (G3).
  - Support Shift-Out (`0x0E` / `SO` $\rightarrow$ invoke G1) and Shift-In (`0x0F` / `SI` $\rightarrow$ invoke G0).
  - Support DEC Special Graphics character set (`ESC ( 0`) for rendering legacy line-drawing characters (box borders, corners, horizontal lines, crosshairs) without UTF-8 codepoints.

### 2.6 Synchronized Output (Mode 2026)
- **Feature**:
  - Sequence: `CSI ? 2026 h` (BSU - Begin Synchronized Update) and `CSI ? 2026 l` (ESU - End Synchronized Update).
  - Buffer all mutations during a synchronized update and commit them atomically to the render surface to prevent flicker and tearing during rich TUI animations.

---

## 3. Implementation Steps

1. **Harness Setup**:
   - Flesh out [EscTest2ConformanceTests.cs](file:///D:/repos/Rikitav/VirtualTerminal/tests/VirtualTerminal.Tests/EscTest2ConformanceTests.cs) with mock session and response listener.
2. **Implement DECSLRM & DECOM Margins**:
   - Update `TerminalScreenBuffer` to store `MarginLeft` and `MarginRight`.
   - Update `TerminalDecoder.CursorPositionSet` and line operations to respect 2D margins.
3. **Implement DECRQCRA & DECRQSS**:
   - Add checksum calculation algorithm matching DEC VT520 / xterm specification.
   - Dispatch responses via `TerminalDecoder.Respond`.
4. **Implement OSC 4/10/11/12 Query Replies**:
   - Extend `OscDispatcher` to parse `?` and emit responses using `TerminalOptions.AllowOSCColorQueries`.
5. **Port Test Matrix**:
   - Gradually port all 50+ test files from `esctest2/tests/*.py` to xUnit tests.

---

## 4. Acceptance Criteria & Metrics
- All ported `esctest2` test fixtures pass under xUnit.
- 100% compliance with xterm-standard cursor movements and rectangular margin behaviors.
- Successful color and setting round-trip reports via `DECRQSS` and `OSC 4/10/11/12`.
- Synchronized output suppresses intermediate frame rendering until `ESU` is received.
