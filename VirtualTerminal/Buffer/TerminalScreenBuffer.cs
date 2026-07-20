using System.Drawing;
using VirtualTerminal.Model;

namespace VirtualTerminal.Buffer;

/// <summary>
/// The terminal screen model: an active grid (primary or alternate), a scrollback ring fed only
/// by the primary screen, a scroll region, tab stops, and per-row dirty tracking. Mutation is
/// expected to happen on a single (UI) thread; the renderer reads dirty rows.
/// </summary>
public sealed class TerminalScreenBuffer : IDisposable
{
    private const int DefaultScrollbackMax = 10000;

    /// <summary>Lock object used to serialize buffer mutations and render reads.</summary>
    public Lock SyncRoot { get; } = new Lock();

    private TerminalRow[] _primary;
    private TerminalRow[]? _alt;
    private TerminalRow[] _active;
    private bool _isAlt;

    private int _columns;
    private int _rows;

    private int _scrollTop;
    private int _scrollBottom;

    private readonly Queue<TerminalRow> _scrollback = new();
    private int _scrollbackMax = DefaultScrollbackMax;

    private bool[] _tabStops;

    private bool _disposed;

    /// <summary>Gets the number of columns in the active screen.</summary>
    public int Columns => _columns;

    /// <summary>Gets the number of rows in the active screen.</summary>
    public int Rows => _rows;

    /// <summary>Gets the top row of the current scroll region (inclusive).</summary>
    public int ScrollTop => _scrollTop;

    /// <summary>Gets the bottom row of the current scroll region (inclusive).</summary>
    public int ScrollBottom => _scrollBottom;

    /// <summary>Gets a value indicating whether the alternate screen is active.</summary>
    public bool IsAlternate => _isAlt;

    /// <summary>Gets the number of rows currently stored in scrollback.</summary>
    public int ScrollbackCount => _scrollback.Count;

    /// <summary>Gets the maximum number of rows to retain in scrollback.</summary>
    public int ScrollbackMax => _scrollbackMax;

    /// <summary>Convenience size (columns, rows) for consumers expecting the legacy shape.</summary>
    public Size GridSize => new Size(_columns, _rows);

    /// <summary>Initializes a new screen buffer.</summary>
    /// <param name="columns">Number of columns.</param>
    /// <param name="rows">Number of rows.</param>
    /// <param name="scrollbackMax">Maximum scrollback rows to retain.</param>
    public TerminalScreenBuffer(int columns, int rows, int scrollbackMax = DefaultScrollbackMax)
    {
        _columns = Math.Max(1, columns);
        _rows = Math.Max(1, rows);
        _scrollbackMax = Math.Max(0, scrollbackMax);
        _primary = new TerminalRow[_rows];

        for (int i = 0; i < _rows; i++)
            _primary[i] = new TerminalRow(_columns);

        _active = _primary;
        _tabStops = new bool[_columns];

        ResetScrollRegion();
        InitDefaultTabStops();
    }

    /// <summary>Sets the maximum scrollback size and trims excess rows.</summary>
    /// <param name="max">The new maximum number of scrollback rows.</param>
    public void SetScrollbackMax(int max)
    {
        _scrollbackMax = Math.Max(0, max);
        while (_scrollback.Count > _scrollbackMax)
            _scrollback.Dequeue();
    }

    // ---- Row / cell access ----
    /// <summary>Returns the cells of row <paramref name="y"/> as a span.</summary>
    /// <param name="y">The zero-based row index.</param>
    /// <returns>A span over the row's cells.</returns>
    public Span<TerminalCellInfo> GetRow(int y)
        => _active[y].AsSpan();

    /// <summary>Returns the <see cref="TerminalRow"/> object at row <paramref name="y"/>.</summary>
    /// <param name="y">The zero-based row index.</param>
    /// <returns>The row object.</returns>
    public TerminalRow GetRowObject(int y)
        => _active[y];

