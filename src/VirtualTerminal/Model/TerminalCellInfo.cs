using System.Diagnostics;
using System.Text;

namespace VirtualTerminal.Model;

/// <summary>
/// A single screen cell: a Unicode codepoint plus its render style. Mutable so the decoder
/// can write it in place via <c>ref</c>. Equality covers both glyph and style so it can be
/// used both for full-cell comparison and (via <see cref="Style"/>) for glyph-run coalescing.
/// </summary>
[DebuggerDisplay("{'{Character.Value}'}")]
public struct TerminalCellInfo(Rune character, CellRenderStyle style) : IEquatable<TerminalCellInfo>
{
    /// <summary>The codepoint occupying this cell (space when blank).</summary>
    public Rune Character { get; set; } = character;

    /// <summary>Render attributes used for glyph-run coalescing and painting.</summary>
    public CellRenderStyle Style { get; set; } = style;

    /// <summary>A blank cell: space glyph with the default style.</summary>
    public static TerminalCellInfo Blank { get; } = new TerminalCellInfo(new Rune(' '), CellRenderStyle.Default);

    /// <summary>Resets the cell to a blank space while preserving nothing.</summary>
    public void Clear()
    {
        Character = new Rune(' ');
        Style = CellRenderStyle.Default;
    }

    /// <summary>Indicates whether the current cell is equal to another cell.</summary>
    /// <param name="other">The cell to compare.</param>
    /// <returns>true if the cells have the same character and style; otherwise false.</returns>
    public readonly bool Equals(TerminalCellInfo other)
        => Character == other.Character && Style == other.Style;

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj)
        => obj is TerminalCellInfo other && Equals(other);

    /// <inheritdoc/>
    public override readonly int GetHashCode()
        => HashCode.Combine(Character, Style);

    /// <summary>Determines whether two cells are equal.</summary>
    /// <param name="left">The first cell to compare.</param>
    /// <param name="right">The second cell to compare.</param>
    /// <returns>true if the cells are equal; otherwise false.</returns>
    public static bool operator ==(TerminalCellInfo left, TerminalCellInfo right)
        => left.Equals(right);

    /// <summary>Determines whether two cells are not equal.</summary>
    /// <param name="left">The first cell to compare.</param>
    /// <param name="right">The second cell to compare.</param>
    /// <returns>true if the cells are not equal; otherwise false.</returns>
    public static bool operator !=(TerminalCellInfo left, TerminalCellInfo right)
        => !left.Equals(right);
}
