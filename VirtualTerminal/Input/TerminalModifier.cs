namespace VirtualTerminal.Input;

/// <summary>
/// UI-agnostic modifier key flags used by <see cref="KeyboardEncoder"/> and <see cref="MouseEncoder"/>.
/// </summary>
[Flags]
public enum TerminalModifier
{
    None = 0,
    Shift = 1,
    Alt = 2,
    Control = 4,
    Meta = 8,
}