    /// <summary>Returns a reference to the cell at (<paramref name="y"/>, <paramref name="x"/>).</summary>
    /// <param name="y">The zero-based row index.</param>
    /// <param name="x">The zero-based column index.</param>
    /// <returns>A reference to the requested cell.</returns>
    public ref TerminalCellInfo GetCell(int y, int x)
        => ref _active[y].Cells[x];

    // ---- Dirty tracking ----
    /// <summary>Gets a value indicating whether any row on the active screen is dirty.</summary>
    public bool HasDirtyRows
    {
        get
        {
            for (int i = 0; i < _rows; i++)
            {
                if (_active[i].Dirty)
                    return true;
            }

            return false;
        }
    }

    /// <summary>Determines whether row <paramref name="y"/> is dirty.</summary>
    /// <param name="y">The zero-based row index.</param>
    /// <returns><see langword="true"/> if the row is dirty; otherwise, <see langword="false"/>.</returns>
    public bool IsRowDirty(int y)
        => y >= 0 && y < _rows && _active[y].Dirty;

    /// <summary>Marks row <paramref name="y"/> dirty if it is within bounds.</summary>
    /// <param name="y">The zero-based row index.</param>
    public void MarkRowDirty(int y)
    {
        if (y >= 0 && y < _rows)
            _active[y].Dirty = true;
    }

    /// <summary>Marks a range of rows dirty.</summary>
    /// <param name="from">The starting row index.</param>
    /// <param name="count">The number of rows to mark.</param>
    public void MarkRowsDirty(int from, int count)
    {
        int end = Math.Min(from + count, _rows);
        for (int i = Math.Max(0, from); i < end; i++)
            _active[i].Dirty = true;
    }

    /// <summary>Marks row <paramref name="y"/> clean if it is within bounds.</summary>
    /// <param name="y">The zero-based row index.</param>
    public void MarkRowClean(int y)
    {
        if (y >= 0 && y < _rows)
            _active[y].Dirty = false;
    }

    /// <summary>Marks every row on the active screen dirty.</summary>
    public void MarkAllDirty()
    {
        for (int i = 0; i < _rows; i++)
            _active[i].Dirty = true;
    }

    // ---- Clearing ----
    /// <summary>Clears every cell on the active screen and marks all rows dirty.</summary>
    public void ClearAll()
    {
        for (int i = 0; i < _rows; i++)
            _active[i].Clear();
    }

    /// <summary>Clears rows in [<paramref name="from"/>, <paramref name="from"/>+count).</summary>
    public void ClearRows(int from, int count)
    {
        int end = Math.Min(from + count, _rows);
        for (int i = Math.Max(0, from); i < end; i++)
            _active[i].Clear();
    }

    /// <summary>Blanks <paramref name="count"/> cells in row <paramref name="y"/> from column <paramref name="x"/>.</summary>
    public void ClearCells(int y, int x, int count)
        => _active[y].ClearRange(x, count);

    // ---- Tab stops ----
    /// <summary>Determines whether a tab stop is set at column <paramref name="x"/>.</summary>
    /// <param name="x">The zero-based column index.</param>
    /// <returns><see langword="true"/> if a tab stop exists at the column; otherwise, <see langword="false"/>.</returns>
    public bool IsTabStop(int x)
        => x >= 0 && x < _tabStops.Length && _tabStops[x];

    /// <summary>Sets a tab stop at column <paramref name="x"/>.</summary>
    /// <param name="x">The zero-based column index.</param>
    public void SetTabStop(int x)
    {
        if (x >= 0 && x < _tabStops.Length)
            _tabStops[x] = true;
    }

    /// <summary>Clears the tab stop at column <paramref name="x"/>.</summary>
    /// <param name="x">The zero-based column index.</param>
    public void ClearTabStop(int x)
    {
        if (x >= 0 && x < _tabStops.Length)
            _tabStops[x] = false;
    }

