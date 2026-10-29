using System.Text;
using VirtualTerminal.Buffer;
using VirtualTerminal.Model;

namespace VirtualTerminal.Tests;

public class TerminalScreenBufferTests
{
    private static TerminalScreenBuffer CreateBuffer(int columns = 10, int rows = 5)
        => new(columns, rows);

    private static void SetCell(TerminalScreenBuffer buffer, int y, int x, char c)
        => buffer.GetCell(y, x) = new TerminalCellInfo(new Rune(c), CellRenderStyle.Default);

    [Fact]
    public void Constructor_ValidatesAndExposesDimensions()
    {
        using TerminalScreenBuffer buffer = new(0, 0);

        Assert.Equal(1, buffer.Columns);
        Assert.Equal(1, buffer.Rows);
        Assert.Equal(0, buffer.ScrollTop);
        Assert.Equal(0, buffer.ScrollBottom);
        Assert.False(buffer.IsAlternate);
    }

    [Fact]
    public void GetCell_AndGetRow_ProvideConsistentAccess()
    {
        using TerminalScreenBuffer buffer = CreateBuffer();

        SetCell(buffer, 0, 2, 'X');

        Assert.Equal('X', buffer.GetCell(0, 2).Character.Value);

        Span<TerminalCellInfo> row = buffer.GetRow(0);
        Assert.Equal('X', row[2].Character.Value);

        TerminalRow rowObject = buffer.GetRowObject(0);
        Assert.Equal('X', rowObject.Cells[2].Character.Value);
    }

    [Fact]
    public void ClearAll_BlanksEveryCellAndMarksDirty()
    {
        using TerminalScreenBuffer buffer = CreateBuffer();
        for (int i = 0; i < buffer.Rows; i++)
            buffer.MarkRowClean(i);

        SetCell(buffer, 0, 0, 'A');
        SetCell(buffer, buffer.Rows - 1, buffer.Columns - 1, 'Z');

        buffer.ClearAll();

        Assert.Equal(' ', buffer.GetCell(0, 0).Character.Value);
        Assert.Equal(' ', buffer.GetCell(buffer.Rows - 1, buffer.Columns - 1).Character.Value);
        Assert.True(buffer.HasDirtyRows);
    }

    [Fact]
    public void ClearCells_BlanksOnlyRequestedRange()
    {
        using TerminalScreenBuffer buffer = CreateBuffer();
        for (int x = 0; x < 5; x++)
            SetCell(buffer, 0, x, (char)('A' + x));

        buffer.MarkRowClean(0);
        buffer.ClearCells(0, 1, 2);

        Assert.Equal('A', buffer.GetCell(0, 0).Character.Value);
        Assert.Equal(' ', buffer.GetCell(0, 1).Character.Value);
        Assert.Equal(' ', buffer.GetCell(0, 2).Character.Value);
        Assert.Equal('D', buffer.GetCell(0, 3).Character.Value);
        Assert.True(buffer.IsRowDirty(0));
    }

    [Fact]
    public void DirtyTracking_MarkCleanAndDirty()
    {
        using TerminalScreenBuffer buffer = CreateBuffer();

        Assert.True(buffer.HasDirtyRows);

        for (int i = 0; i < buffer.Rows; i++)
            buffer.MarkRowClean(i);

        Assert.False(buffer.HasDirtyRows);
        Assert.False(buffer.IsRowDirty(2));

        buffer.MarkRowDirty(2);
        Assert.True(buffer.IsRowDirty(2));
        Assert.True(buffer.HasDirtyRows);

        buffer.MarkRowsDirty(1, 2);
        Assert.True(buffer.IsRowDirty(1));
        Assert.True(buffer.IsRowDirty(2));

        // Out-of-bounds calls are no-ops.
        buffer.MarkRowClean(-1);
        buffer.MarkRowClean(99);
    }

    [Fact]
    public void TabStops_DefaultsAndManipulation()
    {
        using TerminalScreenBuffer buffer = new(20, 5);

        Assert.False(buffer.IsTabStop(0));
        Assert.True(buffer.IsTabStop(8));
        Assert.True(buffer.IsTabStop(16));

        Assert.Equal(8, buffer.NextTabStop(0));
        Assert.Equal(16, buffer.NextTabStop(8));
        Assert.Equal(buffer.Columns - 1, buffer.NextTabStop(buffer.Columns - 1));

        Assert.Equal(8, buffer.PrevTabStop(9));
        Assert.Equal(0, buffer.PrevTabStop(1));

        buffer.SetTabStop(3);
        Assert.True(buffer.IsTabStop(3));

        buffer.ClearTabStop(8);
        Assert.False(buffer.IsTabStop(8));

        buffer.ClearAllTabStops();
        Assert.False(buffer.IsTabStop(3));
        Assert.False(buffer.IsTabStop(8));
    }

