namespace VirtualTerminal.Input;

/// <summary>
/// Progressive keyboard enhancement flags defined by the Kitty Keyboard Protocol.
/// </summary>
[Flags]
public enum KittyKeyboardFlags
{
    /// <summary>No enhancements active (standard legacy encoding).</summary>
    None = 0,

    /// <summary>
    /// Flag 1 (1 &lt;&lt; 0): Disambiguate escape codes.
    /// Emits escape codes for keys that normally have ambiguous legacy encodings
    /// (e.g. Shift+Enter, Tab vs Ctrl+I, Enter vs Ctrl+M, Escape, Backspace).
    /// </summary>
    DisambiguateEscapeCodes = 1 << 0,

    /// <summary>
    /// Flag 2 (1 &lt;&lt; 1): Report key event types (press, repeat, release).
    /// </summary>
    ReportEventTypes = 1 << 1,

    /// <summary>
    /// Flag 4 (1 &lt;&lt; 2): Report alternate key codes (e.g. shifted key character).
    /// </summary>
    ReportAlternateKeys = 1 << 2,

    /// <summary>
    /// Flag 8 (1 &lt;&lt; 3): Report all keys as escape codes (even plain text characters).
    /// </summary>
    ReportAllKeysAsEscapeCodes = 1 << 3,

    /// <summary>
    /// Flag 16 (1 &lt;&lt; 4): Report associated text with key events.
    /// </summary>
    ReportAssociatedText = 1 << 4,
}