    /// <summary>Clears all tab stops.</summary>
    public void ClearAllTabStops()
        => Array.Fill(_tabStops, false);

    private void InitDefaultTabStops()
    {
        Array.Fill(_tabStops, false);
        for (int x = 8; x < _tabStops.Length; x += 8)
            _tabStops[x] = true;
    }

    /// <summary>Next tab stop at or after <paramref name="x"/> (clamped to last column).</summary>
    public int NextTabStop(int x)
    {
        for (int i = x + 1; i < _tabStops.Length; i++)
        {
            if (_tabStops[i])
                return i;
        }

        return _tabStops.Length - 1;
    }

    /// <summary>Previous tab stop before <paramref name="x"/> (clamped to 0).</summary>
    public int PrevTabStop(int x)
    {
        for (int i = x - 1; i >= 0; i--)
        {
            if (_tabStops[i])
                return i;
        }

        return 0;
    }

    // ---- Scroll region ----
    /// <summary>Sets the scroll region to the specified inclusive row range.</summary>
    /// <param name="top">The top row of the region.</param>
    /// <param name="bottom">The bottom row of the region.</param>
    public void SetScrollRegion(int top, int bottom)
    {
        if (top < 0)
            top = 0;

        if (bottom >= _rows)
            bottom = _rows - 1;

        if (top >= bottom)
            return;  // invalid region; ignore (spec)

        _scrollTop = top;
        _scrollBottom = bottom;
    }

    /// <summary>Resets the scroll region to cover the entire screen.</summary>
    public void ResetScrollRegion()
    {
        _scrollTop = 0;
        _scrollBottom = _rows - 1;
    }

    // ---- Scrolling ----
    /// <summary>Scrolls the current scroll region up by <paramref name="n"/> (content moves up).</summary>
    public void ScrollUp(int n)
        => ScrollUp(_scrollTop, _scrollBottom, n);

    /// <summary>Scrolls rows in [<paramref name="top"/>, <paramref name="bottom"/>] up by <paramref name="n"/> lines.</summary>
    /// <param name="top">The top row of the region.</param>
    /// <param name="bottom">The bottom row of the region.</param>
    /// <param name="n">The number of lines to scroll.</param>
    public void ScrollUp(int top, int bottom, int n)
    {
        if (n <= 0 || top > bottom)
            return;

        int region = bottom - top + 1;
        if (n > region)
            n = region;

        TerminalRow[] leaving = new TerminalRow[n];
        for (int i = 0; i < n; i++)
            leaving[i] = _active[top + i];

        Array.Copy(_active, top + n, _active, top, region - n);
        for (int i = 0; i < n; i++)
        {
            if (!_isAlt && top == 0 && _scrollbackMax > 0)
            {
                _scrollback.Enqueue(leaving[i]);
                if (_scrollback.Count > _scrollbackMax)
                {
                    TerminalRow recycled = _scrollback.Dequeue();
                    recycled.Clear();
                    _active[bottom - n + 1 + i] = recycled;
                }
                else
                {
                    _active[bottom - n + 1 + i] = new TerminalRow(_columns);
                }
            }
            else
            {
                leaving[i].Clear();
                _active[bottom - n + 1 + i] = leaving[i];
            }
        }

        MarkAllDirty();
    }

    /// <summary>Scrolls the current scroll region down by <paramref name="n"/> (content moves down).</summary>
    public void ScrollDown(int n)
        => ScrollDown(_scrollTop, _scrollBottom, n);

    /// <summary>Scrolls rows in [<paramref name="top"/>, <paramref name="bottom"/>] down by <paramref name="n"/> lines.</summary>
    /// <param name="top">The top row of the region.</param>
    /// <param name="bottom">The bottom row of the region.</param>
    /// <param name="n">The number of lines to scroll.</param>
    public void ScrollDown(int top, int bottom, int n)
    {
        if (n <= 0 || top > bottom)
            return;

        int region = bottom - top + 1;
        if (n > region)
            n = region;

        TerminalRow[] leaving = new TerminalRow[n];
        for (int i = 0; i < n; i++)
            leaving[i] = _active[bottom - i];

        Array.Copy(_active, top, _active, top + n, region - n);
        for (int i = 0; i < n; i++)
        {
            leaving[i].Clear();
            _active[top + i] = leaving[i];
        }

        for (int i = top; i <= bottom; i++)
            _active[i].Dirty = true;

        MarkAllDirty();
    }