    [Fact]
    public void ScrollRegion_SetResetAndInvalidIgnored()
    {
        using TerminalScreenBuffer buffer = CreateBuffer(rows: 10);

        buffer.SetScrollRegion(2, 7);
        Assert.Equal(2, buffer.ScrollTop);
        Assert.Equal(7, buffer.ScrollBottom);

        // top >= bottom is invalid and ignored.
        buffer.SetScrollRegion(5, 5);
        Assert.Equal(2, buffer.ScrollTop);
        Assert.Equal(7, buffer.ScrollBottom);

        // Out-of-bounds values are clamped.
        buffer.SetScrollRegion(-5, 100);
        Assert.Equal(0, buffer.ScrollTop);
        Assert.Equal(9, buffer.ScrollBottom);

        buffer.ResetScrollRegion();
        Assert.Equal(0, buffer.ScrollTop);
        Assert.Equal(9, buffer.ScrollBottom);
    }

    [Fact]
    public void ScrollUp_Primary_MovesContentAndAddsToScrollback()
    {
        using TerminalScreenBuffer buffer = new(10, 5);
        for (int x = 0; x < 10; x++)
        {
            SetCell(buffer, 0, x, 'A');
            SetCell(buffer, 1, x, 'B');
            SetCell(buffer, 2, x, 'C');
        }

        buffer.ScrollUp(1);

        Assert.Equal('B', buffer.GetCell(0, 0).Character.Value);
        Assert.Equal('C', buffer.GetCell(1, 0).Character.Value);
        Assert.Equal(' ', buffer.GetCell(4, 0).Character.Value);
        Assert.Equal(1, buffer.ScrollbackCount);
        Assert.Equal('A', buffer.GetScrollback()[0].Cells[0].Character.Value);
    }

    [Fact]
    public void ScrollUp_Alternate_DoesNotAddScrollback()
    {
        using TerminalScreenBuffer buffer = new(10, 5);
        buffer.EnterAlternateScreen();

        for (int x = 0; x < 10; x++)
            SetCell(buffer, 0, x, 'A');

        buffer.ScrollUp(1);

        Assert.Equal(0, buffer.ScrollbackCount);
        Assert.Equal(' ', buffer.GetCell(4, 0).Character.Value);
    }

    [Fact]
    public void ScrollDown_ShiftsContentDownAndBlanksTop()
    {
        using TerminalScreenBuffer buffer = new(10, 5);
        for (int x = 0; x < 10; x++)
        {
            SetCell(buffer, 0, x, 'A');
            SetCell(buffer, 1, x, 'B');
            SetCell(buffer, 2, x, 'C');
        }

        buffer.SetScrollRegion(0, 2);
        buffer.ScrollDown(1);

        Assert.Equal(' ', buffer.GetCell(0, 0).Character.Value);
        Assert.Equal('A', buffer.GetCell(1, 0).Character.Value);
        Assert.Equal('B', buffer.GetCell(2, 0).Character.Value);
        Assert.True(buffer.IsRowDirty(0));
        Assert.True(buffer.IsRowDirty(2));
    }

    [Fact]
    public void InsertChars_ShiftsCellsRight()
    {
        using TerminalScreenBuffer buffer = CreateBuffer();
        for (int x = 0; x < 5; x++)
            SetCell(buffer, 0, x, (char)('A' + x));

        buffer.InsertChars(0, 1, 2);

        Assert.Equal('A', buffer.GetCell(0, 0).Character.Value);
        Assert.Equal(' ', buffer.GetCell(0, 1).Character.Value);
        Assert.Equal(' ', buffer.GetCell(0, 2).Character.Value);
        Assert.Equal('B', buffer.GetCell(0, 3).Character.Value);
        Assert.Equal('C', buffer.GetCell(0, 4).Character.Value);
    }

    [Fact]
    public void DeleteChars_ShiftsCellsLeftAndBlanksEdge()
    {
        using TerminalScreenBuffer buffer = CreateBuffer();
        for (int x = 0; x < 5; x++)
            SetCell(buffer, 0, x, (char)('A' + x));

        buffer.DeleteChars(0, 1, 2);

        Assert.Equal('A', buffer.GetCell(0, 0).Character.Value);
        Assert.Equal('D', buffer.GetCell(0, 1).Character.Value);
        Assert.Equal('E', buffer.GetCell(0, 2).Character.Value);
        Assert.Equal(' ', buffer.GetCell(0, 3).Character.Value);
        Assert.Equal(' ', buffer.GetCell(0, 4).Character.Value);
    }

