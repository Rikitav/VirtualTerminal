using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using System.Text;
using VirtualTerminal.Buffer;
using VirtualTerminal.Extensions;
using VirtualTerminal.Helpers;
using VirtualTerminal.Input;
using VirtualTerminal.Interfaces;
using VirtualTerminal.Model;
using VirtualTerminal.Options;
using VirtualTerminal.Rendering;
using AvaloniaColor = Avalonia.Media.Color;
using Color = System.Drawing.Color;

namespace VirtualTerminal;

/// <summary>
/// A customizable, xterm-compatible virtual terminal control. Renders a <see cref="TerminalScreenBuffer"/>
/// via a <see cref="TerminalRenderer"/> (GlyphRun), feeds keyboard/mouse through xterm encoders, supports
/// text selection, and exposes cursor/palette/scrollback customization via StyledProperties.
/// </summary>
public class TerminalControl : TemplatedControl, IDisposable
{
    private readonly TerminalRenderer _renderer = new TerminalRenderer();
    private readonly TerminalOptions _options = new TerminalOptions();

    private readonly DrawingGroup _rootDrawingGroup = new DrawingGroup();
    private readonly DrawingGroup _cursorVisual = new DrawingGroup();
    private readonly DrawingGroup _contentGroup = new DrawingGroup();
    private readonly List<DrawingGroup> _lineDrawings = [];

    private readonly DispatcherTimer _renderTimer;
    private readonly DispatcherTimer _blinkTimer;
    private readonly DispatcherTimer _resizeTimer;

    private TerminalDecoder? _decoder;
    private bool _cursorBlinkState = true;
    private bool _needsFullInvalidation = true;
    private bool _rendererConfigured;

    // Text selection (mouse drag).
    private TerminalSelection? _selection;
    private bool _isSelecting;
    private Point _selectStart;

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

    // Scroll-to-bottom indicator hit-test region.
    private Rect _scrollIndicatorRect;

    // Serializes changes to render state that is not the screen buffer itself.
    private readonly Lock _renderStateLock = new Lock();

    /// <summary>Raised when the connected program changes the title (OSC 0/1/2).</summary>
    public event EventHandler<string>? TitleChanged;

    /// <summary>Raised on BEL.</summary>
    public event EventHandler? Bell;

    /// <summary>Gets or sets the active terminal session rendered by this control.</summary>
    public ITerminalSession? CurrentSession
    {
        get => GetValue(CurrentSessionProperty);
        set => SetValue(CurrentSessionProperty, value);
    }

    /// <summary>Gets or sets the terminal font size in device-independent pixels.</summary>
    public double TerminalFontSize
    {
        get => GetValue(TerminalFontSizeProperty);
        set => SetValue(TerminalFontSizeProperty, value);
    }