    /// <summary>Inserts <paramref name="n"/> blank lines at row <paramref name="y"/>, scrolling within [<paramref name="y"/>, <paramref name="bottom"/>].</summary>
    public void InsertLines(int y, int n, int bottom)
        => ScrollDown(y, bottom, n);

    /// <summary>Deletes <paramref name="n"/> lines at row <paramref name="y"/>, scrolling within [<paramref name="y"/>, <paramref name="bottom"/>].</summary>
    public void DeleteLines(int y, int n, int bottom)
        => ScrollUp(y, bottom, n);

    // ---- Per-row character insert/delete ----
    /// <summary>Inserts <paramref name="n"/> blank cells at (<paramref name="y"/>, <paramref name="x"/>), shifting existing cells right.</summary>
    /// <param name="y">The zero-based row index.</param>
    /// <param name="x">The zero-based column index.</param>
    /// <param name="n">The number of blank cells to insert.</param>
    public void InsertChars(int y, int x, int n)
    {
        if (n <= 0 || y < 0 || y >= _rows)
            return;

        ref TerminalCellInfo[] cells = ref _active[y].Cells;
        int len = cells.Length;
        if (x >= len)
            return;

        if (n > len - x)
            n = len - x;

        for (int i = len - 1; i >= x + n; i--)
            cells[i] = cells[i - n];

        for (int i = x; i < x + n; i++)
            cells[i] = TerminalCellInfo.Blank;

        _active[y].Dirty = true;
    }

    /// <summary>Deletes <paramref name="n"/> cells at (<paramref name="y"/>, <paramref name="x"/>), shifting remaining cells left and blanking the right edge.</summary>
    /// <param name="y">The zero-based row index.</param>
    /// <param name="x">The zero-based column index.</param>
    /// <param name="n">The number of cells to delete.</param>
    public void DeleteChars(int y, int x, int n)
    {
        if (n <= 0 || y < 0 || y >= _rows)
            return;

        ref TerminalCellInfo[] cells = ref _active[y].Cells;
        int len = cells.Length;
        if (x >= len)
            return;

        if (n > len - x)
            n = len - x;

        for (int i = x; i < len - n; i++)
            cells[i] = cells[i + n];

        for (int i = len - n; i < len; i++)
            cells[i] = TerminalCellInfo.Blank;

        _active[y].Dirty = true;
    }

    /// <summary>Erases <paramref name="n"/> characters starting at (<paramref name="y"/>, <paramref name="x"/>) without moving the cursor.</summary>
    public void EraseChars(int y, int x, int n) => _active[y].ClearRange(x, n);

    // ---- Alternate screen ----
    /// <summary>Switches to the alternate screen, clearing it and resetting the scroll region.</summary>
    public void EnterAlternateScreen()
    {
        if (_isAlt)
            return;

        _alt ??= new TerminalRow[_rows];
        if (_alt.Length != _rows)
            Array.Resize(ref _alt, _rows);

        for (int i = 0; i < _rows; i++)
        {
            _alt[i] ??= new TerminalRow(_columns);
            if (_alt[i].Length != _columns)
                _alt[i].Resize(_columns);

            _alt[i].Clear();
        }

        _active = _alt;
        _isAlt = true;
        ResetScrollRegion();
    }

    /// <summary>Returns to the primary screen and marks all rows dirty.</summary>
    public void LeaveAlternateScreen()
    {
        if (!_isAlt)
            return;

        _active = _primary;
        _isAlt = false;
        ResetScrollRegion();
        MarkAllDirty();
    }

