# VirtualTerminal Modernization & Optimization Roadmap

This directory contains the multi-phase architectural plan for optimizing, refactoring, modernizing, and expanding `VirtualTerminal` to theoretical conformance with the DEC STD 070 / xterm standards using [ThomasDickey/esctest2](https://github.com/ThomasDickey/esctest2).

---

## Roadmap Phases

```
+----------------------------------------------------------------------------------------------------+
|  Phase 1: Core Engine Optimization & Memory Architecture                                           |
|  - O(1) Fast-Path ASCII Width Table                                                                |
|  - Zero-Alloc UTF-8 Parsing Pipeline                                                               |
|  - Paged Circular Screen Buffer & Zero-Copy Scrolling                                              |
|  - Compact 16-Byte Cell Representation                                                             |
|  - Bounded Stream I/O Safety (Stackalloc fix & circular char buffer)                               |
+----------------------------------------------------------------------------------------------------+
                                                  |
                                                  v
+----------------------------------------------------------------------------------------------------+
|  Phase 2: Escape Sequence Parity & esctest2 Conformance Harness                                    |
|  - Port esctest2 automated test matrix to xUnit                                                    |
|  - DECSLRM (Left/Right margins) & DECOM (Origin Mode)                                              |
|  - DECRQCRA (Rectangle Checksums) & DECRQSS (Status inquiries)                                     |
|  - OSC Color Queries (4, 10, 11, 12) with RGB replies                                              |
|  - DEC Line Drawing & G0-G3 Character Sets (SO/SI)                                                 |
|  - Mode 2026 Synchronized Updates                                                                  |
+----------------------------------------------------------------------------------------------------+
                                                  |
                                                  v
+----------------------------------------------------------------------------------------------------+
|  Phase 3: Session Engine, Multiplexing & Transport Modernization                                   |
|  - High-throughput System.IO.Pipelines streaming                                                   |
|  - Native TerminalMultiplexer (Split panes, tabs, layout tree)                                     |
|  - Headless daemon mode & session detach/reattach persistence                                      |
|  - Kitty Keyboard Protocol (progressive enhancement)                                              |
+----------------------------------------------------------------------------------------------------+
                                                  |
                                                  v
+----------------------------------------------------------------------------------------------------+
|  Phase 4: Desktop Frontend Modernization (WPF, Avalonia & Qt)                                      |
|  - Hardware-accelerated dirty-rect rendering (Skia / DirectWrite)                                  |
|  - Rich underlines (curly, double, dotted, dashed) & SGR text decorations                          |
|  - Smooth animated caret with spring physics                                                       |
|  - Procedural box-drawing synthesis & font fallback chain                                          |
|  - Ready-to-use XAML TerminalMultiplexerControl (splits & tabs)                                    |
+----------------------------------------------------------------------------------------------------+
                                                  |
                                                  v
+----------------------------------------------------------------------------------------------------+
|  Phase 5: Advanced Visual Features (Graphics Protocols, Sixel & Kitty)                             |
|  - Sixel graphics decoder & scrollback integration                                                 |
|  - Kitty graphics protocol parser & texture cache                                                  |
|  - iTerm2 inline image protocol (OSC 1337)                                                         |
|  - Text-over-image clipping and buffer lifecycle                                                   |
+----------------------------------------------------------------------------------------------------+
                                                  |
                                                  v
+----------------------------------------------------------------------------------------------------+
|  Phase 6: Web & TUI Frontends, Ecosystem Expansion & Packaging                                     |
|  - Package modularization (VirtualTerminal.Core, VirtualTerminal.WPF, etc.)                        |
|  - VirtualTerminal.Blazor (HTML5 Canvas / WebGL WebAssembly & Server control)                      |
|  - VirtualTerminal.Spectre / Terminal.Gui TUI embedding                                            |
|  - Headless WebSocket / SSH daemon middleware                                                      |
|  - Public documentation & automated CI compliance reporting                                        |
+----------------------------------------------------------------------------------------------------+
```

---

## Phase Documents

- [Phase 1: Core VT Engine Optimization & Memory Architecture](phase_1.md)
- [Phase 2: Escape Sequence Parity & esctest2 Conformance Harness](phase_2.md)
- [Phase 3: Session Engine, Multiplexing & Transport Modernization](phase_3.md)
- [Phase 4: Desktop Frontend Modernization (WPF, Avalonia & Qt)](phase_4.md)
- [Phase 5: Advanced Visual Features (Graphics Protocols, Sixel & Kitty)](phase_5.md)
- [Phase 6: Web & TUI Frontends, Ecosystem Expansion & Packaging](phase_6.md)
