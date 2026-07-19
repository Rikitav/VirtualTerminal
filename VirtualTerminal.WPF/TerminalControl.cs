using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VirtualTerminal.Buffer;
using VirtualTerminal.Extensions;
using VirtualTerminal.Helpers;
using VirtualTerminal.Input;
using VirtualTerminal.Interfaces;
using VirtualTerminal.Model;
using VirtualTerminal.Options;
using VirtualTerminal.Rendering;
using WindowsColor = System.Windows.Media.Color;

namespace VirtualTerminal;

/// <summary>
/// WPF control that renders a <see cref="ITerminalSession"/> using a fixed-size terminal grid
/// (size is derived from the control bounds). Scrolling is performed against the scrollback
/// buffer, not by scrolling a huge child element.
/// </summary>
public partial class TerminalControl : Control, IDisposable
{
    private readonly TerminalRenderer _renderer = new();
    private readonly TerminalOptions _options = new();

    private readonly object _invalidateLock = new object();
    private readonly DispatcherTimer _renderTimer;
    private readonly DispatcherTimer _blinkTimer;
    private readonly DispatcherTimer _resizeTimer;

    private TerminalDecoder? _decoder;
    private bool _cursorBlinkState = true;
    private bool _rendererConfigured;
    private bool _invalidatePending;

    // Text selection (mouse drag).
    private TerminalSelection? _selection;
    private bool _isSelecting = false;
    private Point _selectStart = new Point(0, 0);

    // Scrollback navigation (mouse wheel). 0 = live at the bottom; >0 = rows scrolled back into history.
    private int _scrollOffset;
    private const int ScrollWheelLines = 3;

    // Sanity limits for the terminal grid. Sizes below this (especially 1 column)
    // make ConPTY and most shells behave badly (reset/crash).
    private const int MinimumColumns = 10;
    private const int MinimumRows = 3;

    // After a resize, suppress intermediate renders for a short time so ConPTY's
    // reflow output can accumulate and we show the final frame instead of partial/blank states.
    private DateTime _suppressRenderUntil = DateTime.MinValue;
    private static readonly TimeSpan ResizeRenderSuppression = TimeSpan.FromMilliseconds(100);

    private SolidColorBrush _foregroundBrush = new SolidColorBrush(Colors.White);
    private SolidColorBrush _backgroundBrush = new SolidColorBrush(Colors.Black);

