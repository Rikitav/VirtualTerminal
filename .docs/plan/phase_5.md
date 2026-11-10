# Phase 5: Advanced Visual Features (Graphics Protocols, Sixel & Kitty)

## 1. Executive Summary
Phase 5 equips `VirtualTerminal` with modern inline graphics capabilities. Modern terminal applications (such as Neovim image previews, CLI dashboards, `timg`, `viu`, and `fastfetch`) rely on inline raster and vector graphics protocols. Phase 5 adds native parsers and renderers for Sixel and Kitty Graphics protocols across the core engine and UI controls.

---

## 2. Key Objectives & Deliverables

### 2.1 Sixel Graphics Protocol (`DCS q ... ST`)
- **Protocol Details**:
  - Sequence begins with `DCS P1 ; P2 ; q` and streams 6-pixel vertical slivers encoded in characters `0x3F..0x7E`.
  - Palette definition commands (`# Pc ; Pu ; Px ; Py ; Pz`).
- **Engine Architecture**:
  - Implement streaming Sixel state machine in `VtDecoder.DcsHook`, `DcsPut`, and `DcsUnhook`.
  - Decode raw 6-pixel strips into an in-memory 32-bit RGBA pixel bitmap (`TerminalBitmap`).
  - Compute cell footprint (number of columns and rows spanned by the image dimensions).
  - Associate the `TerminalBitmap` with the screen buffer cells at the insertion coordinate, advancing the cursor past the graphic.
- **Scrollback Integration**:
  - Image blocks scroll naturally with rows in the circular buffer; partially scrolled-off images are clipped cleanly.

### 2.2 Kitty Graphics Protocol (`APC G ... ST`)
- **Protocol Details**:
  - Sequence format: `\x1b_Ga=...,f=...,s=...,v=...,m=...;<payload>\x1b\`
  - Supports direct 24-bit RGB, 32-bit RGBA, and PNG transmission (compressed or chunked base64).
  - Placements by cell columns/rows, scaling, and z-index ordering.
- **Engine Architecture**:
  - Implement `VtDecoder.SosPmApcDispatch` handler for `APC G` actions (`a=t` transmit, `a=p` put/display, `a=d` delete).
  - Cache decoded image surfaces in a shared graphic resource pool keyed by image ID.
  - Render GPU-accelerated texture quads over the cell grid in Avalonia and WPF.

### 2.3 iTerm2 Inline Images Protocol (`OSC 1337`)
- **Feature**:
  - Parse `OSC 1337 ; File=name=...;size=...;inline=1:<base64-data> BEL`.
  - Decode standard PNG/JPEG payloads and place inline bitmaps at the cursor position.

### 2.4 Image Cell Management & Clipping in Buffer
- **Data Model**:
  - Add image placeholder metadata to `TerminalCellInfo` (e.g. `GraphicId`, `GraphicSubRect`).
  - When text is printed over an image cell, intelligently slice or clear the obscured image pixels.
  - Expose API to clear images on screen reset (`RIS`, `ED 2`, `ED 3`).

---

## 3. Implementation Steps

1. **Sixel Decoder Implementation**:
   - Create `SixelDecoder` in `VirtualTerminal.Vt.Graphics`.
   - Implement palette mapping (RGB/HLS), repeat counts (`!`), and line carriage returns (`$`, `-`).
2. **Kitty Protocol State Machine**:
   - Parse key-value APC parameters.
   - Decompress and parse PNG / raw RGBA chunks.
3. **UI Renderer Integration**:
   - In Avalonia: draw cached `WriteableBitmap` or Skia `SKImage` at cell pixel boundaries.
   - In WPF: draw `BitmapSource` using `DrawingContext.DrawImage`.
4. **Scrollback & Resizing Tests**:
   - Add unit tests verifying image position preservation during buffer scrolling and terminal resize.

---

## 4. Acceptance Criteria & Metrics
- Correct rendering of test images produced by `sixel-demo`, `chafa`, `viu`, and `timg`.
- Zero memory leaks: images removed from the screen or discarded from scrollback have their GPU/RAM bitmaps disposed immediately.
- Terminal resize correctly recalculates pixel bounds without distorting aspect ratios.
