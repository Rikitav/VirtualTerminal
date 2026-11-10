namespace VirtualTerminal.Enums;

/// <summary>
/// Cursor geometry shape according to DEC private mode DECSCUSR.
/// </summary>
public enum CursorShape : byte
{
    /// <summary>Full cell block cursor (DECSCUSR 0, 1, 2).</summary>
    Block = 0,

    /// <summary>Horizontal underline bar at the bottom of the cell (DECSCUSR 3, 4).</summary>
    Underline = 1,

    /// <summary>Vertical bar / I-Beam at the left of the cell (DECSCUSR 5, 6).</summary>
    Bar = 2,
}