    /// <summary>
    /// Gets or sets the active <see cref="ITerminalSession"/> displayed by this control.
    /// </summary>
    public ITerminalSession? Session
    {
        get => (ITerminalSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>
    /// Gets or sets whether cursor blinking should be enabled.
    /// </summary>
    public bool CursorBlinking
    {
        get => (bool)GetValue(CursorBlinkingProperty);
        set => SetValue(CursorBlinkingProperty, value);
    }

    /// <summary>
    /// Gets or sets whether this control should forward keyboard input directly into the session.
    /// </summary>
    public bool AllowDirectInput
    {
        get => (bool)GetValue(AllowDirectInputProperty);
        set => SetValue(AllowDirectInputProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the floating "scroll to bottom" button is enabled.
    /// </summary>
    public bool ScrollDownVisible
    {
        get => (bool)GetValue(ScrollDownVisibleProperty);
        set => SetValue(ScrollDownVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets the terminal background color.
    /// </summary>
    public WindowsColor ScreenBackground
    {
        get => (WindowsColor)GetValue(ScreenBackgroundProperty);
        set => SetValue(ScreenBackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the terminal foreground color.
    /// </summary>
    public WindowsColor ScreenForeground
    {
        get => (WindowsColor)GetValue(ScreenForegroundProperty);
        set => SetValue(ScreenForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the cursor is visible.
    /// </summary>
    public bool CursorVisible
    {
        get => (bool)GetValue(CursorVisibleProperty);
        set => SetValue(CursorVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets the cursor color.
    /// </summary>
    public WindowsColor CursorColor
    {
        get => (WindowsColor)GetValue(CursorColorProperty);
        set => SetValue(CursorColorProperty, value);
    }

    /// <summary>
    /// Gets or sets the shape of the cursor.
    /// </summary>
    public CursorShape CursorShape
    {
        get => (CursorShape)GetValue(CursorShapeProperty);
        set => SetValue(CursorShapeProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum number of scrollback lines retained.
    /// </summary>
    public int ScrollbackLines
    {
        get => (int)GetValue(ScrollbackLinesProperty);
        set => SetValue(ScrollbackLinesProperty, value);
    }

    /// <summary>
    /// Gets or sets the line height multiplier (1.0 = default).
    /// </summary>
    public double LineHeight
    {
        get => (double)GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    /// <summary>
    /// Gets the rendered output as text (reserved for higher-level usages).
    /// </summary>
    public string OutputText
    {
        get => (string)GetValue(OutputTextProperty);
        private set => SetValue(OutputTextProperty, value);
    }

    /// <summary>
    /// Gets whether the viewport is pinned to the live bottom.
    /// </summary>
    public bool AutoScrolling
    {
        get => (bool)GetValue(AutoScrollingProperty);
        private set => SetValue(AutoScrollingProperty, value);
    }

    /// <summary>
    /// Gets the encoding used by the underlying session.
    /// </summary>
    public Encoding? Encoding
    {
        get => Session?.InputEncoding;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalControl"/> control.
    /// </summary>
    public TerminalControl()
    {
        Debug.WriteLine($"[{GetType().Name}] Constructor");
        _renderTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, DispatcherRenderHandler, Dispatcher);
        _blinkTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(530), DispatcherPriority.Background, DispatcherBlinkHandler, Dispatcher);
        _resizeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (s, e) => { _resizeTimer.Stop(); ResizeSessionToBounds(); }, Dispatcher);

        Focusable = true;
        Loaded += (o, e) => Debug.WriteLine($"[{GetType().Name}] Loaded: Actual={ActualWidth}x{ActualHeight}, RenderSize={RenderSize}");
        Unloaded += (o, e) => Dispose();

        _renderTimer.Start();
        _blinkTimer.Start();
    }

    // ---- Property change handling ----
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == SessionProperty)
        {
            Debug.WriteLine($"[{GetType().Name}] SessionProperty changed: old={e.OldValue?.GetType().Name ?? "null"}, new={e.NewValue?.GetType().Name ?? "null"}, rendererConfigured={_rendererConfigured}");
            if (e.OldValue is ITerminalSession oldSession)
            {
                oldSession.BufferUpdated -= OnSessionBufferUpdated;
                if (oldSession.Decoder is TerminalDecoder oldDec)
                {
                    oldDec.TitleChanged -= OnDecoderTitleChanged;
                    oldDec.Bell -= OnDecoderBell;
                }
            }

            _decoder = (e.NewValue as ITerminalSession)?.Decoder as TerminalDecoder;
            if (_decoder is { } dec)
            {
                dec.Options = _options;
                dec.TitleChanged += OnDecoderTitleChanged;
                dec.Bell += OnDecoderBell;
            }

            if (e.NewValue is ITerminalSession newSession)
                newSession.BufferUpdated += OnSessionBufferUpdated;

            SyncOptionsToDecoder();
            ResizeSessionToBounds();
            InvalidateVisual();
        }
        else if (e.Property == FontFamilyProperty || e.Property == FontSizeProperty || e.Property == LineHeightProperty)
        {
            ReconfigureRenderer();
        }
        else if (e.Property == ScreenBackgroundProperty || e.Property == ScreenForegroundProperty)
        {
            UpdateBrushes();
            InvalidateVisual();
        }
        else if (e.Property == CursorColorProperty || e.Property == CursorShapeProperty || e.Property == ScrollbackLinesProperty)
        {
            SyncOptionsToDecoder();
            InvalidateVisual();
        }
        else if (e.Property == CursorVisibleProperty)
        {
            InvalidateVisual();
        }
    }

    private void UpdateBrushes()
    {
        _foregroundBrush = new SolidColorBrush(ScreenForeground);
        _backgroundBrush = new SolidColorBrush(ScreenBackground);

        _options.DefaultBackground = ScreenBackground.ToDrawingColor();
        _options.DefaultForeground = ScreenForeground.ToDrawingColor();
    }

    private void SyncOptionsToDecoder()
    {
        _options.CursorShape = CursorShape;
        _options.DefaultCursorColor = CursorColor.ToDrawingColor();
        _options.ScrollbackMaxLines = ScrollbackLines;
        _options.LineHeight = LineHeight;
        _options.DefaultForeground = ScreenForeground.ToDrawingColor();
        _options.DefaultBackground = ScreenBackground.ToDrawingColor();

        Debug.WriteLine($"[{GetType().Name}] SyncOptionsToDecoder: ScrollbackLines={ScrollbackLines}, ScrollbackMaxLines={_options.ScrollbackMaxLines}");
        if (_decoder?.Buffer is { } buf)
        {
            buf.SetScrollbackMax(_options.ScrollbackMaxLines);
            Debug.WriteLine($"[{GetType().Name}] SyncOptionsToDecoder: buffer ScrollbackMax={buf.ScrollbackMax}, ScrollbackCount={buf.ScrollbackCount}");
        }

        _blinkTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(50, _options.CursorBlinkIntervalMs));
    }

    private void OnDecoderTitleChanged(object? sender, string title)
        => TitleChanged?.Invoke(this, title);

    private void OnDecoderBell(object? sender, EventArgs e)
        => Bell?.Invoke(this, e);

    // ---- Lifecycle ----
    protected override void OnRender(DrawingContext context)
    {
        Debug.WriteLine($"[{GetType().Name}] OnRender: decoder={_decoder is not null}, configured={_rendererConfigured}, RenderSize={RenderSize}");
        try
        {
            //base.OnRender(context);
            DrawTerminal(context);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{GetType().Name}] OnRender EXCEPTION: {ex}");
            throw;
        }
    }

    private void DrawTerminal(DrawingContext context)
    {
        if (_decoder is null || !_rendererConfigured)
        {
            context.DrawRectangle(_backgroundBrush, null, new Rect(RenderSize));
            return;
        }

        Size cell = _renderer.CellSize;
        double padLeft = Padding.Left;
        double padTop = Padding.Top;
        double padRight = Padding.Right;
        double padBottom = Padding.Bottom;

        context.PushTransform(new TranslateTransform(padLeft, padTop));

        Rect contentRect = new(0, 0, Math.Max(0, RenderSize.Width - padLeft - padRight), Math.Max(0, RenderSize.Height - padTop - padBottom));
        context.DrawRectangle(_backgroundBrush, null, contentRect);

        lock (_decoder.Buffer.SyncRoot)
        {
            RenderFullBuffer(context);
        }

        context.Pop();
    }

    private void RenderFullBuffer(DrawingContext drawingContext)
    {
        if (_decoder is null)
            return;

        TerminalScreenBuffer buf = _decoder.Buffer;
        IReadOnlyList<TerminalRow>? scrollback = _scrollOffset > 0 ? buf.GetScrollback() : null;

        int rows = buf.Rows;
        Size cell = _renderer.CellSize;
        float pixelsPerDip = (float)VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (int y = 0; y < rows; y++)
        {
            drawingContext.PushTransform(new TranslateTransform(0, y * cell.Height));
            Span<TerminalCellInfo> cells = GetRowCells(buf, scrollback, y);

            _renderer.RenderRow(drawingContext, cells, _selection, y, pixelsPerDip);
            drawingContext.Pop();
        }

        RenderCursor(drawingContext);
        for (int i = 0; i < buf.Rows; i++)
            buf.MarkRowClean(i);
    }

    private Span<TerminalCellInfo> GetRowCells(TerminalScreenBuffer buf, IReadOnlyList<TerminalRow>? scrollback, int viewportRow)
    {
        if (scrollback is null || scrollback.Count == 0)
            return buf.GetRow(viewportRow);

        int streamLen = scrollback.Count + buf.Rows;
        int top = scrollback.Count - _scrollOffset;
        int idx = top + viewportRow;

        if (idx < 0 || idx >= streamLen)
            return [];

        return idx < scrollback.Count ? scrollback[idx].AsSpan() : buf.GetRow(idx - scrollback.Count);
    }

    private void RenderCursor(DrawingContext drawingContext)
    {
        if (_decoder is null)
            return;

        TerminalState state = _decoder.State;
        Size cell = _renderer.CellSize;

        bool shouldBlink = state.Modes.CursorBlinking || _options.CursorBlink;
        bool visible = IsFocused && _scrollOffset == 0 && state.Modes.CursorVisible && CursorVisible && (!shouldBlink || _cursorBlinkState);

        if (!visible)
            return;

        // Apply only the vertical translation; RenderCursor positions the cursor
        // horizontally from the supplied column.
        drawingContext.PushTransform(new TranslateTransform(0, state.CursorY * cell.Height));
        _renderer.RenderCursor(drawingContext, state.CursorX, state.Modes.CursorShape);
        drawingContext.Pop();
    }

    private void DispatcherRenderHandler(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow < _suppressRenderUntil)
            return;

        TerminalDecoder? dec = _decoder;
        if (dec is null)
            return;

        TerminalScreenBuffer buf = dec.Buffer;
        bool dirty;
        lock (buf.SyncRoot)
            dirty = buf.HasDirtyRows;

        if (dirty || _selection is not null || _scrollOffset > 0)
            InvalidateVisual();
    }

    private void DispatcherBlinkHandler(object? sender, EventArgs e)
    {
        TerminalDecoder? dec = _decoder;
        if (dec is null)
            return;

        bool shouldBlink = dec.State.Modes.CursorBlinking || _options.CursorBlink;
        if (!shouldBlink || !IsFocused || !dec.State.Modes.CursorVisible || !CursorVisible)
            return;

        _cursorBlinkState = !_cursorBlinkState;
        InvalidateVisual();
    }

    // ---- Renderer configuration ----
    private void ReconfigureRenderer()
    {
        try
        {
            Debug.WriteLine($"[{GetType().Name}] ReconfigureRenderer: font={FontFamily.Source}, size={FontSize}");
            SyncOptionsToDecoder();
            _renderer.Configure(FontFamily.Source, FontSize, _options);
            _rendererConfigured = true;
            Debug.WriteLine($"[{GetType().Name}] ReconfigureRenderer succeeded");

            ResizeSessionToBounds();
            InvalidateVisual();
        }
        catch (Exception ex)
        {
            _rendererConfigured = false;
            Debug.WriteLine($"[{GetType().Name}] ReconfigureRenderer failed: {ex.Message}");
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        Debug.WriteLine($"[{GetType().Name}] OnRenderSizeChanged: old={sizeInfo.PreviousSize}, new={sizeInfo.NewSize}, Actual={ActualWidth}x{ActualHeight}, rendererConfigured={_rendererConfigured}");
        base.OnRenderSizeChanged(sizeInfo);

        // Debounce rapid resize events. ConPTY reflow is expensive and produces garbage
        // if we call it for every intermediate pixel change.
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void ResizeSessionToBounds()
    {
        Size renderSize = RenderSize;
        Debug.WriteLine($"[{GetType().Name}] ResizeSessionToBounds: decoder={_decoder is not null}, configured={_rendererConfigured}, RenderSize={renderSize}, Actual={ActualWidth}x{ActualHeight}");
        if (_decoder is null || !_rendererConfigured)
            return;

        if (renderSize.Width <= 0 || renderSize.Height <= 0)
        {
            Debug.WriteLine($"[{GetType().Name}] ResizeSessionToBounds: render size is zero/negative, skipping");
            return;
        }

        Size cell = _renderer.CellSize;
        if (cell.Width <= 0 || cell.Height <= 0)
        {
            Debug.WriteLine($"[{GetType().Name}] ResizeSessionToBounds: cell size is zero/negative, skipping");
            return;
        }

        double availW = Math.Max(0, renderSize.Width - Padding.Left - Padding.Right);
        double availH = Math.Max(0, renderSize.Height - Padding.Top - Padding.Bottom);
        ushort cols = (ushort)Math.Max(MinimumColumns, Math.Floor(availW / cell.Width));
        ushort rows = (ushort)Math.Max(MinimumRows, Math.Floor(availH / cell.Height));

        Debug.WriteLine($"[{GetType().Name}] ResizeSessionToBounds: cell={cell.Width}x{cell.Height}, cols={cols}, rows={rows}, current={_decoder.Buffer.Columns}x{_decoder.Buffer.Rows}");
        if (_decoder.Buffer.Columns != cols || _decoder.Buffer.Rows != rows)
        {
            Debug.WriteLine($"[{GetType().Name}] ResizeSessionToBounds: resizing session to {cols}x{rows}");

            int oldRows = _decoder.Buffer.Rows;
            Session?.Resize(cols, rows);

            // If the user is scrolled back and the height shrank, keep the same logical line
            // at the top of the viewport instead of jumping down. Expansion leaves offset unchanged.
            if (_scrollOffset > 0 && rows < oldRows)
            {
                int lost = oldRows - rows;
                _scrollOffset = Math.Clamp(_scrollOffset + lost, 0, _decoder.Buffer.ScrollbackCount);
            }

            _suppressRenderUntil = DateTime.UtcNow + ResizeRenderSuppression;

            if (DateTime.UtcNow >= _suppressRenderUntil)
                InvalidateVisual();
        }
    }

    // ---- Focus ----
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        _cursorBlinkState = true;
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    // ---- Keyboard input ----
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (_decoder is null || e.Handled || !AllowDirectInput)
            return;

        if (_scrollOffset > 0)
            ScrollToBottom();

        Key key = e.Key;
        ModifierKeys mods = Keyboard.Modifiers;

        if (key == Key.C && (mods & ModifierKeys.Control) != 0 && (mods & ModifierKeys.Shift) != 0)
        {
            _ = CopySelectionToClipboardAsync();
            e.Handled = true;
            return;
        }

        if (key == Key.V && (mods & ModifierKeys.Control) != 0)
        {
            _ = PasteFromClipboardAsync();
            e.Handled = true;
            return;
        }

        TerminalKey terminalKey = ToTerminalKey(key);
        TerminalModifier terminalMods = ToTerminalModifier(mods);

        string? keySequence = KeyboardEncoder.Encode(terminalKey, terminalMods, in _decoder.State.Modes);
        if (keySequence is not null)
        {
            Session?.Append(keySequence);
            e.Handled = true;
        }
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        base.OnPreviewTextInput(e);
        if (string.IsNullOrEmpty(e.Text) || _decoder is null || !AllowDirectInput)
            return;

        if (_scrollOffset > 0)
            ScrollToBottom();

        Session?.Append(e.Text);
        e.Handled = true;
    }

    private static TerminalKey ToTerminalKey(Key key) => key switch
    {
        Key.Enter => TerminalKey.Enter,
        Key.Back => TerminalKey.Back,
        Key.Tab => TerminalKey.Tab,
        Key.Escape => TerminalKey.Escape,
        Key.Up => TerminalKey.Up,
        Key.Down => TerminalKey.Down,
        Key.Right => TerminalKey.Right,
        Key.Left => TerminalKey.Left,
        Key.Home => TerminalKey.Home,
        Key.Insert => TerminalKey.Insert,
        Key.Delete => TerminalKey.Delete,
        Key.End => TerminalKey.End,
        Key.PageUp => TerminalKey.PageUp,
        Key.PageDown => TerminalKey.PageDown,
        Key.F1 => TerminalKey.F1,
        Key.F2 => TerminalKey.F2,
        Key.F3 => TerminalKey.F3,
        Key.F4 => TerminalKey.F4,
        Key.F5 => TerminalKey.F5,
        Key.F6 => TerminalKey.F6,
        Key.F7 => TerminalKey.F7,
        Key.F8 => TerminalKey.F8,
        Key.F9 => TerminalKey.F9,
        Key.F10 => TerminalKey.F10,
        Key.F11 => TerminalKey.F11,
        Key.F12 => TerminalKey.F12,
        _ => TerminalKey.None,
    };

    private static TerminalModifier ToTerminalModifier(ModifierKeys modifiers)
    {
        TerminalModifier result = TerminalModifier.None;
        if ((modifiers & ModifierKeys.Shift) != 0)
            result |= TerminalModifier.Shift;

        if ((modifiers & ModifierKeys.Alt) != 0)
            result |= TerminalModifier.Alt;

        if ((modifiers & ModifierKeys.Control) != 0)
            result |= TerminalModifier.Control;

        if ((modifiers & ModifierKeys.Windows) != 0)
            result |= TerminalModifier.Meta;

        return result;
    }

    // ---- Mouse selection ----
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (_decoder is null)
            return;

        Focus();
        /*
        Point p = GetCellPosition(e);
        if (p.X < 0)
            return;

        _isSelecting = true;
        _selectStart = p;
        // Don't create a selection on a plain click; selection is started by dragging.
        CaptureMouse();
        */
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isSelecting || _decoder is null)
            return;

        /*
        Point p = GetCellPosition(e);
        if (p.X < 0)
            return;

        _selection = new TerminalSelection((int)_selectStart.X, (int)_selectStart.Y, (int)p.X, (int)p.Y);
        */
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_isSelecting)
            return;

        _isSelecting = false;
        ReleaseMouseCapture();

        if (_selection is { } sel && (sel.StartY != sel.EndY || sel.StartX != sel.EndX))
        {
            _ = CopySelectionToClipboardAsync();
        }
        else
        {
            // Plain click without drag: clear the empty/single-cell selection.
            _selection = null;
            InvalidateVisual();
        }
    }

    // ---- Scrollback navigation ----
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        Debug.WriteLine($"[{GetType().Name}] OnPreviewMouseWheel: delta={e.Delta}, decoder={_decoder is not null}");
        if (_decoder is null || e.Handled)
            return;

        // Ctrl+wheel is zoom; leave it alone.
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            return;

        int direction = e.Delta > 0 ? 1 : (e.Delta < 0 ? -1 : 0);
        if (direction == 0)
            return;

        ScrollBy(direction * ScrollWheelLines);
        e.Handled = true;
    }

    /// <summary>Scrolls the viewport by <paramref name="lines"/> (positive = up into history).</summary>
    public void ScrollBy(int lines)
    {
        if (_decoder is null)
            return;

        int max = _decoder.Buffer.ScrollbackCount;
        int next = Math.Clamp(_scrollOffset + lines, 0, max);
        Debug.WriteLine($"[{GetType().Name}] ScrollBy: requested={lines}, max={max}, current={_scrollOffset}, next={next}");

        if (next == _scrollOffset)
            return;

        _scrollOffset = next;
        UpdateScrollDownVisibility();
        InvalidateVisual();
    }

    /// <summary>Snaps the viewport back to the live bottom of the buffer.</summary>
    public void ScrollToBottom()
    {
        if (_scrollOffset == 0)
            return;

        _scrollOffset = 0;
        UpdateScrollDownVisibility();
        InvalidateVisual();
    }

    private void UpdateScrollDownVisibility()
    {
        /*
        if (PART_ScrollToBottomButton is null)
            return;

        PART_ScrollToBottomButton.Visibility = ScrollDownVisible && _scrollOffset > 0
            ? Visibility.Visible : Visibility.Collapsed;
        */
    }

    private void PART_ScrollToBottomButton_Click(object sender, RoutedEventArgs e)
        => ScrollToBottom();

    private Point GetCellPosition(MouseEventArgs e)
    {
        if (_decoder is null || !_rendererConfigured)
            return new Point(-1, -1);

        Size cell = _renderer.CellSize;
        if (cell.Width <= 0 || cell.Height <= 0)
            return new Point(-1, -1);

        Point pt = e.GetPosition(this);
        double x = (pt.X - Padding.Left) / cell.Width;
        double y = (pt.Y - Padding.Top) / cell.Height;

        int col = (int)Math.Clamp(Math.Floor(x), 0, _decoder.Buffer.Columns - 1);
        int row = (int)Math.Clamp(Math.Floor(y), 0, _decoder.Buffer.Rows - 1);
        return new Point(col, row);
    }

    private string? BuildSelectedText()
    {
        if (_decoder is null || _selection is not { } sel)
            return null;

        StringBuilder builder = new();
        TerminalScreenBuffer buffer = _decoder.Buffer;

        for (int y = sel.StartY; y <= sel.EndY; y++)
        {
            if (y < 0 || y >= buffer.Rows)
                continue;

            int x0 = y == sel.StartY ? sel.StartX : 0;
            int x1 = y == sel.EndY ? sel.EndX : buffer.Columns - 1;
            Span<TerminalCellInfo> row = buffer.GetRow(y);

            for (int x = x0; x <= x1 && x < row.Length; x++)
            {
                if (row[x].Style.Continuation)
                    continue;

                builder.Append(row[x].Character.ToString());
            }

            builder.Append('\n');
        }

        return builder.Length > 0
            ? builder.ToString() : null;
    }

    private async Task CopySelectionToClipboardAsync()
    {
        string? text = BuildSelectedText();
        if (string.IsNullOrEmpty(text))
            return;

        // Clipboard integration is intentionally left for the consumer / future phase.
        await Task.CompletedTask;
    }

    private async Task PasteFromClipboardAsync()
    {
        if (Session is null)
            return;

        // Clipboard integration is intentionally left for the consumer / future phase.
        await Task.CompletedTask;
    }

    // ---- Session events ----
    private void OnSessionBufferUpdated(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow < _suppressRenderUntil)
            return;

        lock (_invalidateLock)
        {
            if (_invalidatePending)
                return;

            _invalidatePending = true;
        }

        Debug.WriteLine($"[{GetType().Name}] OnSessionBufferUpdated (thread={Environment.CurrentManagedThreadId}) -> scheduling invalidate");
        Dispatcher.BeginInvoke(() =>
        {
            lock (_invalidateLock)
                _invalidatePending = false;

            Debug.WriteLine($"[{GetType().Name}] OnSessionBufferUpdated -> InvalidateVisual");
            InvalidateVisual();
        }, DispatcherPriority.Render);
    }

    // ---- Dispose ----
    public void Dispose()
    {
        _renderTimer.Stop();
        _blinkTimer.Stop();
        _resizeTimer.Stop();

        if (Session is { } session)
        {
            session.BufferUpdated -= OnSessionBufferUpdated;
            if (session.Decoder is TerminalDecoder dec)
            {
                dec.TitleChanged -= OnDecoderTitleChanged;
                dec.Bell -= OnDecoderBell;
            }

            // Do not dispose, we do not own the session
        }

        _renderer.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Raised when the connected program changes the title (OSC 0/1/2).</summary>
    public event EventHandler<string>? TitleChanged;

    /// <summary>Raised on BEL.</summary>
    public event EventHandler? Bell;

    // ---- DependencyProperty registrations ----
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(ITerminalSession), typeof(TerminalControl),
        new PropertyMetadata(defaultValue: null));

    public static readonly DependencyProperty AllowDirectInputProperty = DependencyProperty.Register(
        nameof(AllowDirectInput), typeof(bool), typeof(TerminalControl),
        new PropertyMetadata(defaultValue: true));

    public static readonly DependencyProperty CursorBlinkingProperty = DependencyProperty.Register(
        nameof(CursorBlinking), typeof(bool), typeof(TerminalControl),
        new PropertyMetadata(defaultValue: true));

    public static readonly DependencyProperty ScrollDownVisibleProperty = DependencyProperty.Register(
        nameof(ScrollDownVisible), typeof(bool), typeof(TerminalControl),
        new PropertyMetadata(defaultValue: true));

    public static readonly DependencyProperty ScreenBackgroundProperty = DependencyProperty.Register(
        nameof(ScreenBackground), typeof(WindowsColor), typeof(TerminalControl),
        new PropertyMetadata(defaultValue: Colors.Black));

    public static readonly DependencyProperty ScreenForegroundProperty = DependencyProperty.Register(
        nameof(ScreenForeground), typeof(WindowsColor), typeof(TerminalControl),
        new PropertyMetadata(defaultValue: Colors.White));

    public static readonly DependencyProperty CursorVisibleProperty = DependencyProperty.Register(
        nameof(CursorVisible), typeof(bool), typeof(TerminalControl),
        new PropertyMetadata(defaultValue: true));

    public static readonly DependencyProperty CursorColorProperty = DependencyProperty.Register(
        nameof(CursorColor), typeof(WindowsColor), typeof(TerminalControl),
        new PropertyMetadata(defaultValue: Colors.White));

    public static readonly DependencyProperty CursorShapeProperty = DependencyProperty.Register(
        nameof(CursorShape), typeof(CursorShape), typeof(TerminalControl),
        new PropertyMetadata(defaultValue: CursorShape.Block));

    public static readonly DependencyProperty ScrollbackLinesProperty = DependencyProperty.Register(
        nameof(ScrollbackLines), typeof(int), typeof(TerminalControl),
        new PropertyMetadata(defaultValue: 10000));

    public static readonly DependencyProperty LineHeightProperty = DependencyProperty.Register(
        nameof(LineHeight), typeof(double), typeof(TerminalControl),
        new PropertyMetadata(defaultValue: 1.0));

    public static readonly DependencyProperty OutputTextProperty = DependencyProperty.Register(
        nameof(OutputText), typeof(string), typeof(TerminalControl),
        new PropertyMetadata(defaultValue: string.Empty));

    public static readonly DependencyProperty AutoScrollingProperty = DependencyProperty.Register(
        nameof(AutoScrolling), typeof(bool), typeof(TerminalControl),
        new PropertyMetadata(defaultValue: true));
}
