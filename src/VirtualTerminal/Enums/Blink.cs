namespace VirtualTerminal.Enums;

/// <summary>Blink attribute (SGR 5 slow / 6 rapid).</summary>
public enum Blink
{
    /// <summary>No blinking.</summary>
    /// <summary>
    /// Slow.
    /// </summary>
    None,
 /// <summary>
 /// Rapid.
 /// </summary>

    /// <summary>Slow blink (SGR 5).</summary>
    Slow,

    /// <summary>Rapid blink (SGR 6).</summary>
    Rapid,
}
