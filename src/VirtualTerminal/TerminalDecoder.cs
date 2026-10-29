using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using VirtualTerminal.Buffer;
using VirtualTerminal.Enums;
using VirtualTerminal.Interfaces;
using VirtualTerminal.Model;
using VirtualTerminal.Options;
using VirtualTerminal.Vt;

namespace VirtualTerminal;

/// <summary>
/// The terminal state machine's handler: receives parsed VT/ANSI events from <see cref="VtDecoder"/>
/// and applies them to the <see cref="TerminalScreenBuffer"/> and <see cref="TerminalState"/>.
/// Implements <see cref="ITerminalDecoder"/> for the session layer and keeps the historical class
/// name so existing casts (<c>is TerminalDecoder</c>) keep working.
///
/// <para>
/// Cursor debounce: CR/LF bytes that end a <see cref="Write"/> chunk are not applied to
/// <see cref="State"/> immediately. They are parked internally and applied only when any further
/// bytes arrive, so consumers rendering after each write never observe the transient cursor
/// position at the start of a fresh line before the bytes following the newline have been
/// processed. Any non-newline event (text, escape/CSI/OSC/DCS sequences, BEL/BS/TAB, resize)
/// flushes the parked bytes first.
/// </para>
/// </summary>
public sealed class TerminalDecoder : IVtHandler, ITerminalDecoder
{
    private readonly VtDecoder _parser;
    private readonly List<string> _hyperlinks = [];

    // Trailing-newline debounce: CR/LF bytes that end a Write chunk are parked here and only
    // applied when further bytes arrive, so the UI never renders the transient cursor position
    // at the start of a fresh line before the bytes following the newline land.
    private bool _pendingReturn;
    private int _pendingLineFeeds;

    /// <summary>Gets the terminal screen buffer being modified by this decoder.</summary>
    public TerminalScreenBuffer Buffer { get; }

    /// <summary>Gets the current terminal state (cursor, attributes, modes).</summary>
    public TerminalState State { get; } = new();

    /// <summary>Gets or sets options that control rendering and behavior.</summary>
    public TerminalOptions Options { get; set; }

    /// <inheritdoc />
    public bool AutoFlush { get; set; }

    /// <summary>Current window title (OSC 0/1/2).</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Raised when the title changes.</summary>
    public event EventHandler<string>? TitleChanged;

    /// <summary>Raised on BEL.</summary>
    public event EventHandler? Bell;

    /// <summary>Raised when an OSC 9 desktop notification is received.</summary>
    public event EventHandler<string>? NotificationReceived;

    /// <summary>Raised when an OSC 52 clipboard set/query is received.</summary>
    public event EventHandler<ClipboardRequestEventArgs>? ClipboardRequested;

    /// <summary>Raised when an OSC 133 shell-integration mark is received.</summary>
    public event EventHandler<string>? ShellIntegrationMark;

    /// <summary>Raised when an OSC 1337 iTerm2 annotation is received.</summary>
    public event EventHandler<string>? Iterm2Annotation;

    /// <summary>Wire to the session output queue so reports (DSR/DA/…) reach the client.</summary>
    public Action<ReadOnlySpan<byte>>? SendOutput { get; set; }

    /// <summary>Gets the current cursor position as a point.</summary>
    public TerminalPoint CursorPosition => new(State.CursorX, State.CursorY);

    /// <summary>Gets the encoding used by the underlying VT parser.</summary>
    public Encoding Encoding => _parser.Encoding;

    /// <summary>
    /// Initializes a new <see cref="TerminalDecoder"/> with an optional buffer and options.
    /// </summary>
    /// <param name="buffer">Screen buffer to use, or <c>null</c> to create a default buffer.</param>
    /// <param name="options">Options to use, or <c>null</c> to use default options.</param>
    public TerminalDecoder(TerminalScreenBuffer? buffer = null, TerminalOptions? options = null)
    {
        Buffer = buffer ?? new TerminalScreenBuffer(128, 20);
        Options = options ?? new TerminalOptions();
        Buffer.SetScrollbackMax(Options.ScrollbackMaxLines);
        _parser = new VtDecoder(this);
    }

