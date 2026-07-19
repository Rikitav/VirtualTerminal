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

    public int Columns => _columns;
    public int Rows => _rows;
    public int ScrollTop => _scrollTop;
    public int ScrollBottom => _scrollBottom;
    public bool IsAlternate => _isAlt;
    public int ScrollbackCount => _scrollback.Count;
    public int ScrollbackMax => _scrollbackMax;

    /// <summary>Convenience size (columns, rows) for consumers expecting the legacy shape.</summary>
    public Size GridSize => new Size(_columns, _rows);

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

    public void SetScrollbackMax(int max)
    {
        _scrollbackMax = Math.Max(0, max);
        while (_scrollback.Count > _scrollbackMax)
            _scrollback.Dequeue();
    }

    // ---- Row / cell access ----
    public Span<TerminalCellInfo> GetRow(int y)
        => _active[y].AsSpan();

    public TerminalRow GetRowObject(int y)
        => _active[y];

    public ref TerminalCellInfo GetCell(int y, int x)
        => ref _active[y].Cells[x];

    // ---- Dirty tracking ----
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

    public bool IsRowDirty(int y)
        => y >= 0 && y < _rows && _active[y].Dirty;

    public void MarkRowDirty(int y)
    {
        if (y >= 0 && y < _rows)
            _active[y].Dirty = true;
    }

    public void MarkRowsDirty(int from, int count)
    {
        int end = Math.Min(from + count, _rows);
        for (int i = Math.Max(0, from); i < end; i++)
            _active[i].Dirty = true;
    }

    public void MarkRowClean(int y)
    {
        if (y >= 0 && y < _rows)
            _active[y].Dirty = false;
    }

    public void MarkAllDirty()
    {
        for (int i = 0; i < _rows; i++)
            _active[i].Dirty = true;
    }

    // ---- Clearing ----
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
    public bool IsTabStop(int x)
        => x >= 0 && x < _tabStops.Length && _tabStops[x];

    public void SetTabStop(int x)
    {
        if (x >= 0 && x < _tabStops.Length)
            _tabStops[x] = true;
    }

    public void ClearTabStop(int x)
    {
        if (x >= 0 && x < _tabStops.Length)
            _tabStops[x] = false;
    }

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

    public void ResetScrollRegion()
    {
        _scrollTop = 0;
        _scrollBottom = _rows - 1;
    }

    // ---- Scrolling ----
    /// <summary>Scrolls the current scroll region up by <paramref name="n"/> (content moves up).</summary>
    public void ScrollUp(int n)
        => ScrollUp(_scrollTop, _scrollBottom, n);

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

    public void LeaveAlternateScreen()
    {
        if (!_isAlt)
            return;

        _active = _primary;
        _isAlt = false;
        ResetScrollRegion();
        MarkAllDirty();
    }

    public void ClearAlternateScreen()
    {
        if (!_isAlt)
            return;

        for (int i = 0; i < _rows; i++)
            _active[i].Clear();
    }

    // ---- Resize ----
    public void Resize(int columns, int rows)
        => Resize(columns, rows, state: null, pushScrollback: true);

    public void Resize(int columns, int rows, TerminalState? state)
        => Resize(columns, rows, state, pushScrollback: true);

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

    public void Dispose()
    {
        if (_disposed)
            return;

        _scrollback.Clear();
        _disposed = true;
    }
}