    [Fact]
    public void EraseChars_ClearsWithoutShifting()
    {
        using TerminalScreenBuffer buffer = CreateBuffer();
        for (int x = 0; x < 5; x++)
            SetCell(buffer, 0, x, (char)('A' + x));

        buffer.EraseChars(0, 1, 2);

        Assert.Equal('A', buffer.GetCell(0, 0).Character.Value);
        Assert.Equal(' ', buffer.GetCell(0, 1).Character.Value);
        Assert.Equal(' ', buffer.GetCell(0, 2).Character.Value);
        Assert.Equal('D', buffer.GetCell(0, 3).Character.Value);
        Assert.Equal('E', buffer.GetCell(0, 4).Character.Value);
    }

    [Fact]
    public void InsertLines_AndDeleteLines_ScrollWithinBounds()
    {
        using TerminalScreenBuffer buffer = new(5, 5);
        for (int x = 0; x < 5; x++)
        {
            SetCell(buffer, 0, x, 'A');
            SetCell(buffer, 1, x, 'B');
            SetCell(buffer, 2, x, 'C');
        }

        buffer.InsertLines(1, 1, 3);
        Assert.Equal('A', buffer.GetCell(0, 0).Character.Value);
        Assert.Equal(' ', buffer.GetCell(1, 0).Character.Value);
        Assert.Equal('B', buffer.GetCell(2, 0).Character.Value);
        Assert.Equal('C', buffer.GetCell(3, 0).Character.Value);

        buffer.DeleteLines(1, 1, 3);
        Assert.Equal('B', buffer.GetCell(1, 0).Character.Value);
        Assert.Equal('C', buffer.GetCell(2, 0).Character.Value);
        Assert.Equal(' ', buffer.GetCell(3, 0).Character.Value);
    }

    [Fact]
    public void AlternateScreen_SwitchingPreservesPrimary()
    {
        using TerminalScreenBuffer buffer = CreateBuffer();

        SetCell(buffer, 0, 0, 'P');

        buffer.EnterAlternateScreen();
        Assert.True(buffer.IsAlternate);
        Assert.Equal(' ', buffer.GetCell(0, 0).Character.Value);

        SetCell(buffer, 0, 0, 'A');

        buffer.LeaveAlternateScreen();
        Assert.False(buffer.IsAlternate);
        Assert.Equal('P', buffer.GetCell(0, 0).Character.Value);
        Assert.True(buffer.IsRowDirty(0));
    }

    [Fact]
    public void Resize_GrowsGridPreservesContent()
    {
        using TerminalScreenBuffer buffer = new(5, 3);

        SetCell(buffer, 0, 0, 'A');
        SetCell(buffer, 2, 4, 'Z');

        buffer.Resize(8, 5);

        Assert.Equal(8, buffer.Columns);
        Assert.Equal(5, buffer.Rows);
        Assert.Equal('A', buffer.GetCell(0, 0).Character.Value);
        Assert.Equal('Z', buffer.GetCell(2, 4).Character.Value);
        Assert.Equal(' ', buffer.GetCell(4, 7).Character.Value);
    }

    [Fact]
    public void Resize_ShrinksGridPushesLostRowsToScrollback()
    {
        using TerminalScreenBuffer buffer = new(5, 5);

        for (int x = 0; x < 5; x++)
        {
            SetCell(buffer, 0, x, 'T');
            SetCell(buffer, 4, x, 'B');
        }

        TerminalState state = new() { CursorX = 2, CursorY = 4 };
        buffer.Resize(5, 3, state);

        Assert.Equal(5, buffer.Columns);
        Assert.Equal(3, buffer.Rows);
        Assert.Equal(2, state.CursorX);
        Assert.Equal(2, state.CursorY);
        Assert.Equal('B', buffer.GetCell(2, 0).Character.Value);
        Assert.Equal(2, buffer.ScrollbackCount);

        IReadOnlyList<TerminalRow> scrollback = buffer.GetScrollback();
        Assert.Equal('T', scrollback[0].Cells[0].Character.Value);
    }

    [Fact]
    public void SetScrollbackMax_TrimsExcess()
    {
        using TerminalScreenBuffer buffer = new(5, 3, scrollbackMax: 2);

        for (int x = 0; x < 5; x++)
            SetCell(buffer, 0, x, '1');
        buffer.ScrollUp(1);

        for (int x = 0; x < 5; x++)
            SetCell(buffer, 0, x, '2');
        buffer.ScrollUp(1);

        for (int x = 0; x < 5; x++)
            SetCell(buffer, 0, x, '3');
        buffer.ScrollUp(1);

        Assert.Equal(2, buffer.ScrollbackCount);

        IReadOnlyList<TerminalRow> scrollback = buffer.GetScrollback();
        Assert.Equal('2', scrollback[0].Cells[0].Character.Value);
        Assert.Equal('3', scrollback[1].Cells[0].Character.Value);
    }
}
