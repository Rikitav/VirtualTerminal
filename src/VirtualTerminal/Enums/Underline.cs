namespace VirtualTerminal.Enums;

/// <summary>Underline style for a cell (SGR 4 / 4:0..4 / 21).</summary>
public enum Underline : byte
{
    /// <summary>No underline.</summary>
    None,

    /// <summary>Single straight underline.</summary>
    Single,

    /// <summary>Double straight underline.</summary>
    Double,

    /// <summary>Wavy / curly underline.</summary>
    Curly,

    /// <summary>Dotted underline.</summary>
    Dotted,

    /// <summary>Dashed underline.</summary>
    Dashed,
}
