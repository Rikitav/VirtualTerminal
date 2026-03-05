using System.ComponentModel;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using VirtualTerminal.Engine;
using VirtualTerminal.Engine.Components;
using System.Windows;

using TPoint = System.Drawing.Point;
using TSize = System.Drawing.Size;
using TColor = System.Windows.Media.Color;
using System.Diagnostics;

namespace VirtualTerminal;

/// <summary>
/// Low-level WPF rendering element that draws a terminal screen buffer (<see cref="TerminalScreenBuffer"/>)
/// using a row-per-visual strategy for performance.
/// </summary>
public class VirtualTerminalScreen : FrameworkElement, ITerminalScreenView
{
    private static readonly TColor defaultForeground = Colors.White;
    private static readonly TColor defaultBackground = Colors.Black;

    private readonly DispatcherTimer? _blinkTimer;
    private readonly VisualCollection _children;
    private readonly List<DrawingVisual> _rowVisuals;
    private readonly DrawingVisual _cursorVisual;
    private readonly Lock _renderLock;

    private bool needsFullInvalidation = false;
    private bool cursorState = true;

    private TerminalScreenBuffer? currentBuffer;

    /// <inheritdoc/>
    protected override int VisualChildrenCount => _children.Count;

    /// <summary>
    /// Gets or sets the font family used to render terminal text.
    /// </summary>
    public bool CursorVisible
    {
        get => (bool)GetValue(CursorVisibleProperty);
        set => SetValue(CursorVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets the font family used to render terminal text.
    /// </summary>
    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>
    /// Gets or sets the font size used to render terminal text.
    /// </summary>
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the default foreground color used when console attributes match the default attribute set.
    /// </summary>
    public TColor Foreground
    {
        get => (TColor)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the background color of the terminal surface.
    /// </summary>
    public TColor Background
    {
        get => (TColor)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <inheritdoc/>
    public TPoint CursorPosition
    {
        get;
        private set;
    }

    /// <summary>
    /// Initializes new <see cref="VirtualTerminalScreen"/>
    /// </summary>
    public VirtualTerminalScreen()
    {
        _renderLock = new Lock();
        _cursorVisual = new DrawingVisual();
        _rowVisuals = [];
        _children = new VisualCollection(this) { _cursorVisual };

        _blinkTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(530), DispatcherPriority.Background, DispatcherBlinkHandler, Dispatcher);
        _blinkTimer.Start();
    }

    /// <inheritdoc/>
    protected override Visual GetVisualChild(int index) => _children[index];

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (currentBuffer == null)
            return new Size(0, 0);

        Size size = GetCellSize();
        double width = size.Width * currentBuffer.GridSize.Width;
        double height = size.Height * currentBuffer.GridSize.Height;
        return new Size(width, height);
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(new SolidColorBrush(Background), new Pen(), new Rect(0, 0, RenderSize.Width, RenderSize.Height));
        if (DesignerProperties.GetIsInDesignMode(this))
            drawingContext.DrawText(Format("If you see this message, that means you're in a Designer mode! :P", new SolidColorBrush(Foreground)), new Point(0, 0));

        if (needsFullInvalidation)
        {
            RenderFullBuffer();
            needsFullInvalidation = false;
        }
    }
    
    private void RenderFullBuffer()
    {
        if (currentBuffer == null)
            return;

        lock (_renderLock)
        {
            CorrectVisualsCountForBuffer();
            for (int y = 0; y < currentBuffer.GridSize.Height && y < _rowVisuals.Count; y++)
                RenderRowFromBuffer(y);

            UpdateCursorPositionFromBuffer();
        }
    }

    /// <summary>
    /// Returns <see cref="Size"/> of terminals' cell from current Font settings
    /// </summary>
    /// <returns></returns>
    public Size GetCellSize()
    {
        FormattedText formattedText = Format("M", Brushes.Black);
        return new Size(formattedText.WidthIncludingTrailingWhitespace, formattedText.Height);
    }

    /// <inheritdoc/>
    public void BufferChanged(ITerminalDecoder sender, TerminalScreenBuffer buffer)
    {
        currentBuffer = buffer;
        CorrectVisualsCountForBuffer();
    }

    /// <inheritdoc/>
    public void Characters(ITerminalDecoder sender, ReadOnlySpan<char> chars)
    {
        if (sender is not IProxingDecoder proxy)
            throw new InvalidOperationException("Screen support only IProxingDecoder as parent decoder");

        // Invalidate affected rows - must be on UI thread
        InvalidateRowsFromBuffer(proxy);
    }

    /// <inheritdoc/>
    public void SaveCursor(ITerminalDecoder sender)
    {
        // No visual change needed
    }

    /// <inheritdoc/>
    public void RestoreCursor(ITerminalDecoder sender)
    {
        if (sender is not IProxingDecoder proxy)
            throw new InvalidOperationException("Screen support only IProxingDecoder as parent decoder");

        InvalidateCursor(proxy);
    }

    /// <inheritdoc/>
    public TSize GetSize(ITerminalDecoder sender)
    {
        if (currentBuffer == null)
            return new TSize(0, 0);

        return currentBuffer.GridSize;
    }

    /// <inheritdoc/>
    public void MoveCursor(ITerminalDecoder sender, Direction direction, int amount)
    {
        if (sender is not IProxingDecoder proxy)
            throw new InvalidOperationException("Screen support only IProxingDecoder as parent decoder");

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => MoveCursor(sender, direction, amount));
            return;
        }

        InvalidateCursor(proxy);
    }

