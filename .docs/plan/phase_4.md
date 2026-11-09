# Phase 4: Desktop Frontend Modernization (WPF, Avalonia & Qt)

## 1. Executive Summary
Phase 4 modernizes the desktop UI rendering pipeline across the supported GUI frameworks (`VirtualTerminal.WPF`, `VirtualTerminal.Avalonia`, and `VirtualTerminal.Qt`). It transitions rendering from CPU-bound immediate mode redraws to hardware-accelerated, dirty-rect-optimized pipelines with smooth caret animation, rich text decoration rendering, and font fallback.

---

## 2. Key Objectives & Deliverables

### 2.1 Hardware-Accelerated Rendering & Dirty-Region Optimization
- **Problem**: In current WPF and Avalonia controls, when any row becomes dirty, large portions or the entire screen are frequently redrawn, causing high GPU/CPU draw overhead during rapid streaming.
- **Solution**:
  - **Avalonia**: Use Direct `DrawingContext` with Skia / GPU glyph atlas caching or custom Skia visual node (`CustomDrawOperation`). Coalesce adjacent dirty cells of identical styles into single draw operations.
  - **WPF**: Utilize DirectWrite / `GlyphRun` batching with hardware layer caching.
  - **Partial Invalidation**: Track dirty column ranges per dirty row (`ushort MinDirtyX`, `ushort MaxDirtyX`) rather than redrawing whole rows.

### 2.2 Rich Text Decorations & Underline Styles
- **Feature**:
  - Render modern SGR 4:x styled underlines with custom geometry shaders/drawing paths:
    - Single underline (`4:1`)
    - Double underline (`4:2`)
    - Curly / wavy underline (`4:3`) (typical in code editors for diagnostics/linting)
    - Dotted underline (`4:4`)
    - Dashed underline (`4:5`)
  - Render independent underline colors (`SGR 58:2:...m` / `58:5:...m`).
  - Render overline (`SGR 53`), strikethrough (`SGR 9`), framed (`SGR 51`), and encircled (`SGR 52`).

### 2.3 Smooth Animated Caret & Cursor Customization
- **Feature**:
  - Implement smooth cursor position interpolation (spring or cubic ease-out physics) when the cursor jumps across cells, matching modern terminals (Ghostty, Windows Terminal, Warp).
  - Configurable cursor shapes: Block, Underline, Bar (I-Beam) with blinking states (DECSCUSR `0` through `6`).
  - Cursor glow / aura effect when typing rapidly.

### 2.4 Font Fallback & Box-Drawing Glyph Synthesis
- **Problem**: Monospace fonts often lack complete coverage for box-drawing characters (`U+2500..U+257F`), powerline glyphs (`U+E0A0..U+E0D4`), or emoji, causing alignment breaks, missing glyph boxes, or uneven line heights.
- **Solution**:
  - Implement direct **algorithmic box-drawing synthesis**: draw borders, corners, tees, crosshairs, and progress blocks procedurally using vector geometry rather than relying on font glyphs.
  - Custom font fallback chain: Primary Monospace $\rightarrow$ Nerd Font Symbols $\rightarrow$ Noto Color Emoji / Segoe UI Emoji $\rightarrow$ System Fallback.

### 2.5 Multi-Tab & Split-Pane Controls for WPF and Avalonia
- **Feature**:
  - Provide ready-to-use XAML components:
    - `<TerminalMultiplexerControl />`: Automatically renders tabs and split panes using Phase 3's `TerminalMultiplexer`.
    - Resizable splitters with mouse drag support.
    - Floating action overlay buttons (split horizontal, split vertical, zoom pane, search bar).

---

## 3. Implementation Steps

1. **Implement Algorithmic Box-Drawing**:
   - Create `BoxDrawingRenderer` that proceduralizes Unicode `U+2500..U+257F` and block elements `U+2580..U+259F`.
2. **Implement Styled Underlines Geometry**:
   - Create vector paths for curly, dotted, and dashed underlines in Avalonia and WPF.
3. **Smooth Caret Interpolator**:
   - Create `AnimatedTerminalCursorRenderer` inheriting from `TerminalCursorRenderer`.
   - Add Dispatcher/RenderLoop spring animation tracking target `(X, Y)`.
4. **Develop `TerminalMultiplexerControl`**:
   - Implement split layout tree view in Avalonia and WPF.

---

## 4. Acceptance Criteria & Metrics
- Stable 60–120 FPS rendering during continuous screen updates with less than 5% CPU usage.
- Pixel-perfect procedural rendering of all box-drawing and powerline symbols across any font size.
- Wavy, dotted, double, and dashed underlines render with accurate spacing and colors.
- Interactive split resizing without lag or visual artifacts.
