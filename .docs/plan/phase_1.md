# Phase 1: Core VT Engine Optimization & Memory Architecture

## 1. Executive Summary
Phase 1 focuses on eliminating critical performance bottlenecks, heap churn, GC pauses, and safety vulnerabilities in `VirtualTerminal`. The primary goal is achieving microsecond-tier throughput comparable to high-performance terminal engines (e.g., Alacritty, Windows Terminal, Ghostty) through zero-allocation parsing, paged circular screen buffers, fast-path character metrics, and stream buffer safety.

---

## 2. Key Objectives & Deliverables

### 2.1 Fast-Path ASCII Width Table ($O(1)$ Metrics)
- **Problem**: `Wcwidth.Of(rune)` currently executes two binary searches over 111 zero-width ranges and 73 wide ranges on *every single codepoint*, including ASCII characters `0x20..0x7E`.
- **Solution**:
  - Implement a 256-element direct lookup table (`ReadOnlySpan<sbyte>` / compile-time static array) for ASCII codepoints `0x00..0xFF`.
  - Printable ASCII `[0x20..0x7E]` returns `1` in $O(1)$ time with zero branching.
  - Non-ASCII codepoints ($\ge 0x80$) fall back to the binary search table.
  - Expected throughput gain: **10x to 25x faster rune dispatch**.

### 2.2 Zero-Allocation Ground-State Parser
- **Problem**: In `VtDecoder.ProcessPrintRun`, printable text runs are added to `List<byte> _utf8` via `AddRange`, decoded via rented arrays, and then cleared via `_utf8.RemoveRange(0, consumed)`. `RemoveRange` copies memory inside the list for every run.
- **Solution**:
  - Direct span-based UTF-8 decoding in place using `Utf8.ToUtf16` or `Rune.DecodeFromUtf8` directly over the incoming `ReadOnlySpan<byte>` slice without accumulating in a managed `List<byte>`.
  - Maintain only an 8-byte trailing carry buffer (`byte[8]`) for multi-byte runes split across incoming `Write` chunk boundaries.
  - Eliminate all heap allocations in the `Ground` state fast-path.

### 2.3 Paged Contiguous Buffer with Zero-Copy Scrolling
- **Problem**:
  - `TerminalScreenBuffer` uses an array of class instances (`TerminalRow[]`), each containing a separate `TerminalCellInfo[]` array.
  - Scrollback is stored in a `Queue<TerminalRow>`.
  - In `ScrollUp`, `new TerminalRow[n]` is allocated on every scroll line, copying references and churning Gen 2 memory.
  - `TerminalCellInfo` is 32 bytes per cell (due to uncompacted colors, style enums, and struct alignment).
- **Solution**:
  - Replace `TerminalRow[]` with a **Paged Circular Buffer** or **Chunked Ring** (e.g. 64-line contiguous blocks).
  - Scrolling is performed by **logical index arithmetic** (`_topRowIndex = (_topRowIndex + n) % _capacity`), completely eliminating row copying and array allocations.
  - Pack `TerminalCellInfo` into 16 bytes:
    - `uint Codepoint` (Unicode rune, 21 bits used, upper bits for quick classification)
    - `uint Foreground` (24-bit RGB or indexed palette + flags)
    - `uint Background` (24-bit RGB or indexed palette + flags)
    - `ushort Flags` (bold, italic, underline style, inverse, blink, wide)
    - `ushort HyperlinkId`
  - Memory reduction: $50\%\text{--}75\%$ drop in RAM usage for 10,000 scrollback lines.

### 2.4 Critical Stream I/O Safety & Defect Remediation
- **Fix `BufferStreamWriter` StackOverflow Vulnerability**:
  - Replace `Span<byte> writeData = stackalloc byte[charData.Length * 2];` with an ArrayPool threshold:
    ```csharp
    const int StackAllocLimit = 512;
    int maxBytes = Encoding.GetMaxByteCount(charData.Length);
    byte[]? rented = null;
    Span<byte> span = maxBytes <= StackAllocLimit
        ? stackalloc byte[maxBytes]
        : (rented = ArrayPool<byte>.Shared.Rent(maxBytes));
    try
    {
        int bytesWritten = Encoding.GetBytes(charData, span);
        _decoder.Write(span[..bytesWritten]);
    }
    finally
    {
        if (rented != null) ArrayPool<byte>.Shared.Return(rented);
    }
    ```
- **Fix `BufferStreamReader` $O(N^2)$ Churn**:
  - Replace `StringBuilder.Remove(0, 1)` with a contiguous ring buffer / circular queue of characters (`char[]` with head and tail pointers).
- **Fix `TerminalSession` Input Queue Locking**:
  - Replace `lock (_inputQueue)` on `ConcurrentQueue<byte>` with a lock-free ring buffer or chunked byte block queue to avoid per-byte node allocations.

---

## 3. Implementation Steps

1. **Benchmarking Harness**:
   - Create a `VirtualTerminal.Benchmarks` project using `BenchmarkDotNet`.
   - Add benchmarks for:
     - `CmatrixStreamBenchmark` (rapid color & cursor positioning)
     - `CatLargeFileBenchmark` (streaming massive UTF-8 and ASCII output)
     - `ScrollbackChurnBenchmark` (scrolling 50,000 lines through 10,000 line scrollback)
2. **Implement Fast-Path `Wcwidth`**:
   - Create static ASCII table in `Wcwidth.cs`.
   - Update `TerminalDecoder.Print` to use the fast lookup.
3. **Refactor `VtDecoder` UTF-8 Pipeline**:
   - Remove `List<byte> _utf8`.
   - Stream directly from incoming `ReadOnlySpan<byte>` with stack/fixed carry buffer.
4. **Implement Contiguous/Paged Screen Buffer**:
   - Implement `PagedScreenBuffer` with circular scroll indexing.
   - Compress `TerminalCellInfo` to 16 bytes.
5. **Fix `BufferStreamReader` & `BufferStreamWriter`**:
   - Apply bounded stackalloc guard and circular character buffer.

---

## 4. Acceptance Criteria & Metrics
- Zero allocations per byte in steady-state text streaming (`Ground` state).
- Scrolling 10,000 lines generates **0 B** of Gen 2 heap allocations.
- Throughput on `CatLargeFileBenchmark` improves by at least **400%**.
- All existing 157 unit tests in `VirtualTerminal.Tests` pass without regression.
