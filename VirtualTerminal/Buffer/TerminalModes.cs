namespace VirtualTerminal.Buffer;

/// <summary>DEC/ANSI mode flags tracked by the terminal state (DECSET/DECRST).</summary>
public sealed class TerminalModes
{
    /// <summary>DEC private ?1 — application cursor keys (arrows send SS3).</summary>
    public bool ApplicationCursorKeys;

    /// <summary>ANSI 4 (IRM) — insert mode: printing shifts cells right instead of overwriting.</summary>
    public bool InsertMode;

    /// <summary>DEC private ?6 (DECOM) — origin mode: cursor positioning is relative to the scroll region.</summary>
    public bool OriginMode;

    /// <summary>DEC private ?7 (DECAWM) — autowrap on reaching the right margin (default on).</summary>
    public bool AutoWrap = true;

    /// <summary>DEC private ?12 — cursor blink.</summary>
    public bool CursorBlink;

    /// <summary>DEC private ?25 (DECTCEM) — cursor visible (default on).</summary>
    public bool CursorVisible = true;

    /// <summary>DEC private ?5 (DECSCNM) — reverse video (screen-wide inverse).</summary>
    public bool ReverseVideo;

    /// <summary>DEC private ?66 — application keypad (DECKPAM).</summary>
    public bool ApplicationKeypad;

    /// <summary>DEC private ?67 — backspace sends 0x08 instead of 0x7F.</summary>
    public bool BackspaceSendsControlH;

    /// <summary>ANSI 20 (LNM) — line feed/new line mode: Enter sends CR+LF, LF does CR+LF.</summary>
    public bool LineFeedNewLine = true;

    /// <summary>DEC private ?2004 — bracketed paste.</summary>
    public bool BracketedPaste;

    /// <summary>DEC private ?1004 — focus reporting.</summary>
    public bool FocusReporting;

    /// <summary>Mouse reporting enabled at all (any of ?9/?1000/?1002/?1003).</summary>
    public MouseTrackingMode MouseTracking = MouseTrackingMode.Off;

    /// <summary>DEC private ?1006 — SGR mouse encoding.</summary>
    public bool SgrMouseEncoding;

    /// <summary>DEC private ?1049 cursor-shape (DECSCUSR) last set value, for restore.</summary>
    public CursorShape CursorShape = CursorShape.Block;

    /// <summary>Whether the cursor blinks (DECSCUSR 1/2/3) vs steady (0/4).</summary>
    public bool CursorBlinking;

    /// <summary>Creates a copy of the current mode state.</summary>
    /// <returns>A new <see cref="TerminalModes"/> instance with the same flag values.</returns>
    public TerminalModes Clone() => new()
    {
        ApplicationCursorKeys = ApplicationCursorKeys,
        InsertMode = InsertMode,
        OriginMode = OriginMode,
        AutoWrap = AutoWrap,
        CursorBlink = CursorBlink,
        CursorVisible = CursorVisible,
        ReverseVideo = ReverseVideo,
        ApplicationKeypad = ApplicationKeypad,
        BackspaceSendsControlH = BackspaceSendsControlH,
        LineFeedNewLine = LineFeedNewLine,
        BracketedPaste = BracketedPaste,
        FocusReporting = FocusReporting,
        MouseTracking = MouseTracking,
        SgrMouseEncoding = SgrMouseEncoding,
        CursorShape = CursorShape,
        CursorBlinking = CursorBlinking,
    };
}

/// <summary>Mouse tracking mode (off / X10 click / button-event / any-event motion).</summary>
public enum MouseTrackingMode
{
    /// <summary>No mouse reporting.</summary>
    Off,

    /// <summary>Legacy X11/X10 mode: report only button presses with coordinates.</summary>
    X10,

    /// <summary>Normal tracking: report button press and release events.</summary>
    ButtonEvent,

    /// <summary>Any-event tracking: report press, release, and motion events.</summary>
    AnyEvent,
}

/// <summary>Cursor shape (DECSCUSR).</summary>
public enum CursorShape
{
    /// <summary>Solid block cursor.</summary>
    Block,

    /// <summary>Horizontal underline cursor.</summary>
    Underline,

    /// <summary>Vertical bar cursor.</summary>
    Bar,
}
