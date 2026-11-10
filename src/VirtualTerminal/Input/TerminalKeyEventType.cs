namespace VirtualTerminal.Input;

/// <summary>
/// Specifies the type of key event (press, repeat, release) used in Kitty Keyboard Protocol.
/// </summary>
public enum TerminalKeyEventType
{
    /// <summary>Key press event (value 1 in Kitty protocol).</summary>
    Press = 1,

    /// <summary>Key repeat event (value 2 in Kitty protocol).</summary>
    Repeat = 2,

    /// <summary>Key release event (value 3 in Kitty protocol).</summary>
    Release = 3,
}