    /// <summary>Gets or sets the terminal font family name.</summary>
    public string TerminalFontFamily
    {
        get => GetValue(TerminalFontFamilyProperty);
        set => SetValue(TerminalFontFamilyProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether the cursor is visible.</summary>
    public bool CursorVisible
    {
        get => GetValue(CursorVisibleProperty);
        set => SetValue(CursorVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether keyboard input is forwarded directly to the session.
    /// When <c>false</c>, key presses and text input are not sent to <see cref="CurrentSession"/>.
    /// </summary>
    public bool AllowDirectInput
    {
        get => GetValue(AllowDirectInputProperty);
        set => SetValue(AllowDirectInputProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the floating "scroll to bottom" indicator is shown
    /// when the viewport is scrolled back into history.
    /// </summary>
    public bool ScrollDownVisible
    {
        get => GetValue(ScrollDownVisibleProperty);
        set => SetValue(ScrollDownVisibleProperty, value);
    }

    /// <summary>Gets or sets the shape of the cursor.</summary>
    public CursorShape CursorShape
    {
        get => GetValue(CursorShapeProperty);
        set => SetValue(CursorShapeProperty, value);
    }

    /// <summary>Gets or sets the cursor color.</summary>
    public AvaloniaColor CursorColor
    {
        get => GetValue(CursorColorProperty);
        set => SetValue(CursorColorProperty, value);
    }

    /// <summary>Gets or sets the maximum number of scrollback lines retained.</summary>
    public int ScrollbackLines
    {
        get => GetValue(ScrollbackLinesProperty);
        set => SetValue(ScrollbackLinesProperty, value);
    }

    /// <summary>Gets or sets the line height multiplier (1.0 = default).</summary>
    public double LineHeight
    {
        get => GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    /// <summary>Gets the terminal options instance used by the underlying decoder.</summary>
    public TerminalOptions Options => _options;

    /// <summary>Gets the current screen buffer, or <c>null</c> if no session is attached.</summary>
    public TerminalScreenBuffer? CurrentBuffer => _decoder?.Buffer;

    /// <summary>Gets the current cursor position in buffer coordinates.</summary>
    public Point CursorPosition => _decoder is null ? default : new Point(_decoder.State.CursorX, _decoder.State.CursorY);

    public TerminalControl()
    {
        _renderTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, Dispatcher.CurrentDispatcher, DispatcherRenderHandler);
        _blinkTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(530), DispatcherPriority.Background, Dispatcher.CurrentDispatcher, DispatcherBlinkHandler);
        _resizeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, Dispatcher.CurrentDispatcher, (s, e) => { _resizeTimer.Stop(); ResizeSessionToBounds(); });

        _rootDrawingGroup.Children.Add(_contentGroup);
        _rootDrawingGroup.Children.Add(_cursorVisual);
    }

    /// <summary>Sends a clear-screen + home escape sequence to the active session.</summary>
    public void Clear()
    {
        // Standard "clear screen + home" via escape sequences (decoded, not a direct buffer wipe).
        _decoder?.Write("\x1b[2J\x1b[H"u8);
    }

    // ---- Property change handling ----
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CurrentSessionProperty)
        {
            if (change.OldValue is ITerminalSession oldSession && oldSession.Decoder is TerminalDecoder oldDec)
            {
                oldDec.TitleChanged -= OnDecoderTitleChanged;
                oldDec.Bell -= OnDecoderBell;
            }

            _decoder = (change.NewValue as ITerminalSession)?.Decoder as TerminalDecoder;
            if (_decoder is { } dec)
            {
                dec.Options = _options;
                dec.TitleChanged += OnDecoderTitleChanged;
                dec.Bell += OnDecoderBell;
            }

            SyncOptionsToDecoder();
            ResizeSessionToBounds();
            _needsFullInvalidation = true;
            InvalidateVisual();
        }

        if (change.Property == TerminalFontSizeProperty
            || change.Property == TerminalFontFamilyProperty
            || change.Property == LineHeightProperty
            || change.Property == CursorColorProperty
            || change.Property == CursorShapeProperty
            || change.Property == ScrollbackLinesProperty)
        {
            SyncOptionsToDecoder();
            ReconfigureRenderer();
        }

        if (change.Property == PaddingProperty)
        {
            ApplyPadding();
            InvalidateVisual();
        }
    }

    private void SyncOptionsToDecoder()
    {
        _options.CursorShape = CursorShape;
        _options.DefaultCursorColor = CursorColor.ToDrawingColor();
        _options.ScrollbackMaxLines = ScrollbackLines;
        _options.LineHeight = LineHeight;
        _options.DefaultForeground = Foreground is SolidColorBrush fb ? fb.Color.ToDrawingColor() : Color.White;
        _options.DefaultBackground = Background is SolidColorBrush bb ? bb.Color.ToDrawingColor() : Color.Black;

        if (_decoder?.Buffer is { } buf)
            buf.SetScrollbackMax(_options.ScrollbackMaxLines);

        _blinkTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(50, _options.CursorBlinkIntervalMs));
    }

    private void OnDecoderTitleChanged(object? sender, string title)
        => TitleChanged?.Invoke(this, title);

    private void OnDecoderBell(object? sender, EventArgs e)
        => Bell?.Invoke(this, e);

    // ---- Lifecycle ----
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ReconfigureRenderer();
        _renderTimer.Start();
        _blinkTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _renderTimer.Stop();
        _blinkTimer.Stop();
    }

