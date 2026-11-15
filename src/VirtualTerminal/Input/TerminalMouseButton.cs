namespace VirtualTerminal.Input;

/// <summary>
/// UI-agnostic mouse button identifiers used by <see cref="MouseEncoder"/>.
/// </summary>
public enum TerminalMouseButton
{
    /// <summary>
    /// No button.
    /// </summary>
    None,

    /// <summary>
    /// Left mouse button.
    /// </summary>
    Left,

    /// <summary>
    /// Middle mouse button.
    /// </summary>
    Middle,

    /// <summary>
    /// Right mouse button.
    /// </summary>
    Right,

    /// <summary>
    /// First extended mouse button.
    /// </summary>
    XButton1,

    /// <summary>
    /// Second extended mouse button.
    /// </summary>
    XButton2,
}
