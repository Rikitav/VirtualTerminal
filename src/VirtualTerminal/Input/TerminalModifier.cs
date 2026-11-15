namespace VirtualTerminal.Input;

/// <summary>
/// UI-agnostic modifier key flags used by <see cref="KeyboardEncoder"/> and <see cref="MouseEncoder"/>.
/// </summary>
[Flags]
public enum TerminalModifier
{
    /// <summary>
    /// No modifier held.
    /// </summary>
    None = 0,

    /// <summary>
    /// Shift key.
    /// </summary>
    Shift = 1,

    /// <summary>
    /// Alt key.
    /// </summary>
    Alt = 2,

    /// <summary>
    /// Control key.
    /// </summary>
    Control = 4,

    /// <summary>
    /// Meta / Windows / Command key.
    /// </summary>
    Meta = 8,
}