    private void ReconfigureRenderer()
    {
        try
        {
            SyncOptionsToDecoder();
            _renderer.Configure(TerminalFontFamily, TerminalFontSize, _options);

            ApplyPadding();
            _rendererConfigured = true;

            ResizeSessionToBounds();
            _needsFullInvalidation = true;

            InvalidateVisual();
        }
        catch
        {
            // Font not yet available; will retry on the next layout pass.
            _rendererConfigured = false;
        }
    }

    private void ApplyPadding()
    {
        _contentGroup.Transform = new TranslateTransform(Padding.Left, Padding.Top);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        // Debounce rapid resize events. ConPTY reflow is expensive and produces garbage
        // if we call it for every intermediate pixel change.
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void ResizeSessionToBounds()
    {
        if (_decoder is null || !_rendererConfigured)
            return;

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        Size cell = _renderer.CellSize;
        if (cell.Width <= 0 || cell.Height <= 0)
            return;

        double availW = Math.Max(0, Bounds.Width - Padding.Left - Padding.Right);
        double availH = Math.Max(0, Bounds.Height - Padding.Top - Padding.Bottom);
        ushort cols = (ushort)Math.Max(MinimumColumns, Math.Floor(availW / cell.Width));
        ushort rows = (ushort)Math.Max(MinimumRows, Math.Floor(availH / cell.Height));

        if (_decoder.Buffer.Columns != cols || _decoder.Buffer.Rows != rows)
        {
            int oldRows = _decoder.Buffer.Rows;
            CurrentSession?.Resize(cols, rows);

            // If the user is scrolled back and the height shrank, keep the same logical line
            // at the top of the viewport instead of jumping down. Expansion leaves offset unchanged.
            if (_scrollOffset > 0 && rows < oldRows)
            {
                int lost = oldRows - rows;
                _scrollOffset = Math.Clamp(_scrollOffset + lost, 0, _decoder.Buffer.ScrollbackCount);
            }

            lock (_renderStateLock)
                _needsFullInvalidation = true;

            _suppressRenderUntil = DateTime.UtcNow + ResizeRenderSuppression;

            if (DateTime.UtcNow >= _suppressRenderUntil)
                InvalidateVisual();
        }
    }

    // ---- Rendering ----
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_decoder is null || !_rendererConfigured)
        {
            if (Background is not null)
                context.DrawRectangle(Background, null, new Rect(Bounds.Size));

            return;
        }

        lock (_decoder.Buffer.SyncRoot)
        {
            if (_needsFullInvalidation)
            {
                RenderFullBuffer();
                _needsFullInvalidation = false;
            }
        }

        if (Background is not null)
            context.DrawRectangle(Background, null, new Rect(Bounds.Size));

