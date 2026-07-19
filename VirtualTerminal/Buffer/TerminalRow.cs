using System;
using VirtualTerminal.Model;

namespace VirtualTerminal.Buffer;

/// <summary>A single grid line: a cell array plus wrap/dirty metadata.</summary>
public sealed class TerminalRow
{
    public TerminalCellInfo[] Cells;
    public bool Wrapped;
    public bool Dirty = true;

    public TerminalRow(int columns)
    {
        Cells = new TerminalCellInfo[columns];
        Clear();
    }

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

    public Span<TerminalCellInfo> AsSpan() => Cells;

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