    /// <summary>Clears every cell on the alternate screen if it is active.</summary>
    public void ClearAlternateScreen()
    {
        if (!_isAlt)
            return;

        for (int i = 0; i < _rows; i++)
            _active[i].Clear();
    }

    // ---- Resize ----
    /// <summary>Resizes the active grid to the specified dimensions, pushing dropped rows to scrollback.</summary>
    /// <param name="columns">The new number of columns.</param>
    /// <param name="rows">The new number of rows.</param>
    public void Resize(int columns, int rows)
        => Resize(columns, rows, state: null, pushScrollback: true);

    /// <summary>Resizes the active grid and adjusts the cursor state.</summary>
    /// <param name="columns">The new number of columns.</param>
    /// <param name="rows">The new number of rows.</param>
    /// <param name="state">The cursor state to update, or <see langword="null"/>.</param>
    public void Resize(int columns, int rows, TerminalState? state)
        => Resize(columns, rows, state, pushScrollback: true);

    /// <summary>Resizes the active grid and optionally adjusts the cursor state and scrollback.</summary>
    /// <param name="columns">The new number of columns.</param>
    /// <param name="rows">The new number of rows.</param>
    /// <param name="state">The cursor state to update, or <see langword="null"/>.</param>
    /// <param name="pushScrollback">Whether rows dropped from the top should be pushed into scrollback.</param>
    public void Resize(int columns, int rows, TerminalState? state, bool pushScrollback)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);

        int oldRows = _rows;
        int oldColumns = _columns;

        if (columns != _columns)
            Array.Resize(ref _tabStops, columns);

        ResizeGrid(ref _primary, columns, rows, pushScrollback && !_isAlt);
        if (_alt is not null)
            ResizeGrid(ref _alt, columns, rows, pushScrollback: false);

        _columns = columns;
        _rows = rows;
        _active = _isAlt ? _alt! : _primary;

        if (state is not null)
        {
            // Keep the cursor on the same logical line when the height shrinks (bottom-aligned),
            // and clamp it into the new bounds.
            if (rows < oldRows)
            {
                int lost = oldRows - rows;
                state.CursorY = Math.Max(0, state.CursorY - lost);
            }

            state.CursorX = Math.Min(state.CursorX, _columns - 1);
            state.CursorY = Math.Clamp(state.CursorY, 0, _rows - 1);
            state.WrapPending = false;
        }

        ResetScrollRegion();
        InitDefaultTabStops();
        MarkAllDirty();
    }

    private void ResizeGrid(ref TerminalRow[] grid, int columns, int rows, bool pushScrollback)
    {
        // Resize every surviving row to the new column count.
        for (int i = 0; i < grid.Length; i++)
            grid[i].Resize(columns);

        if (rows < grid.Length)
        {
            // Shrink: keep the bottom rows visible and push the top rows into scrollback.
            int lost = grid.Length - rows;
            if (pushScrollback && !_isAlt && _scrollbackMax > 0)
            {
                for (int i = 0; i < lost; i++)
                {
                    _scrollback.Enqueue(grid[i]);
                    while (_scrollback.Count > _scrollbackMax)
                        _scrollback.Dequeue();
                }
            }

            if (lost > 0)
                Array.Copy(grid, lost, grid, 0, rows);

            Array.Resize(ref grid, rows);
        }
        else if (rows > grid.Length)
        {
            int old = grid.Length;
            Array.Resize(ref grid, rows);

            for (int i = old; i < rows; i++)
                grid[i] = new TerminalRow(columns);
        }
    }

    /// <summary>Snapshot of the scrollback rows (oldest first). Used by the renderer for history.</summary>
    public IReadOnlyList<TerminalRow> GetScrollback() => Array.AsReadOnly(_scrollback.ToArray());

    /// <summary>Releases resources used by the screen buffer.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _scrollback.Clear();
        _disposed = true;
    }
}
