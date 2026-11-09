using System.Text;
using VirtualTerminal.Buffer;
using VirtualTerminal.Model;

namespace VirtualTerminal.Tests;

public class TerminalScreenBufferTests
{
    private static void SetCell(TerminalScreenBuffer buffer, int y, int x, char c)
    {
        buffer.GetCell(y, x) = new TerminalCellInfo(new Rune(c), CellRenderStyle.Default);
        buffer.MarkRowDirty(y);
    }

    [Fact]
    public void Construction_InitializesDimensionsAndBlanks()
    {
        using TerminalScreenBuffer buffer = new(80, 24);

        Assert.Equal(80, buffer.Columns);
        Assert.Equal(24, buffer.Rows);
        Assert.Equal(0, buffer.ScrollTop);
        Assert.Equal(23, buffer.ScrollBottom);
        Assert.False(buffer.IsAlternate);
        Assert.Equal(0, buffer.ScrollbackCount);

        for (int y = 0; y < 24; y++)
        {
            Span<TerminalCellInfo> row = buffer.GetRow(y);
            Assert.Equal(80, row.Length);
            foreach (ref readonly TerminalCellInfo cell in row)
            {
                Assert.Equal(' ', cell.Character.Value);
            }
        }
    }

    [Fact]
    public void DirtyTracking_FlagsMarkedRows()
    {
        using TerminalScreenBuffer buffer = new(10, 5);

        // Initially clean/dirty based on construction (construction marks dirty via clear).
        buffer.MarkRowClean(0);
        buffer.MarkRowClean(1);
        buffer.MarkRowClean(2);
        buffer.MarkRowClean(3);
        buffer.MarkRowClean(4);
        Assert.False(buffer.HasDirtyRows);

        buffer.MarkRowDirty(2);
        Assert.True(buffer.HasDirtyRows);
        Assert.True(buffer.IsRowDirty(2));
        Assert.False(buffer.IsRowDirty(0));

        buffer.MarkRowClean(2);
        Assert.False(buffer.HasDirtyRows);
    }

    [Fact]
    public void ClearRange_BlanksTargetCellsOnly()
    {
        using TerminalScreenBuffer buffer = new(10, 5);
        for (int x = 0; x < 10; x++)
            SetCell(buffer, 0, x, (char)('0' + x));

        buffer.ClearCells(0, 3, 4); // blank columns 3..6

        Span<TerminalCellInfo> row = buffer.GetRow(0);
        Assert.Equal('0', row[0].Character.Value);
        Assert.Equal('1', row[1].Character.Value);
        Assert.Equal('2', row[2].Character.Value);
        Assert.Equal(' ', row[3].Character.Value);
        Assert.Equal(' ', row[4].Character.Value);
        Assert.Equal(' ', row[5].Character.Value);
        Assert.Equal(' ', row[6].Character.Value);
        Assert.Equal('7', row[7].Character.Value);
    }

    [Fact]
    public void TabStops_DefaultEvery8Columns()
    {
        using TerminalScreenBuffer buffer = new(80, 24);

        Assert.False(buffer.IsTabStop(0));
        Assert.True(buffer.IsTabStop(8));
        Assert.True(buffer.IsTabStop(16));
        Assert.True(buffer.IsTabStop(24));
        Assert.False(buffer.IsTabStop(5));

        Assert.Equal(8, buffer.NextTabStop(0));
        Assert.Equal(8, buffer.NextTabStop(5));
        Assert.Equal(16, buffer.NextTabStop(8));
        Assert.Equal(0, buffer.PrevTabStop(5));
        Assert.Equal(8, buffer.PrevTabStop(10));
    }

    [Fact]
    public void SetAndClearTabStop_MutatesStops()
    {
        using TerminalScreenBuffer buffer = new(80, 24);

        buffer.SetTabStop(5);
        Assert.True(buffer.IsTabStop(5));
        Assert.Equal(5, buffer.NextTabStop(3));

        buffer.ClearTabStop(5);
        Assert.False(buffer.IsTabStop(5));
        Assert.Equal(8, buffer.NextTabStop(3));

        buffer.ClearAllTabStops();
        Assert.False(buffer.IsTabStop(8));
        Assert.Equal(79, buffer.NextTabStop(0)); // clamps to last col
        Assert.Equal(0, buffer.PrevTabStop(50)); // clamps to 0
    }

    [Fact]
    public void InsertChars_ShiftsCellsRightAndBlanks()
    {
        using TerminalScreenBuffer buffer = new(6, 2);
        for (int x = 0; x < 6; x++)
            SetCell(buffer, 0, x, (char)('A' + x));

        // Row 0 is ABCDEF. Insert 2 at x=2 -> AB  CD (E, F pushed off)
        buffer.InsertChars(0, 2, 2);

        Span<TerminalCellInfo> row = buffer.GetRow(0);
        Assert.Equal('A', row[0].Character.Value);
        Assert.Equal('B', row[1].Character.Value);
        Assert.Equal(' ', row[2].Character.Value);
        Assert.Equal(' ', row[3].Character.Value);
        Assert.Equal('C', row[4].Character.Value);
        Assert.Equal('D', row[5].Character.Value);
    }