    /// <inheritdoc/>
    public void MoveCursorToBeginningOfLineBelow(ITerminalDecoder sender, int lineNumberRelativeToCurrentLine)
    {
        if (sender is not IProxingDecoder proxy)
            throw new InvalidOperationException("Screen support only IProxingDecoder as parent decoder");

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => MoveCursorToBeginningOfLineAbove(sender, lineNumberRelativeToCurrentLine));
            return;
        }

        InvalidateCursor(proxy);
    }

    /// <inheritdoc/>
    public void MoveCursorToBeginningOfLineAbove(ITerminalDecoder sender, int lineNumberRelativeToCurrentLine)
    {
        if (sender is not IProxingDecoder proxy)
            throw new InvalidOperationException("Screen support only IProxingDecoder as parent decoder");

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => MoveCursorToBeginningOfLineAbove(sender, lineNumberRelativeToCurrentLine));
            return;
        }

        InvalidateCursor(proxy);
    }

    /// <inheritdoc/>
    public void MoveCursorToColumn(ITerminalDecoder sender, int columnNumber)
    {
        if (sender is not IProxingDecoder proxy)
            throw new InvalidOperationException("Screen support only IProxingDecoder as parent decoder");

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => MoveCursorToColumn(sender, columnNumber));
            return;
        }

        InvalidateCursor(proxy);
    }

    /// <inheritdoc/>
    public void MoveCursorTo(ITerminalDecoder sender, TPoint position)
    {
        if (sender is not IProxingDecoder proxy)
            throw new InvalidOperationException("Screen support only IProxingDecoder as parent decoder");

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => MoveCursorTo(sender, position));
            return;
        }

        InvalidateCursor(proxy);
    }

    /// <inheritdoc/>
    public void ClearScreen(ITerminalDecoder sender, ClearDirection direction)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ClearScreen(sender, direction));
            return;
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <inheritdoc/>
    public void ClearLine(ITerminalDecoder sender, ClearDirection direction)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ClearLine(sender, direction));
            return;
        }

        // Invalidate the affected line
        InvalidateRow(CursorPosition.Y);
    }

    /// <inheritdoc/>
    public void ScrollPageUpwards(ITerminalDecoder sender, int linesToScroll)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ScrollPageUpwards(sender, linesToScroll));
            return;
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <inheritdoc/>
    public void EraseCharacters(ITerminalDecoder sender, int count)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => EraseCharacters(sender, count));
            return;
        }

        InvalidateRow(CursorPosition.Y);
    }

    /// <inheritdoc/>
    public void ScrollPageDownwards(ITerminalDecoder sender, int linesToScroll)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ScrollPageDownwards(sender, linesToScroll));
            return;
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <inheritdoc/>
    public TPoint GetCursorPosition(ITerminalDecoder sender)
    {
        return CursorPosition;
    }

    /// <inheritdoc/>
    public void SetGraphicRendition(ITerminalDecoder sender, GraphicRendition[] commands)
    {
        // Invalidate current row where cursor is
        int row = CursorPosition.Y;
        InvalidateRow(row);
    }

    /// <inheritdoc/>
    public void ModeChanged(ITerminalDecoder sender, AnsiMode mode)
    {
        if (sender is not IProxingDecoder proxy)
            throw new InvalidOperationException("Screen support only IProxingDecoder as parent decoder");

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ModeChanged(sender, mode));
            return;
        }

        // Some modes might require full invalidation
        switch (mode)
        {
            case AnsiMode.HideCursor:
                {
                    CursorVisible = false;
                    InvalidateCursor(proxy);
                    break;
                }

            case AnsiMode.ShowCursor:
                {
                    CursorVisible = true;
                    InvalidateCursor(proxy);
                    break;
                }
        }
    }
    
    private void CorrectVisualsCountForBuffer()
    {
        if (currentBuffer == null)
            return;

        int targetRows = currentBuffer.Rows.Count;
        int currentRows = _rowVisuals.Count;
        int difference = targetRows - currentRows;

        if (difference == 0)
            return;

        if (difference > 0)
        {
            for (int i = 0; i < difference; i++)
            {
                DrawingVisual newVisual = new DrawingVisual();
                _rowVisuals.Add(newVisual);
                _children.Insert(_children.Count - 1, newVisual);
            }
        }
        else if (difference < 0)
        {
            for (int i = 0; i < -difference; i++)
            {
                _rowVisuals.RemoveAt(_rowVisuals.Count - 1);
                _children.RemoveAt(_children.Count - 2);
            }
        }
    }
    
    private void InvalidateRowsFromBuffer(IProxingDecoder sender)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => InvalidateRowsFromBuffer(sender));
            return;
        }
        
        CursorPosition = sender.CursorPosition;
        InvalidateRow(CursorPosition.Y);
    }
    
    private void InvalidateRow(int row)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => InvalidateRow(row));
            return;
        }
        
        if (currentBuffer == null || row < 0 || row >= currentBuffer.GridSize.Height)
            return;
            
        lock (_renderLock)
        {
            CorrectVisualsCountForBuffer();
            if (row < _rowVisuals.Count)
            {
                RenderRowFromBuffer(row);
            }
        }
    }
    
    private void InvalidateCursor(IProxingDecoder sender)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => InvalidateCursor(sender));
            return;
        }
        
        CursorPosition = sender.CursorPosition;
        UpdateCursorPositionFromBuffer();
        RenderCursorFromBuffer();
    }
    
    private void RenderRowFromBuffer(int row)
    {
        if (currentBuffer == null || row < 0 || row >= currentBuffer.GridSize.Height || row >= _rowVisuals.Count)
            return;
            
        try
        {
            Size cellSize = GetCellSize();
            double verticalOffset = row * cellSize.Height;
            
            DrawingVisual visual = _rowVisuals[row];
            visual.Offset = new Vector(0, verticalOffset);
            
            Span<TerminalCellInfo> rowSpan = currentBuffer.Rows[row];
            RenderRowFromTerminalBuffer(visual, rowSpan, cellSize);
        }
        catch
        {
            // fucked up somewhere
            _ = 0xBAD + 0xC0DE;
        }
    }
    
    private void RenderRowFromTerminalBuffer(DrawingVisual visual, Span<TerminalCellInfo> rowSpan, Size cellSize)
    {
        lock (_renderLock)
        {
            using DrawingContext dc = visual.RenderOpen();
            double horizontalOffset = 0;

            Span<char> textSpan = rowSpan.Length > 1024
                ? new char[rowSpan.Length]
                : stackalloc char[rowSpan.Length];

            for (int x = 0; x < rowSpan.Length;)
            {
                try
                {
                    Span<TerminalCellInfo> remainingSlice = rowSpan.Slice(x);
                    int runLength = TakeWhileSameAttributes(remainingSlice);
                    x += runLength;

                    Span<TerminalCellInfo> runSpan = remainingSlice.Slice(0, runLength);

                    for (int i = 0; i < runLength; i++)
                        textSpan[i] = runSpan[i].Character;

                    TerminalCellInfo firstCell = runSpan[0];
                    
                    // Apply bold if needed
                    FontWeight weight = firstCell.Bold ? FontWeights.Bold : FontWeights.Normal;
                    Typeface runTypeface = new Typeface(FontFamily, FontStyles.Normal, weight, FontStretches.Normal);
                    
                    FormattedText formatted = new FormattedText(
                        new string(textSpan.Slice(0, runLength)), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        runTypeface, FontSize, new SolidColorBrush(firstCell.Foreground == defaultForeground ? Foreground : firstCell.Foreground),
                        VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    
                    if (firstCell.Background != defaultBackground)
                    {
                        dc.DrawRectangle(
                            new SolidColorBrush(firstCell.Foreground), new Pen(),
                            new Rect(horizontalOffset, 0, formatted.WidthIncludingTrailingWhitespace, formatted.Height));
                    }

                    dc.DrawText(formatted, new Point(horizontalOffset, 0));
                    horizontalOffset += formatted.WidthIncludingTrailingWhitespace;
                }
                catch
                {
                    // fucked up somewhere
                    _ = 0xBAD + 0xC0DE;
                }
            }
        }
    }
    
    private static int TakeWhileSameAttributes(ReadOnlySpan<TerminalCellInfo> span)
    {
        if (span.IsEmpty)
            return 0;

        TerminalCellInfo first = span[0];
        int length = span.Length;

        for (int i = 1; i < length; i++)
        {
            TerminalCellInfo current = span[i];
            if (first != current)
                return i;
        }

        return span.Length;
    }
    
    private void UpdateCursorPositionFromBuffer()
    {
        if (currentBuffer == null)
            return;
            
        Size cellSize = GetCellSize();
        Point point = new Point(CursorPosition.X * cellSize.Width, CursorPosition.Y * cellSize.Height);
        _cursorVisual.Offset = (Vector)point;
    }
    
    private void RenderCursorFromBuffer()
    {
        if (currentBuffer == null)
            return;

        try
        {
            lock (_renderLock)
            {
                UpdateCursorPositionFromBuffer();

                using DrawingContext dc = _cursorVisual.RenderOpen();
                SolidColorBrush background = new SolidColorBrush(cursorState ? Foreground : Colors.Transparent);
                dc.DrawRectangle(background, new Pen(), new Rect(0, 0, 1, GetCellSize().Height));
            }
        }
        catch (Exception exc)
        {
            Debug.WriteLine(exc);
        }
    }

    private void DispatcherBlinkHandler(object? sender, EventArgs e)
    {
        lock (_renderLock)
        {
            if (!CursorVisible)
            {
                cursorState = false;
                RenderCursorFromBuffer();
                return;
            }

            cursorState = !cursorState;
            RenderCursorFromBuffer();
        }
    }

    private FormattedText Format(string text, Brush foreground)
    {
        Typeface typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        return new FormattedText(
            text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, FontSize, foreground,  VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    private static void OnCursorVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs args)
    {

    }

    private static void OnFontFamilyChanged(DependencyObject d, DependencyPropertyChangedEventArgs args)
    {

    }

    private static void OnFontSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs args)
    {

    }

    private static void OnForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs args)
    {

    }

    private static void OnBackgroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs args)
    {

    }

    /// <summary>
    /// Identifies the <see cref="FontFamily"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty CursorVisibleProperty = DependencyProperty.Register(
        nameof(CursorVisible), typeof(bool), typeof(VirtualTerminalScreen),
        new FrameworkPropertyMetadata(true, OnCursorVisibleChanged));

    /// <summary>
    /// Identifies the <see cref="FontFamily"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(
        nameof(FontFamily), typeof(FontFamily), typeof(VirtualTerminalScreen),
        new FrameworkPropertyMetadata(new FontFamily("Consolas"), FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnFontFamilyChanged));

    /// <summary>
    /// Identifies the <see cref="FontSize"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
        nameof(FontSize), typeof(double), typeof(VirtualTerminalScreen),
        new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnFontSizeChanged));

    /// <summary>
    /// Identifies the <see cref="Foreground"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Color), typeof(VirtualTerminalScreen),
        new FrameworkPropertyMetadata(Colors.LightGray, FrameworkPropertyMetadataOptions.AffectsRender, OnForegroundChanged));

    /// <summary>
    /// Identifies the <see cref="Background"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Color), typeof(VirtualTerminalScreen),
        new FrameworkPropertyMetadata(Colors.Black, FrameworkPropertyMetadataOptions.AffectsRender, OnBackgroundChanged));
}
