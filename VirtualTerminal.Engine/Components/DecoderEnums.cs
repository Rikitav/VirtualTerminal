namespace VirtualTerminal.Engine.Components;

public enum Direction
{
    Up,
    Down,
    Forward,
    Backward
}

public enum ClearDirection
{
    Forward = 0,
    Backward = 1,
    Both = 2
}

public enum GraphicRendition
{
    Reset = 0,                  // all attributes off
    Bold = 1,                   // all attributes off
    Faint = 2,                  // Intensity: Faint. not widely supported
    Italic = 3,                 // Italic: On. not widely supported. Sometimes treated as inverse.
    Underline = 4,              // Underline: Single. not widely supported
    BlinkSlow = 5,              // Blink: Slow. less than 150 per minute
    BlinkRapid = 6,             // Blink: Rapid. MS-DOS ANSI.SYS; 150 per minute or more
    Inverse = 7,                // Image: Negative. inverse or reverse; swap foreground and background
    Conceal = 8,                // Conceal, not widely supported
    Font1 = 10,                 // Font selection (not sure which)
    UnderlineDouble = 21,       // Underline: Double
    NormalIntensity = 22,       // Intensity: Normal, not bold and not faint
    NoUnderline = 24,           // Underline: None  
    NoBlink = 25,               // Blink: off
    Positive = 27,              // Image: Positive. Not sure what this is supposed to be, the opposite of inverse???
    Reveal = 28,                // Reveal, conceal off

    // Set foreground color, normal intensity
    ForegroundNormalBlack = 30,
    ForegroundNormalRed = 31,
    ForegroundNormalGreen = 32,
    ForegroundNormalYellow = 33,
    ForegroundNormalBlue = 34,
    ForegroundNormalMagenta = 35,
    ForegroundNormalCyan = 36,
    ForegroundNormalWhite = 37,
    ForegroundNormalReset = 39,

    // Set background color, normal intensity
    BackgroundNormalBlack = 40,
    BackgroundNormalRed = 41,
    BackgroundNormalGreen = 42,
    BackgroundNormalYellow = 43,
    BackgroundNormalBlue = 44,
    BackgroundNormalMagenta = 45,
    BackgroundNormalCyan = 46,
    BackgroundNormalWhite = 47,
    BackgroundNormalReset = 49,

    // Set foreground color, high intensity (aixtem)
    ForegroundBrightBlack = 90,
    ForegroundBrightRed = 91,
    ForegroundBrightGreen = 92,
    ForegroundBrightYellow = 93,
    ForegroundBrightBlue = 94,
    ForegroundBrightMagenta = 95,
    ForegroundBrightCyan = 96,
    ForegroundBrightWhite = 97,
    ForegroundBrightReset = 99,

    // Set background color, high intensity (aixterm)
    BackgroundBrightBlack = 100,
    BackgroundBrightRed = 101,
    BackgroundBrightGreen = 102,
    BackgroundBrightYellow = 103,
    BackgroundBrightBlue = 104,
    BackgroundBrightMagenta = 105,
    BackgroundBrightCyan = 106,
    BackgroundBrightWhite = 107,
    BackgroundBrightReset = 109,
}

public enum AnsiMode
{
    ShowCursor,
    HideCursor,
    LineFeed,
    NewLine,
    CursorKeyToCursor,
    CursorKeyToApplication,
    ANSI,
    VT52,
    Columns80,
    Columns132,
    JumpScrolling,
    SmoothScrolling,
    NormalVideo,
    ReverseVideo,
    OriginIsAbsolute,
    OriginIsRelative,
    LineWrap,
    DisableLineWrap,
    AutoRepeat,
    DisableAutoRepeat,
    Interlacing,
    DisableInterlacing,
    NumericKeypad,
    AlternateKeypad,
}