    [Fact]
    public void DeleteChars_ShiftsCellsLeftAndBlanksRight()
    {
        using TerminalScreenBuffer buffer = new(6, 2);
        for (int x = 0; x < 6; x++)
            SetCell(buffer, 0, x, (char)('A' + x));

        // Row 0 is ABCDEF. Delete 2 at x=1 -> ADEF  (BC deleted)
        buffer.DeleteChars(0, 1, 2);

        Span<TerminalCellInfo> row = buffer.GetRow(0);
        Assert.Equal('A', row[0].Character.Value);
        Assert.Equal('D', row[1].Character.Value);
        Assert.Equal('E', row[2].Character.Value);
        Assert.Equal('F', row[3].Character.Value);
        Assert.Equal(' ', row[4].Character.Value);
        Assert.Equal(' ', row[5].Character.Value);
    }

    [Fact]
    public void AlternateScreen_SwapClearsAndLeavesRestores()
    {
        using TerminalScreenBuffer buffer = new(10, 5);
        SetCell(buffer, 0, 0, 'P');

        buffer.EnterAlternateScreen();
        Assert.True(buffer.IsAlternate);
        Assert.Equal(' ', buffer.GetCell(0, 0).Character.Value);

        SetCell(buffer, 0, 0, 'A');
        Assert.Equal('A', buffer.GetCell(0, 0).Character.Value);

        buffer.LeaveAlternateScreen();
        Assert.False(buffer.IsAlternate);
        Assert.Equal('P', buffer.GetCell(0, 0).Character.Value);
    }

    [Fact]
    public void SetScrollRegion_ClampsBounds()
    {
        using TerminalScreenBuffer buffer = new(10, 10);

        buffer.SetScrollRegion(2, 7);
        Assert.Equal(2, buffer.ScrollTop);
        Assert.Equal(7, buffer.ScrollBottom);

        // Invalid: top >= bottom -> ignored.
        buffer.SetScrollRegion(8, 3);
        Assert.Equal(2, buffer.ScrollTop);
        Assert.Equal(7, buffer.ScrollBottom);

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
    }

    [Fact]
    public void GetVisibleText_TrimsTrailingBlanksPerLine()
    {
        using TerminalScreenBuffer buffer = new(5, 3);
        SetCell(buffer, 0, 0, 'H');
        SetCell(buffer, 0, 1, 'i');
        SetCell(buffer, 1, 0, 'X');

        string text = buffer.GetVisibleText();
        Assert.Equal("Hi\nX", text);
    }

    [Fact]
    public void Resize_GrowsGrid_PreservesExistingCells()
    {
        using TerminalScreenBuffer buffer = new(5, 3);
        SetCell(buffer, 0, 0, 'A');
        SetCell(buffer, 2, 4, 'Z');

        buffer.Resize(10, 6);

        Assert.Equal(10, buffer.Columns);
        Assert.Equal(6, buffer.Rows);
        Assert.Equal('A', buffer.GetCell(0, 0).Character.Value);
        Assert.Equal('Z', buffer.GetCell(2, 4).Character.Value);
        Assert.Equal(' ', buffer.GetCell(5, 9).Character.Value);
    }

    [Fact]
    public void Resize_ShrinksGrid_PushesLostRowsToScrollbackAndAdjustsCursor()
    {
        using TerminalScreenBuffer buffer = new(5, 5);
        SetCell(buffer, 0, 0, 'T'); // top row
        SetCell(buffer, 1, 0, 'U');
        SetCell(buffer, 2, 0, 'V');
        SetCell(buffer, 3, 0, 'A');
        SetCell(buffer, 4, 0, 'B');

        TerminalState state = new()
        {
            CursorX = 2,
            CursorY = 4, // bottom row
        };

        // Shrink from 5 to 3 rows -> drops 2 from top ('T', 'U' go to scrollback).
        buffer.Resize(5, 3, state, pushScrollback: true);

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

    [Fact]
    public void ScrollUp_WithFullScrollback_RecyclesOldestRowWithoutAllocations()
    {
        using TerminalScreenBuffer buffer = new(5, 2, scrollbackMax: 2);

        SetCell(buffer, 0, 0, '1');
        SetCell(buffer, 1, 0, '2');
        buffer.ScrollUp(1); // '1' enters scrollback

        SetCell(buffer, 1, 0, '3');
        buffer.ScrollUp(1); // '2' enters scrollback, capacity (2) reached

        SetCell(buffer, 1, 0, '4');
        buffer.ScrollUp(1); // '1' recycled to become row at bottom

        Assert.Equal(2, buffer.ScrollbackCount);
        IReadOnlyList<TerminalRow> scrollback = buffer.GetScrollback();
        Assert.Equal('2', scrollback[0].Cells[0].Character.Value);
        Assert.Equal('3', scrollback[1].Cells[0].Character.Value);
        Assert.Equal('4', buffer.GetCell(0, 0).Character.Value);
        Assert.Equal(' ', buffer.GetCell(1, 0).Character.Value); // newly cleared recycled row
    }

    [Fact]
    public void ScrollDown_MultiLine_PreservesRegionBounds()
    {
        using TerminalScreenBuffer buffer = new(5, 6);
        for (int y = 0; y < 6; y++)
            SetCell(buffer, y, 0, (char)('A' + y));

        buffer.SetScrollRegion(1, 4);
        buffer.ScrollDown(1, 4, 2); // scroll rows 1..4 down by 2

        Assert.Equal('A', buffer.GetCell(0, 0).Character.Value); // outside region
        Assert.Equal(' ', buffer.GetCell(1, 0).Character.Value); // blanked
        Assert.Equal(' ', buffer.GetCell(2, 0).Character.Value); // blanked
        Assert.Equal('B', buffer.GetCell(3, 0).Character.Value); // shifted down
        Assert.Equal('C', buffer.GetCell(4, 0).Character.Value); // shifted down
        Assert.Equal('F', buffer.GetCell(5, 0).Character.Value); // outside region
    }
}