    /// <summary>Writes a span of raw VT/ANSI bytes to the decoder.</summary>
    /// <param name="data">Bytes to process.</param>
    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        lock (Buffer.SyncRoot)
            _parser.Write(data);
    }

    /// <inheritdoc />
    public void Flush()
    {
        lock (Buffer.SyncRoot)
            FlushPendingNewline();
    }

    /// <summary>Resizes the underlying screen buffer and clamps the cursor into the new bounds.</summary>
    public void Resize(ushort columns, ushort rows)
        => Resize(columns, rows, pushScrollback: true);

    /// <summary>Resizes the underlying screen buffer and clamps the cursor into the new bounds.</summary>
    /// <param name="columns">New number of columns.</param>
    /// <param name="rows">New number of rows.</param>
    /// <param name="pushScrollback"><c>true</c> to push removed rows into scrollback.</param>
    public void Resize(ushort columns, ushort rows, bool pushScrollback)
    {
        lock (Buffer.SyncRoot)
        {
            FlushPendingNewline();
            Buffer.Resize(columns, rows, State, pushScrollback);
        }
    }

    /// <summary>Releases the underlying VT parser resources.</summary>
    public void Dispose()
        => _parser.Dispose();

    internal string? GetHyperlink(byte id)
        => id == 0 || id > _hyperlinks.Count ? null : _hyperlinks[id - 1];

    internal byte RegisterHyperlink(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return 0;

        int idx = _hyperlinks.IndexOf(uri);
        if (idx < 0)
        {
            if (_hyperlinks.Count >= 255)
                return 0;

            _hyperlinks.Add(uri);
            idx = _hyperlinks.Count - 1;
        }

        return (byte)(idx + 1);
    }

    internal void SetTitle(string title)
    {
        Title = title;
        TitleChanged?.Invoke(this, title);
    }

    private void RaiseBell()
        => Bell?.Invoke(this, EventArgs.Empty);

    internal void RaiseNotification(string message)
        => NotificationReceived?.Invoke(this, message);

    internal void RaiseClipboardRequested(string selection, string? data)
        => ClipboardRequested?.Invoke(this, new ClipboardRequestEventArgs(selection, data));

    internal void RaiseShellIntegrationMark(string arguments)
        => ShellIntegrationMark?.Invoke(this, arguments);

    internal void RaiseIterm2Annotation(string arguments)
        => Iterm2Annotation?.Invoke(this, arguments);

    private void Respond(string response)
    {
        if (SendOutput is null)
            return;

        int byteCount = Encoding.UTF8.GetByteCount(response);
        if (byteCount == 0)
            return;

        const int StackAllocThreshold = 512;
        if (byteCount <= StackAllocThreshold)
        {
            Span<byte> bytes = stackalloc byte[byteCount];
            Encoding.UTF8.GetBytes(response, bytes);
            SendOutput(bytes);
            return;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            Encoding.UTF8.GetBytes(response, rented);
            SendOutput(rented.AsSpan(0, byteCount));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    // ============== IVtHandler ==============

    /// <inheritdoc/>
    public void Print(Rune rune)
    {
        Rune single = rune;
        Print(MemoryMarshal.CreateReadOnlySpan(in single, 1));
    }

    /// <inheritdoc/>
    public void Print(scoped ReadOnlySpan<Rune> runes)
    {
        if (runes.IsEmpty)
            return;

        try
        {
            FlushPendingNewline();

            int cols = Buffer.Columns;
            CellRenderStyle style = State.Attributes.ToCellStyle(Options.Palette, Options.BoldAsBright);
            bool insertMode = State.Modes.InsertMode;
            int lastDirtyRow = -1;

            foreach (Rune rune in runes)
            {
                int width = Wcwidth.Of(rune);
                if (width < 0)
                    continue;  // non-printable; should not reach here

                if (width == 0)
                    width = 1;  // combining mark: Phase 1 treats as width-1

                // Deferred wrap from a previous print that ended on the last column.
                if (State.WrapPending)
                {
                    State.CursorX = 0;
                    Index();
                    State.WrapPending = false;
                }

                int cx = State.CursorX;
                int cy = State.CursorY;

                // A wide glyph that does not fit wraps first (when autowrap is on).
                if (cx + width > cols)
                {
                    if (State.Modes.AutoWrap && width <= cols)
                    {
                        State.CursorX = 0;
                        Index();
                        cx = 0;
                        cy = State.CursorY;
                    }
                    else
                    {
                        width = Math.Max(1, cols - cx);
                    }
                }

                if (insertMode)
                    Buffer.InsertChars(cy, cx, width);

                ref TerminalCellInfo cell = ref Buffer.GetCell(cy, cx);
                CellStyleFlags flags = style.Flags;
                if (width == 2)
                    flags |= CellStyleFlags.Wide;

                cell = new TerminalCellInfo(rune, style with { Flags = flags });

                if (width == 2 && cx + 1 < cols)
                {
                    ref TerminalCellInfo cont = ref Buffer.GetCell(cy, cx + 1);
                    cont = new TerminalCellInfo(new Rune(' '), style with { Flags = style.Flags | CellStyleFlags.Continuation });
                }

                if (cy != lastDirtyRow)
                {
                    Buffer.MarkRowDirty(cy);
                    lastDirtyRow = cy;
                }

                // Advance cursor: stay on the last column and arm pending-wrap when autowrap is on.
                if (cx + width < cols)
                {
                    State.CursorX = cx + width;
                    State.WrapPending = false;
                }
                else
                {
                    State.CursorX = cols - 1;
                    State.WrapPending = State.Modes.AutoWrap;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TerminalDecoder.Print failed for a run of {runes.Length} runes.\n{ex}");
        }
    }

    /// <inheritdoc/>
    public void Execute(byte control)
    {
        switch (control)
        {
            case 0x0A:
            case 0x0B:
            case 0x0C:
                if (AutoFlush)
                {
                    LineFeed();
                }
                else
                {
                    // Trailing-newline debounce: park the line feed; FlushPendingNewline applies it
                    // when any further bytes arrive.
                    _pendingLineFeeds++;
                }

                break;
            case 0x0D:
                if (AutoFlush)
                {
                    CarriageReturn();
                }
                else
                {
                    _pendingReturn = true;
                }

                break;
            case 0x07:
                FlushPendingNewline();
                RaiseBell();
                break;
            case 0x08:
                FlushPendingNewline();
                CursorLeft();
                break;
            case 0x09:
                FlushPendingNewline();
                Tab();
                break;
            default:
                break;  // other C0 ignored
        }
    }

    /// <summary>
    /// Applies parked trailing CR/LF bytes, if any. See the note on <see cref="Execute"/> for
    /// why newline bytes are held back until further input arrives.
    /// </summary>
    private void FlushPendingNewline()
    {
        if (!_pendingReturn && _pendingLineFeeds == 0)
            return;

        bool cr = _pendingReturn;
        int lfs = _pendingLineFeeds;
        _pendingReturn = false;
        _pendingLineFeeds = 0;

        if (cr)
            CarriageReturn();

        for (int i = 0; i < lfs; i++)
            LineFeed();
    }

    /// <inheritdoc/>
    public void EscDispatch(byte final, ReadOnlySpan<byte> intermediates)
    {
        FlushPendingNewline();

        switch ((char)final)
        {
            case '7':
                State.SaveCursor();
                break;            // DECSC
            case '8':
                State.RestoreCursor();
                break;         // DECRC
            case 'D':
                Index();
                break;                       // IND
            case 'E':
                NewLine();
                break;                     // NEL
            case 'H':
                Buffer.SetTabStop(State.CursorX);
                break; // HTS
            case 'M':
                ReverseIndex();
                break;                // RI
            case 'c':
                FullReset();
                break;                   // RIS
            case '=':
                State.Modes.ApplicationKeypad = true;
                break;   // DECKPAM
            case '>':
                State.Modes.ApplicationKeypad = false;
                break;  // DECKPNM
            case '(':
            case ')':
            case '*':
            case '+':
                break;  // G0/G1/G2/G3 charset designation: UTF-8 terminals treat this as a no-op.
            case '#':
                if (intermediates.Length == 0 || intermediates[0] == '8')
                    AlignmentTest();
                break;
            default:
                break;
        }
    }

    /// <inheritdoc/>
    public void CsiDispatch(byte final, ReadOnlySpan<byte> intermediates, in CsiParams parameters, char privateMarker)
    {
        FlushPendingNewline();

        char fc = (char)final;
        try
        {
            // DECSCUSR: CSI Ps SP q — cursor style is a renderer concern; parse and ignore.
            if (fc == 'q' && intermediates.Length == 1 && intermediates[0] == ' ')
                return;

            // DECRQSS: CSI ? Ps $ p
            if (fc == 'p' && intermediates.Length == 1 && intermediates[0] == '$' && privateMarker == '?')
            {
                RequestSelectionOrSetting(parameters.At(0, 0));
                return;
            }

            switch (fc)
            {
                // ---- Cursor movement ----
                case 'A':
                    CursorVertical(-parameters.At(0, 1));
                    break;       // CUU
                case 'B':
                    CursorVertical(+parameters.At(0, 1));
                    break;       // CUD
                case 'C':
                    CursorHorizontal(+parameters.At(0, 1));
                    break;     // CUF
                case 'D':
                    CursorHorizontal(-parameters.At(0, 1));
                    break;     // CUB
                case 'E':
                    CursorLine(parameters.At(0, 1), +1);
                    break;        // CNL
                case 'F':
                    CursorLine(parameters.At(0, 1), -1);
                    break;        // CPL
                case 'G':
                case '`':
                    CursorToColumn(parameters.At(0, 1));
                    break; // CHA / HPA
                case 'd':
                    CursorToRow(parameters.At(0, 1));
                    break;           // VPA
                case 'e':
                    CursorVertical(+parameters.At(0, 1));
                    break;       // VPR (rare)
                case 'H':
                case 'f':
                    CursorPositionSet(parameters.At(0, 1), parameters.At(1, 1));
                    break; // CUP/HVP

                // ---- Erase ----
                case 'J':
                    EraseDisplay(parameters.At(0, 0));
                    break;          // ED
                case 'K':
                    EraseLine(parameters.At(0, 0));
                    break;             // EL
                case 'X':
                    Buffer.EraseChars(State.CursorY, State.CursorX, Math.Max(1, parameters.At(0, 1)));
                    break; // ECH

                // ---- Insert / delete ----
                case '@':
                    Buffer.InsertChars(State.CursorY, State.CursorX, Math.Max(1, parameters.At(0, 1)));
                    break; // ICH
                case 'P':
                    Buffer.DeleteChars(State.CursorY, State.CursorX, Math.Max(1, parameters.At(0, 1)));
                    break;  // DCH
                case 'L':
                    InsertLines(Math.Max(1, parameters.At(0, 1)));
                    break; // IL
                case 'M':
                    DeleteLines(Math.Max(1, parameters.At(0, 1)));
                    break; // DL

                // ---- Scroll region ----
                case 'S':
                    Buffer.ScrollUp(Math.Max(1, parameters.At(0, 1)));
                    break;  // SU
                case 'T':
                    Buffer.ScrollDown(Math.Max(1, parameters.At(0, 1)));
                    break; // SD

                // ---- Tabs ----
                case 'I':
                    ForwardTab(Math.Max(1, parameters.At(0, 1)));
                    break;  // CHT
                case 'Z':
                    BackwardTab(Math.Max(1, parameters.At(0, 1)));
                    break; // CBT
                case 'g':
                    ClearTab(parameters.At(0, 0));
                    break;                  // TBC

                // ---- Save / restore (ANSI) ----
                case 's':
                    State.SaveCursor();
                    break;
                case 'u':
                    State.RestoreCursor();
                    break;

                // ---- Modes ----
                case 'h':
                    SetModes(parameters, set: true, privateMarker);
                    break;
                case 'l':
                    SetModes(parameters, set: false, privateMarker);
                    break;

                // ---- Reports ----
                case 'n':
                    DeviceStatusReport(parameters.At(0, 0));
                    break;
                case 'c':
                case '>': /* handled via marker below */
                    DeviceAttributes(privateMarker);
                    break;
                case 'x':
                    DecRequestParameters(parameters.At(0, 1));
                    break; // DECREQTPARM

                // ---- Scroll region (DECSTBM) ----
                case 'r':
                    SetScrollRegion(parameters.At(0, 1), parameters.At(1, Buffer.Rows));
                    break;

                // ---- SGR ----
                case 'm':
                    SetGraphicRendition(parameters);
                    break;

                default:
                    break;  // ignore unknown CSI
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TerminalDecoder.CsiDispatch failed for final {fc}.\n{ex}");
        }
    }

    /// <inheritdoc/>
    public void OscDispatch(ReadOnlySpan<byte> data)
    {
        FlushPendingNewline();
        OscDispatcher.ProcessOsc(this, data);
    }

    /// <inheritdoc/>
    public void DcsHook(byte final, ReadOnlySpan<byte> intermediates, in CsiParams parameters, char privateMarker)
    {
        FlushPendingNewline();
        // Phase 1: DCS payload is consumed by the parser but not interpreted (Sixel/DECRQSS = Phase 3).
    }

    /// <inheritdoc/>
    public void DcsPut(byte b)
    {
    }

    /// <inheritdoc/>
    public void DcsUnhook()
    {
        FlushPendingNewline();
    }

    /// <inheritdoc/>
    public void SosPmApcDispatch(byte kind, ReadOnlySpan<byte> data)
    {
        FlushPendingNewline();
    }

    // ============== Movement primitives ==============

    private void CursorLeft()
    {
        if (State.CursorX > 0)
            State.CursorX--;

        State.WrapPending = false;
    }

    private void Tab()
    {
        State.CursorX = Buffer.NextTabStop(State.CursorX);
        State.WrapPending = false;
    }

    private void CarriageReturn()
    {
        State.CursorX = 0;
        State.WrapPending = false;
    }

    private void LineFeed()
    {
        Index();
        if (State.Modes.LineFeedNewLine)
            State.CursorX = 0;
    }

    private void Index()
    {
        if (State.CursorY < Buffer.ScrollBottom)
            State.CursorY++;
        else
            Buffer.ScrollUp(Buffer.ScrollTop, Buffer.ScrollBottom, 1);

        State.WrapPending = false;
    }

    private void NewLine()
    {
        State.CursorX = 0;
        Index();
    }

    private void ReverseIndex()
    {
        if (State.CursorY > Buffer.ScrollTop)
            State.CursorY--;
        else
            Buffer.ScrollDown(Buffer.ScrollTop, Buffer.ScrollBottom, 1);

        State.WrapPending = false;
    }

    private void CursorVertical(int delta)
    {
        int limit = delta < 0
            ? (State.CursorY >= Buffer.ScrollTop ? Buffer.ScrollTop : 0)
            : (State.CursorY <= Buffer.ScrollBottom ? Buffer.ScrollBottom : Buffer.Rows - 1);

        State.CursorY = Math.Clamp(State.CursorY + delta, 0, Buffer.Rows - 1);
        if (delta < 0 && State.CursorY < limit)
            State.CursorY = limit;

        if (delta > 0 && State.CursorY > limit)
            State.CursorY = limit;

        State.WrapPending = false;
    }

    private void CursorHorizontal(int delta)
    {
        State.CursorX = Math.Clamp(State.CursorX + delta, 0, Buffer.Columns - 1);
        State.WrapPending = false;
    }

    private void CursorLine(int count, int dir)
    {
        count = Math.Max(1, count);
        State.CursorY = Math.Clamp(State.CursorY + dir * count, 0, Buffer.Rows - 1);
        State.CursorX = 0;
        State.WrapPending = false;
    }

    private void CursorToColumn(int column)
    {
        State.CursorX = Math.Clamp(column - 1, 0, Buffer.Columns - 1);
        State.WrapPending = false;
    }

    private void CursorToRow(int row)
    {
        int target = State.Modes.OriginMode
            ? Buffer.ScrollTop + row - 1 : row - 1;

        State.CursorY = Math.Clamp(target, 0, Buffer.Rows - 1);
        State.WrapPending = false;
    }

    private void CursorPositionSet(int row, int col)
    {
        int y = State.Modes.OriginMode
            ? Buffer.ScrollTop + row - 1 : row - 1;

        int x = col - 1;
        State.CursorY = Math.Clamp(y, 0, Buffer.Rows - 1);
        State.CursorX = Math.Clamp(x, 0, Buffer.Columns - 1);
        State.WrapPending = false;
    }

    // ============== Erase / insert / delete ==============

    private void EraseDisplay(int mode)
    {
        int cx = State.CursorX, cy = State.CursorY;
        switch (mode)
        {
            case 0: // cursor → end of screen
                Buffer.ClearCells(cy, cx, Buffer.Columns - cx);
                Buffer.ClearRows(cy + 1, Buffer.Rows - cy - 1);
                break;

            case 1: // start → cursor
                Buffer.ClearRows(0, cy);
                Buffer.ClearCells(cy, 0, cx + 1);
                break;

            case 2: // whole screen
                Buffer.ClearRows(0, Buffer.Rows);
                break;

            case 3: // whole screen + scrollback
                Buffer.ClearRows(0, Buffer.Rows);
                break;  // scrollback cleared separately if needed (Phase 2 history API)
        }
    }

    private void EraseLine(int mode)
    {
        int cx = State.CursorX, cy = State.CursorY;
        switch (mode)
        {
            case 0:
                Buffer.ClearCells(cy, cx, Buffer.Columns - cx);
                break;

            case 1:
                Buffer.ClearCells(cy, 0, cx + 1);
                break;

            case 2:
                Buffer.ClearCells(cy, 0, Buffer.Columns);
                break;
        }
    }

    private void InsertLines(int n)
    {
        if (State.CursorY > Buffer.ScrollBottom)
            return;

        int bottom = Buffer.ScrollBottom;
        Buffer.InsertLines(State.CursorY, n, bottom);
        State.CursorX = 0;
        State.WrapPending = false;
    }

    private void DeleteLines(int n)
    {
        if (State.CursorY > Buffer.ScrollBottom)
            return;

        int bottom = Buffer.ScrollBottom;
        Buffer.DeleteLines(State.CursorY, n, bottom);
        State.CursorX = 0;
        State.WrapPending = false;
    }

    private void ForwardTab(int count)
    {
        for (int i = 0; i < count; i++)
            State.CursorX = Buffer.NextTabStop(State.CursorX);

        State.WrapPending = false;
    }

    private void BackwardTab(int count)
    {
        for (int i = 0; i < count; i++)
            State.CursorX = Buffer.PrevTabStop(State.CursorX);

        State.WrapPending = false;
    }

    private void ClearTab(int mode)
    {
        if (mode == 0)
            Buffer.ClearTabStop(State.CursorX);
        else if (mode == 3)
            Buffer.ClearAllTabStops();
    }

    // ============== Modes ==============

    private void SetModes(in CsiParams parameters, bool set, char marker)
    {
        for (int i = 0; i < parameters.Length; i++)
            SetMode(parameters[i], set, marker);
    }

    private void SetMode(int mode, bool set, char marker)
    {
        if (marker == '?')
        {
            switch (mode)
            {
                case 1:
                    State.Modes.ApplicationCursorKeys = set;
                    break;

                case 5:
                    State.Modes.ReverseVideo = set;
                    break;

                case 6:
                    State.Modes.OriginMode = set;
                    break;

                case 7:
                    State.Modes.AutoWrap = set;
                    break;

                case 12:
                    State.Modes.CursorBlink = set;
                    break;

                case 25:
                    State.Modes.CursorVisible = set;
                    break;

                case 47:
                case 1047:
                    {
                        if (set)
                            Buffer.EnterAlternateScreen();
                        else
                            Buffer.LeaveAlternateScreen();
                        break;
                    }

                case 1048:
                    {
                        if (set)
                            State.SaveCursor();
                        else
                            State.RestoreCursor();

                        break;
                    }

                case 1049:
                    {
                        if (set)
                        {
                            State.SaveCursor();
                            Buffer.EnterAlternateScreen();
                        }
                        else
                        {
                            Buffer.LeaveAlternateScreen();
                            State.RestoreCursor();
                        }

                        break;
                    }

                case 66:
                    State.Modes.ApplicationKeypad = set;
                    break;

                case 67:
                    State.Modes.BackspaceSendsControlH = set;
                    break;

                case 1000:
                case 1002:
                case 1003:
                    {
                        State.Modes.MouseTracking = set
                            ? (mode == 1003 ? MouseTrackingMode.AnyEvent
                               : mode == 1002 ? MouseTrackingMode.ButtonEvent
                               : MouseTrackingMode.X10)
                            : MouseTrackingMode.Off;

                        break;
                    }

                case 1006:
                    State.Modes.SgrMouseEncoding = set;
                    break;

                case 1004:
                    State.Modes.FocusReporting = set;
                    break;

                case 2004:
                    State.Modes.BracketedPaste = set;
                    break;

                default:
                    break;
            }
        }
        else
        {
            switch (mode)
            {
                case 4:
                    State.Modes.InsertMode = set;
                    break;       // IRM
                case 20:
                    State.Modes.LineFeedNewLine = set;
                    break; // LNM
                default:
                    break;
            }
        }
    }

    private void SetScrollRegion(int top, int bottom)
    {
        if (top == 1 && bottom == Buffer.Rows)
            Buffer.ResetScrollRegion();
        else
            Buffer.SetScrollRegion(top - 1, bottom - 1);

        // Setting the region homes the cursor (respecting origin mode).
        State.CursorY = State.Modes.OriginMode ? Buffer.ScrollTop : 0;
        State.CursorX = 0;
        State.WrapPending = false;
    }

    // ============== Reports ==============

    private void DeviceStatusReport(int mode)
    {
        switch (mode)
        {
            case 5:
                Respond("\x1b[0n");
                break;  // terminal OK

            case 6:
                int row = State.Modes.OriginMode ? State.CursorY - Buffer.ScrollTop + 1 : State.CursorY + 1;
                int col = State.CursorX + 1;
                Respond($"\x1b[{row};{col}R");
                break;
        }
    }

    private void DeviceAttributes(char marker)
    {
        if (marker == '>')
            Respond("\x1b[>41;0;0c");   // DA2
        else
            Respond("\x1b[?62;0c");     // DA1 (claim a VT102-class terminal)
    }

    private void DecRequestParameters(int ps)
    {
        // DECREQTPARM response: parity present, no parity, lines, xmit, mult, flags.
        Respond($"\x1b[{(ps == 1 ? 2 : 3)};1;1;112;112;1;0x");
    }

    private void RequestSelectionOrSetting(int ps)
    {
        // DECRQSS: respond with the current value of the requested setting.
        switch (ps)
        {
            case 1: // SGR
                Respond($"\x1bP1$r{EncodeSgr()}\x1b\\");
                break;

            default:
                // Invalid request: respond with DCS 0 $ r ST.
                Respond("\x1bP0$r\x1b\\");
                break;
        }
    }

    // ============== Reset ==============

    private void FullReset()
    {
        // A full reset repositions the cursor anyway: discard any parked newline instead of applying it.
        _pendingReturn = false;
        _pendingLineFeeds = 0;

        Buffer.ClearAll();
        Buffer.ResetScrollRegion();
        State.Attributes = CharAttributes.Default;
        State.Modes = new TerminalModes();
        State.CursorX = 0;
        State.CursorY = 0;
        State.WrapPending = false;
        State.SavedAttributes = CharAttributes.Default;
        State.SavedCursorX = 0;
        State.SavedCursorY = 0;
        Buffer.ClearAllTabStops();
    }

    private void AlignmentTest()
    {
        // DECALN: fill the screen with 'E'.
        CellRenderStyle style = CharAttributes.Default.ToCellStyle(Options.Palette, Options.BoldAsBright);
        TerminalCellInfo cell = new TerminalCellInfo(new Rune('E'), style);

        for (int y = 0; y < Buffer.Rows; y++)
        {
            for (int x = 0; x < Buffer.Columns; x++)
                Buffer.GetCell(y, x) = cell;

            Buffer.MarkRowDirty(y);
        }
    }

    // ============== SGR ==============

    private void SetGraphicRendition(in CsiParams p)
    {
        if (p.IsEmpty)
        {
            State.Attributes.Reset();
            return;
        }

        ref CharAttributes a = ref State.Attributes;
        for (int i = 0; i < p.Length;)
        {
            int v = p[i];
            switch (v)
            {
                case 0:
                    a.Reset();
                    i++;
                    break;

                case 1:
                    a.Bold = true;
                    i++;
                    break;

                case 2:
                    a.Faint = true;
                    i++;
                    break;

                case 3:
                    a.Italic = true;
                    i++;
                    break;

                case 4:
                    {
                        if (p.IsSubParam(i + 1))
                        {
                            a.UnderlineStyle = ToUnderlineStyle(p[i + 1]);
                            i += 2;
                        }
                        else
                        {
                            a.UnderlineStyle = Underline.Single;
                            i++;
                        }

                        break;
                    }

                case 5:
                    a.Blink = Blink.Slow;
                    i++;
                    break;

                case 6:
                    a.Blink = Blink.Rapid;
                    i++;
                    break;

                case 7:
                    a.Inverse = true;
                    i++;
                    break;

                case 8:
                    a.Conceal = true;
                    i++;
                    break;

                case 9:
                    a.Strikethrough = true;
                    i++;
                    break;

                case 20:
                    a.Fraktur = true;
                    i++;
                    break;

                case 21:
                    a.UnderlineStyle = Underline.Double;
                    a.DoublyUnderlined = true;
                    i++;
                    break;

                case 22:
                    a.Bold = false;
                    a.Faint = false;
                    i++;
                    break;

                case 23:
                    a.Italic = false;
                    a.Fraktur = false;
                    i++;
                    break;

                case 24:
                    a.UnderlineStyle = Underline.None;
                    a.DoublyUnderlined = false;
                    i++;
                    break;

                case 25:
                    a.Blink = Blink.None;
                    i++;
                    break;

                case 27:
                    a.Inverse = false;
                    i++;
                    break;

                case 28:
                    a.Conceal = false;
                    i++;
                    break;

                case 29:
                    a.Strikethrough = false;
                    i++;
                    break;

                case 51:
                    a.Framed = true;
                    i++;
                    break;

                case 52:
                    a.Encircled = true;
                    i++;
                    break;

                case 53:
                    a.Overline = true;
                    i++;
                    break;

                case 54:
                    a.Framed = false;
                    a.Encircled = false;
                    i++;
                    break;

                case 55:
                    a.Overline = false;
                    i++;
                    break;

                case 39:
                    a.ForegroundSource = ColorSource.Default;
                    i++;
                    break;

                case 49:
                    a.BackgroundSource = ColorSource.Default;
                    i++;
                    break;

                case 59:
                    a.UnderlineSource = ColorSource.Default;
                    i++;
                    break;

                case 99:
                    a.ForegroundSource = ColorSource.Default;
                    i++;
                    break;

                case 109:
                    a.BackgroundSource = ColorSource.Default;
                    i++;
                    break;

                case >= 30 and <= 37:
                    a.ForegroundSource = ColorSource.Indexed;
                    a.ForegroundIndex = v - 30;
                    i++;
                    break;

                case >= 90 and <= 97:
                    a.ForegroundSource = ColorSource.Indexed;
                    a.ForegroundIndex = v - 90 + 8;
                    i++;
                    break;

                case >= 40 and <= 47:
                    a.BackgroundSource = ColorSource.Indexed;
                    a.BackgroundIndex = v - 40;
                    i++;
                    break;

                case >= 100 and <= 107:
                    a.BackgroundSource = ColorSource.Indexed;
                    a.BackgroundIndex = v - 100 + 8;
                    i++;
                    break;

                case 38:
                    i = ConsumeColor(p, i, ColorTarget.Foreground, ref a);
                    break;

                case 48:
                    i = ConsumeColor(p, i, ColorTarget.Background, ref a);
                    break;

                case 58:
                    i = ConsumeColor(p, i, ColorTarget.Underline, ref a);
                    break;

                default:
                    i++;
                    break;  // ignore unknown SGR
            }
        }
    }

    private static Underline ToUnderlineStyle(int code) => code switch
    {
        0 => Underline.None,
        1 => Underline.Single,
        2 => Underline.Double,
        3 => Underline.Curly,
        4 => Underline.Dotted,
        5 => Underline.Dashed,
        _ => Underline.Single,
    };

    private enum ColorTarget { Foreground, Background, Underline }

    private static int ConsumeColor(in CsiParams p, int i, ColorTarget target, ref CharAttributes a)
    {
        if (i + 1 >= p.Length)
            return i + 1;

        int spec = p[i + 1];
        if (spec == 5) // indexed
        {
            int idx = p.At(i + 2, 0);
            AssignIndexed(target, ref a, idx);
            return i + 3;
        }

        if (spec == 2) // direct RGB
        {
            int r, g, b, next;
            if (p.IsSubParam(i + 1))
            {
                // ':' form: 38:2:Pi:r:g:b (with colorspace) or 38:2:r:g:b (without).
                int start = i + 2;
                int end = start;
                while (end < p.Length && p.IsSubParam(end))
                    end++;
                int count = end - start;
                if (count >= 4) // colorspace id present
                {
                    r = p.At(start + 1, 0);
                    g = p.At(start + 2, 0);
                    b = p.At(start + 3, 0);
                }
                else
                {
                    r = p.At(start, 0);
                    g = p.At(start + 1, 0);
                    b = p.At(start + 2, 0);
                }

                next = end;
            }
            else
            {
                // ';' form: 38;2;r;g;b
                r = p.At(i + 2, 0);
                g = p.At(i + 3, 0);
                b = p.At(i + 4, 0);
                next = i + 5;
            }

            AssignDirect(target, ref a, TerminalColor.FromArgb(ClampByte(r), ClampByte(g), ClampByte(b)));
            return next;
        }

        return i + 2;
    }

    private static void AssignIndexed(ColorTarget target, ref CharAttributes a, int idx)
    {
        switch (target)
        {
            case ColorTarget.Foreground:
                a.ForegroundSource = ColorSource.Indexed;
                a.ForegroundIndex = idx;
                break;

            case ColorTarget.Background:
                a.BackgroundSource = ColorSource.Indexed;
                a.BackgroundIndex = idx;
                break;

            case ColorTarget.Underline:
                a.UnderlineSource = ColorSource.Indexed;
                a.UnderlineIndex = idx;
                break;
        }
    }

    private static void AssignDirect(ColorTarget target, ref CharAttributes a, TerminalColor color)
    {
        switch (target)
        {
            case ColorTarget.Foreground:
                a.ForegroundSource = ColorSource.Direct;
                a.ForegroundRgb = color;
                break;

            case ColorTarget.Background:
                a.BackgroundSource = ColorSource.Direct;
                a.BackgroundRgb = color;
                break;

            case ColorTarget.Underline:
                a.UnderlineSource = ColorSource.Direct;
                a.UnderlineRgb = color;
                break;
        }
    }

    private static byte ClampByte(int v) => (byte)Math.Clamp(v, 0, 255);

    // ============== SGR serialization (for DECRQSS) ==============

    private string EncodeSgr()
    {
        CharAttributes a = State.Attributes;
        List<int> p = [];

        if (a.Bold) p.Add(1);
        if (a.Faint) p.Add(2);
        if (a.Italic) p.Add(3);
        if (a.Fraktur) p.Add(20);

        switch (a.UnderlineStyle)
        {
            case Underline.Single:
                p.Add(4);
                break;
            case Underline.Double:
                p.Add(21);
                break;
            case Underline.Curly:
                p.Add(4);
                p.Add(3);
                break;
            case Underline.Dotted:
                p.Add(4);
                p.Add(4);
                break;
            case Underline.Dashed:
                p.Add(4);
                p.Add(5);
                break;
        }

        if (a.Blink == Blink.Slow) p.Add(5);
        if (a.Blink == Blink.Rapid) p.Add(6);
        if (a.Inverse) p.Add(7);
        if (a.Conceal) p.Add(8);
        if (a.Strikethrough) p.Add(9);
        if (a.Framed) p.Add(51);
        if (a.Encircled) p.Add(52);
        if (a.Overline) p.Add(53);
        if (a.DoublyUnderlined && a.UnderlineStyle != Underline.Double) p.Add(21);

        EncodeColor(p, ColorTarget.Foreground, a);
        EncodeColor(p, ColorTarget.Background, a);
        EncodeColor(p, ColorTarget.Underline, a);

        if (p.Count == 0)
            p.Add(0);

        return string.Join(";", p);
    }

    private static void EncodeColor(List<int> p, ColorTarget target, in CharAttributes a)
    {
        ColorSource source;
        int index;
        TerminalColor rgb;

        switch (target)
        {
            case ColorTarget.Foreground:
                source = a.ForegroundSource;
                index = a.ForegroundIndex;
                rgb = a.ForegroundRgb;
                break;
            case ColorTarget.Background:
                source = a.BackgroundSource;
                index = a.BackgroundIndex;
                rgb = a.BackgroundRgb;
                break;
            case ColorTarget.Underline:
                source = a.UnderlineSource;
                index = a.UnderlineIndex;
                rgb = a.UnderlineRgb;
                break;
            default:
                return;
        }

        int prefix = target switch
        {
            ColorTarget.Foreground => 38,
            ColorTarget.Background => 48,
            ColorTarget.Underline => 58,
            _ => 38,
        };

        switch (source)
        {
            case ColorSource.Default:
                p.Add(target == ColorTarget.Foreground ? 39
                    : target == ColorTarget.Background ? 49 : 59);
                break;

            case ColorSource.Indexed:
                // Use the compact ANSI forms for the 16 standard colors when possible.
                if (target == ColorTarget.Foreground)
                {
                    if (index < 8) { p.Add(30 + index); break; }
                    if (index < 16) { p.Add(90 + index - 8); break; }
                }
                else if (target == ColorTarget.Background)
                {
                    if (index < 8) { p.Add(40 + index); break; }
                    if (index < 16) { p.Add(100 + index - 8); break; }
                }

                p.Add(prefix);
                p.Add(5);
                p.Add(index);
                break;

            case ColorSource.Direct:
                p.Add(prefix);
                p.Add(2);
                p.Add(rgb.R);
                p.Add(rgb.G);
                p.Add(rgb.B);
                break;
        }
    }
}
