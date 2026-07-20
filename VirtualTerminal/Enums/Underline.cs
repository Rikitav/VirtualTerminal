namespace VirtualTerminal.Enums;

/// <summary>Underline style for a cell (SGR 4 / 4:0..4 / 21).</summary>
public enum Underline
{
    /// <summary>No underline.</summary>
    /// <summary>
    /// Single.
    /// </summary>
    None,
 /// <summary>
 /// Double.
 /// </summary>

    /// <summary>Single straight underline.</summary>
    /// <summary>
    /// Dotted.
    /// </summary>
    Single,
 /// <summary>
 /// Dashed.
 /// </summary>

    /// <summary>Double straight underline.</summary>
    Double,

    /// <summary>Wavy / curly underline.</summary>
    Curly,

    /// <summary>Dotted underline.</summary>
    Dotted,

    /// <summary>Dashed underline.</summary>
    Dashed,
}
