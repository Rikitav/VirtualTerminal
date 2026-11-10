using VirtualTerminal.Enums;
using VirtualTerminal.Input;

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

    /// <summary>DEC private ?12 — cursor blink (default on matching DECSCUSR 0/1).</summary>
    public bool CursorBlink = true;

    /// <summary>DEC private ?25 (DECTCEM) — cursor visible (default on).</summary>
    public bool CursorVisible = true;

    /// <summary>DECSCUSR cursor shape (Block, Underline, Bar).</summary>
    public CursorShape CursorShape = CursorShape.Block;

    /// <summary>DEC private ?5 (DECSCNM) — reverse video (screen-wide inverse).</summary>
    public bool ReverseVideo;

    /// <summary>DEC private ?66 — application keypad (DECKPAM).</summary>
    public bool ApplicationKeypad;

    /// <summary>DEC private ?67 — backspace sends 0x08 instead of 0x7F.</summary>
    public bool BackspaceSendsControlH;

    /// <summary>DEC private ?69 (DECLRMM) — left and right margin mode.</summary>
    public bool LeftRightMarginMode;

    /// <summary>ANSI 20 (LNM) — line feed/new line mode: Enter sends CR+LF, LF does CR+LF.</summary>
    public bool LineFeedNewLine = true;

    /// <summary>DEC private ?2004 — bracketed paste.</summary>
    public bool BracketedPaste;

    /// <summary>DEC private ?2026 — synchronized output (BSU/ESU).</summary>
    public bool SynchronizedOutput;

    /// <summary>DEC private ?1004 — focus reporting.</summary>
    public bool FocusReporting;

    /// <summary>Mouse tracking mode (off / X10 click / button-event / any-event motion).</summary>
    public MouseTrackingMode MouseTracking = MouseTrackingMode.Off;

    /// <summary>DEC private ?1006 — SGR mouse encoding.</summary>
    public bool SgrMouseEncoding;

    /// <summary>Kitty Keyboard Protocol progressive enhancement flags.</summary>
    public KittyKeyboardFlags KittyKeyboardFlags = KittyKeyboardFlags.None;

    /// <summary>Kitty Keyboard Protocol flag stack for push/pop operations.</summary>
    public Stack<KittyKeyboardFlags> KittyKeyboardStack { get; } = new();

    /// <summary>Creates a copy of the current mode state.</summary>
    /// <returns>A new <see cref="TerminalModes"/> instance with the same flag values.</returns>
    public TerminalModes Clone()
    {
        var clone = new TerminalModes
        {
            ApplicationCursorKeys = ApplicationCursorKeys,
            InsertMode = InsertMode,
            OriginMode = OriginMode,
            AutoWrap = AutoWrap,
            CursorBlink = CursorBlink,
            CursorVisible = CursorVisible,
            CursorShape = CursorShape,
            ReverseVideo = ReverseVideo,
            ApplicationKeypad = ApplicationKeypad,
            BackspaceSendsControlH = BackspaceSendsControlH,
            LeftRightMarginMode = LeftRightMarginMode,
            LineFeedNewLine = LineFeedNewLine,
            BracketedPaste = BracketedPaste,
            SynchronizedOutput = SynchronizedOutput,
            FocusReporting = FocusReporting,
            MouseTracking = MouseTracking,
            SgrMouseEncoding = SgrMouseEncoding,
            KittyKeyboardFlags = KittyKeyboardFlags,
        };

        foreach (var flag in KittyKeyboardStack.Reverse())
            clone.KittyKeyboardStack.Push(flag);

        return clone;
    }
}

/// <summary>Mouse tracking mode (off / X10 click / button-event / any-event motion).</summary>
public enum MouseTrackingMode
{
    /// <summary>No mouse tracking.</summary>
    Off = 0,
    /// <summary>X10 compatibility: button press only (?9).</summary>
    X10 = 1,
    /// <summary>Normal tracking: button press and release (?1000).</summary>
    ButtonPressRelease = 2,
    /// <summary>Highlight tracking (?1001).</summary>
    Highlight = 3,
    /// <summary>Button-event tracking: button press, release, and drag (?1002).</summary>
    ButtonEvent = 4,
    /// <summary>Any-event tracking: all motion and buttons (?1003).</summary>
    AnyEvent = 5,
}
