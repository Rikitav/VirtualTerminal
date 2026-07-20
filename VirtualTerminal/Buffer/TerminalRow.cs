using VirtualTerminal.Model;

namespace VirtualTerminal.Buffer;

/// <summary>A single grid line: a cell array plus wrap/dirty metadata.</summary>
public sealed class TerminalRow
{
    /// <summary>The cell array for this row.</summary>
    public TerminalCellInfo[] Cells;

    /// <summary>Whether this row wraps onto the next line.</summary>
    public bool Wrapped;

    /// <summary>Whether this row has changed and needs to be redrawn.</summary>
    public bool Dirty = true;

    /// <summary>Initializes a new row with the specified number of blank columns.</summary>
    /// <param name="columns">The number of columns in the row.</param>
    public TerminalRow(int columns)
    {
        Cells = new TerminalCellInfo[columns];
        Clear();
    }

    /// <summary>Gets the number of cells in this row.</summary>
    public int Length => Cells.Length;

    /// <summary>Blanks every cell and clears the wrap flag.</summary>
    public void Clear()
    {
        Array.Fill(Cells, TerminalCellInfo.Blank);
        Wrapped = false;
        Dirty = true;
    }

    /// <summary>Blanks <paramref name="count"/> cells starting at <paramref name="start"/>.</summary>
    public void ClearRange(int start, int count)
    {
        int end = Math.Min(start + count, Cells.Length);
        for (int i = start; i < end; i++)
            Cells[i] = TerminalCellInfo.Blank;

        if (end > start)
            Dirty = true;
    }

    /// <summary>Returns the row cells as a span.</summary>
    /// <returns>A span over the row's cell array.</returns>
    public Span<TerminalCellInfo> AsSpan() => Cells;

    /// <summary>Gets a reference to the cell at the specified column.</summary>
    /// <param name="x">The zero-based column index.</param>
    /// <returns>A reference to the cell at column <paramref name="x"/>.</returns>
    public ref TerminalCellInfo this[int x] => ref Cells[x];

    /// <summary>Grows or shrinks the cell array, preserving the surviving cells.</summary>
    public void Resize(int newColumns)
    {
        if (newColumns == Cells.Length)
            return;

        int oldLength = Cells.Length;
        Array.Resize(ref Cells, newColumns);

        for (int i = oldLength; i < newColumns; i++)
            Cells[i] = TerminalCellInfo.Blank;

        Dirty = true;
    }
}