        _rootDrawingGroup.Draw(context);
        RenderScrollToBottomIndicator(context);
    }

    private void RenderScrollToBottomIndicator(DrawingContext context)
    {
        _scrollIndicatorRect = default;
        if (!ScrollDownVisible || _scrollOffset <= 0)
            return;

        double size = 32;
        double padding = 10;
        double x = Math.Max(0, Bounds.Width - size - padding);
        double y = Math.Max(0, Bounds.Height - size - padding);
        _scrollIndicatorRect = new Rect(x, y, size, size);

        SolidColorBrush brush = new SolidColorBrush(AvaloniaColor.Parse("#333333"));
        context.DrawRectangle(brush, null, _scrollIndicatorRect, size / 2, size / 2);

        StreamGeometry arrow = new StreamGeometry();
        using (StreamGeometryContext ctx = arrow.Open())
        {
            ctx.BeginFigure(new Point(x + size * 0.3, y + size * 0.35), false);
            ctx.LineTo(new Point(x + size * 0.5, y + size * 0.65));
            ctx.LineTo(new Point(x + size * 0.7, y + size * 0.35));
        }

        context.DrawGeometry(new SolidColorBrush(Colors.White), null, arrow);
    }

    private void RenderFullBuffer()
    {
        if (_decoder is null)
            return;

        lock (_decoder.Buffer.SyncRoot)
        {
            CorrectVisualsCountForBuffer();
            TerminalScreenBuffer buf = _decoder.Buffer;

            IReadOnlyList<TerminalRow>? scrollback = _scrollOffset > 0 ? buf.GetScrollback() : null;
            int rows = Math.Min(buf.Rows, _lineDrawings.Count);

            for (int y = 0; y < rows; y++)
                RenderViewportRow(y, scrollback);

            RenderCursorFromBuffer();
        }
    }

    private void DispatcherRenderHandler(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow < _suppressRenderUntil)
            return;

        TerminalDecoder? dec = _decoder;
        if (dec is null)
            return;

        TerminalScreenBuffer buf = dec.Buffer;
        bool shouldInvalidate = false;

        lock (buf.SyncRoot)
        {
            // While viewing history, the visible content is static; repaint fully on any change so
            // eviction/scroll shifts are reflected (and mark active rows clean so we don't loop).
            if (_scrollOffset > 0)
            {
                if (buf.HasDirtyRows)
                {
                    CorrectVisualsCountForBuffer();
                    IReadOnlyList<TerminalRow> scrollback = buf.GetScrollback();
                    int rows = Math.Min(buf.Rows, _lineDrawings.Count);

                    for (int y = 0; y < rows; y++)
                        RenderViewportRow(y, scrollback);

                    for (int i = 0; i < buf.Rows; i++)
                        buf.MarkRowClean(i);

                    shouldInvalidate = true;
                }
            }
            else if (buf.HasDirtyRows || _selection is not null)
            {
                CorrectVisualsCountForBuffer();
                int rows = Math.Min(buf.Rows, _lineDrawings.Count);

                for (int i = 0; i < rows; i++)
                {
                    if (!buf.IsRowDirty(i) && _selection is null)
                        continue;

                    RenderViewportRow(i, null);
                    buf.MarkRowClean(i);
                    shouldInvalidate = true;
                }
            }
        }

        if (shouldInvalidate)
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
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
        lock (dec.Buffer.SyncRoot)
            RenderCursorFromBuffer();

        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }

    private void CorrectVisualsCountForBuffer()
    {
        if (_decoder is null)
            return;

        int target = _decoder.Buffer.Rows;
        int current = _lineDrawings.Count;
        if (target == current)
            return;

        if (target > current)
        {
            for (int i = 0; i < target - current; i++)
                _lineDrawings.Add(new DrawingGroup());
        }
        else if (target < current)
        {
            _lineDrawings.RemoveRange(target, current - target);
        }

        // Rebuild the visual tree so row order always matches _lineDrawings exactly.
        // This avoids stale/misaligned visuals after the buffer row count changes.
        _contentGroup.Children.Clear();
        foreach (DrawingGroup group in _lineDrawings)
            _contentGroup.Children.Add(group);
    }

    /// <summary>
    /// Renders one viewport row. When <paramref name="scrollback"/> is non-null (viewing history),
    /// the row's source is computed from the (scrollback + active) stream; otherwise it is the
    /// active buffer row at the same index.
    /// </summary>
    private void RenderViewportRow(int viewportRow, IReadOnlyList<TerminalRow>? scrollback)
    {
        if (_decoder is null || viewportRow < 0 || viewportRow >= _lineDrawings.Count)
            return;

        try
        {
            Size cell = _renderer.CellSize;
            DrawingGroup group = _lineDrawings[viewportRow];
            group.Children.Clear();
            group.Transform = new TranslateTransform(0, viewportRow * cell.Height);

            TerminalScreenBuffer buf = _decoder.Buffer;
            TerminalSelection? sel = _scrollOffset > 0 ? null : _selection;
            Span<TerminalCellInfo> cells;

            if (scrollback is null || scrollback.Count == 0)
            {
                if (viewportRow >= buf.Rows)
                    return;
                cells = buf.GetRow(viewportRow);
            }
            else
            {
                int streamLen = scrollback.Count + buf.Rows;
                int top = scrollback.Count - _scrollOffset; // stream index of the top visible row
                int idx = top + viewportRow;

                if (idx < 0 || idx >= streamLen)
                    return;

                cells = idx < scrollback.Count ? scrollback[idx].AsSpan() : buf.GetRow(idx - scrollback.Count);
            }

            using DrawingContext drawingContext = group.Open();
            _renderer.RenderRow(drawingContext, cells, sel, viewportRow);
        }
        catch
        {
            // transient (e.g. concurrent resize) — skip this pass
        }
    }

    private void RenderCursorFromBuffer()
    {
        if (_decoder is null)
            return;

        try
        {
            Size cell = _renderer.CellSize;
            TerminalState state = _decoder.State;
            _cursorVisual.Transform = new TranslateTransform(0, state.CursorY * cell.Height);

            using DrawingContext drawingContext = _cursorVisual.Open();
            bool shouldBlink = state.Modes.CursorBlinking || _options.CursorBlink;
            bool visible = IsFocused && _scrollOffset == 0
                && state.Modes.CursorVisible && CursorVisible
                && (!shouldBlink || _cursorBlinkState);

            if (visible)
                _renderer.RenderCursor(drawingContext, state.CursorX, state.Modes.CursorShape);
        }
        catch
        {
            // ignore transient cursor render failures
        }
    }

    // ---- Focus ----
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        _cursorBlinkState = true;
        if (_decoder is not null)
        {
            lock (_decoder.Buffer.SyncRoot)
                RenderCursorFromBuffer();
        }

        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        if (_decoder is not null)
        {
            lock (_decoder.Buffer.SyncRoot)
                RenderCursorFromBuffer();
        }

        InvalidateVisual();
    }

    // ---- Keyboard input ----
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_decoder is null || e.Handled)
            return;

        KeyModifiers mods = e.KeyModifiers;

        if (e.Key == Key.C && (mods & KeyModifiers.Control) != 0 && (mods & KeyModifiers.Shift) != 0)
        {
            _ = CopySelectionToClipboardAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.V && (mods & KeyModifiers.Control) != 0)
        {
            _ = PasteFromClipboardAsync();
            e.Handled = true;
            return;
        }

        if (!AllowDirectInput)
            return;

        // Typing snaps the viewport back to the live bottom.
        if (_scrollOffset > 0)
            ScrollToBottom();

        TerminalKey terminalKey = ToTerminalKey(e.Key);
        TerminalModifier terminalMods = ToTerminalModifier(mods);

        string? seq = KeyboardEncoder.Encode(terminalKey, terminalMods, in _decoder.State.Modes);
        if (seq is not null)
        {
            CurrentSession?.Append(seq);
            e.Handled = true;
        }
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

    private static TerminalModifier ToTerminalModifier(KeyModifiers modifiers)
    {
        TerminalModifier result = TerminalModifier.None;
        if ((modifiers & KeyModifiers.Shift) != 0)
            result |= TerminalModifier.Shift;
        if ((modifiers & KeyModifiers.Alt) != 0)
            result |= TerminalModifier.Alt;
        if ((modifiers & KeyModifiers.Control) != 0)
            result |= TerminalModifier.Control;
        if ((modifiers & KeyModifiers.Meta) != 0)
            result |= TerminalModifier.Meta;
        return result;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (string.IsNullOrEmpty(e.Text) || _decoder is null || !AllowDirectInput)
            return;

        if (_scrollOffset > 0)
            ScrollToBottom();

        CurrentSession?.Append(e.Text);
        e.Handled = true;
    }

    // ---- Mouse selection ----
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (ScrollDownVisible && _scrollOffset > 0 && _scrollIndicatorRect.Contains(e.GetPosition(this)))
        {
            ScrollToBottom();
            e.Handled = true;
            return;
        }

        if (_decoder is null)
            return;

        Point p = GetCellPosition(e);
        if (p.X < 0)
            return;

        _isSelecting = true;
        _selectStart = p;
        _selection = new TerminalSelection((int)p.X, (int)p.Y, (int)p.X, (int)p.Y);

        InvalidateVisual();
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isSelecting || _decoder is null)
            return;

        Point p = GetCellPosition(e);
        if (p.X < 0)
            return;

        _selection = new TerminalSelection((int)_selectStart.X, (int)_selectStart.Y, (int)p.X, (int)p.Y);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isSelecting)
            return;

        _isSelecting = false;
        e.Pointer.Capture(null);

        if (_selection is { } sel && (sel.StartY != sel.EndY || sel.StartX != sel.EndX))
            _ = CopySelectionToClipboardAsync();
    }

    // ---- Scrollback navigation ----
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_decoder is null || e.Handled)
            return;

        // Ctrl+wheel is zoom (handled by TerminalZoomBehavior); leave it alone.
        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
            return;

        int direction = e.Delta.Y > 0 ? 1 : (e.Delta.Y < 0 ? -1 : 0);
        if (direction == 0)
            return;

        ScrollBy(direction * ScrollWheelLines);
        e.Handled = true;
    }

    /// <summary>Scrolls the viewport by <paramref name="lines"/> (positive = up into history).</summary>
    /// <param name="lines">Number of lines to scroll. Positive values scroll up into history, negative scroll down toward the live bottom.</param>
    public void ScrollBy(int lines)
    {
        if (_decoder is null)
            return;

        int max = _decoder.Buffer.ScrollbackCount;
        int next = Math.Clamp(_scrollOffset + lines, 0, max);

        if (next == _scrollOffset)
            return;

        _scrollOffset = next;
        lock (_renderStateLock)
            _needsFullInvalidation = true;

        InvalidateVisual();
    }

    /// <summary>Snaps the viewport back to the live bottom of the buffer.</summary>
    public void ScrollToBottom()
    {
        if (_scrollOffset == 0)
            return;

        _scrollOffset = 0;
        lock (_renderStateLock)
            _needsFullInvalidation = true;

        InvalidateVisual();
    }

    /// <summary>Current scrollback offset (0 = live). Exposed for future scrollbar UI.</summary>
    public int ScrollOffset => _scrollOffset;

    /// <summary>Gets the cell position under the specified pointer event, or (-1,-1) if outside the terminal surface.</summary>

    private Point GetCellPosition(PointerEventArgs e)
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

        StringBuilder sb = new StringBuilder();
        TerminalScreenBuffer buf = _decoder.Buffer;
        for (int y = sel.StartY; y <= sel.EndY; y++)
        {
            if (y < 0 || y >= buf.Rows)
                continue;

            int x0 = y == sel.StartY ? sel.StartX : 0;
            int x1 = y == sel.EndY ? sel.EndX : buf.Columns - 1;
            Span<TerminalCellInfo> row = buf.GetRow(y);

            for (int x = x0; x <= x1 && x < row.Length; x++)
            {
                if (row[x].Style.Continuation)
                    continue;

                sb.Append(row[x].Character.ToString());
            }

            sb.Append('\n');
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    private async Task CopySelectionToClipboardAsync()
    {
        string? text = BuildSelectedText();
        if (string.IsNullOrEmpty(text))
            return;

        //await SetClipboardTextAsync(text.TrimEnd('\n'));
    }

    private async Task PasteFromClipboardAsync()
    {
        if (CurrentSession is null)
            return;

        /*
        string? text = await GetClipboardTextAsync();
        if (string.IsNullOrEmpty(text))
            return;

        bool bracketed = _decoder?.State.Modes.BracketedPaste == true;
        CurrentSession.Append(KeyboardEncoder.WrapBracketedPaste(text, bracketed));
        */
    }

    // ---- Dispose ----
    /// <summary>Stops timers, detaches from the decoder and releases rendering resources.</summary>
    public void Dispose()
    {
        _renderTimer.Stop();
        _blinkTimer.Stop();
        _resizeTimer.Stop();

        if (_decoder is { } dec)
        {
            dec.TitleChanged -= OnDecoderTitleChanged;
            dec.Bell -= OnDecoderBell;
        }

        _renderer.Dispose();
        GC.SuppressFinalize(this);
    }

    // ---- StyledProperty registrations ----
    /// <summary>Defines the <see cref="CurrentSession"/> styled property.</summary>
    public static readonly StyledProperty<ITerminalSession?> CurrentSessionProperty =
        AvaloniaProperty.Register<TerminalControl, ITerminalSession?>(nameof(CurrentSession), null);

    /// <summary>Defines the <see cref="CursorVisible"/> styled property.</summary>
    public static readonly StyledProperty<bool> CursorVisibleProperty =
        AvaloniaProperty.Register<TerminalControl, bool>(nameof(CursorVisible), true);

    /// <summary>Defines the <see cref="AllowDirectInput"/> styled property.</summary>
    public static readonly StyledProperty<bool> AllowDirectInputProperty =
        AvaloniaProperty.Register<TerminalControl, bool>(nameof(AllowDirectInput), true);

    /// <summary>Defines the <see cref="ScrollDownVisible"/> styled property.</summary>
    public static readonly StyledProperty<bool> ScrollDownVisibleProperty =
        AvaloniaProperty.Register<TerminalControl, bool>(nameof(ScrollDownVisible), true);

    /// <summary>Defines the <see cref="TerminalFontSize"/> styled property.</summary>
    public static readonly StyledProperty<double> TerminalFontSizeProperty =
        AvaloniaProperty.Register<TerminalControl, double>(nameof(TerminalFontSize), 14);

    /// <summary>Defines the <see cref="TerminalFontFamily"/> styled property.</summary>
    public static readonly StyledProperty<string> TerminalFontFamilyProperty =
        AvaloniaProperty.Register<TerminalControl, string>(nameof(TerminalFontFamily), "Cascadia Code");

    /// <summary>Defines the <see cref="CursorShape"/> styled property.</summary>
    public static readonly StyledProperty<CursorShape> CursorShapeProperty =
        AvaloniaProperty.Register<TerminalControl, CursorShape>(nameof(CursorShape), CursorShape.Block);

    /// <summary>Defines the <see cref="CursorColor"/> styled property.</summary>
    public static readonly StyledProperty<AvaloniaColor> CursorColorProperty =
        AvaloniaProperty.Register<TerminalControl, AvaloniaColor>(nameof(CursorColor), Colors.White);

    /// <summary>Defines the <see cref="ScrollbackLines"/> styled property.</summary>
    public static readonly StyledProperty<int> ScrollbackLinesProperty =
        AvaloniaProperty.Register<TerminalControl, int>(nameof(ScrollbackLines), 10000);

    /// <summary>Defines the <see cref="LineHeight"/> styled property.</summary>
    public static readonly StyledProperty<double> LineHeightProperty =
        AvaloniaProperty.Register<TerminalControl, double>(nameof(LineHeight), 1.0);
}
