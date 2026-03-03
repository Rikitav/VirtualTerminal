using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using VirtualTerminal.Engine;
using VirtualTerminal.Engine.Components;

namespace VirtualTerminal;

/// <summary>
/// Low-level WPF rendering element that draws a terminal screen buffer (<see cref="TerminalScreenBuffer"/>)
/// using a row-per-visual strategy for performance.
/// </summary>
public class VirtualTerminalScreen : FrameworkElement, ITerminalScreenView
{
    private static readonly TextColor defaultForeground = TextColor.White;
    private static readonly TextColor defaultBackground = TextColor.Black;

    private readonly DispatcherTimer? _blinkTimer;
    private readonly VisualCollection _children;
    private readonly List<DrawingVisual> _rowVisuals;
    private readonly DrawingVisual _cursorVisual;
    private readonly Lock _renderLock;

    private IBufferedDecoder? currentDecoder = null;
    private TerminalScreenBuffer? currentBuffer = null;
    private Coord currentCursorPosition = new Coord(0, 0);
    private bool needsFullInvalidation = false;
    private bool cursorState = true;
    
    private Typeface Typeface => new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

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
    public Color Foreground
    {
        get => (Color)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the background color of the terminal surface.
    /// </summary>
    public Color Background
    {
        get => (Color)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
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
        double width = size.Width * currentBuffer.ColumnsCount;
        double height = size.Height * currentBuffer.RowsCount;
        return new Size(width, height);
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(new SolidColorBrush(Background), new Pen(), new Rect(0, 0, RenderSize.Width, RenderSize.Height));
        if (DesignerProperties.GetIsInDesignMode(this))
            drawingContext.DrawText(Format("If you see this message, that means you're in a Designer mode! :P", new SolidColorBrush(Foreground)), new Point(0, 0));

        if (currentBuffer != null && needsFullInvalidation)
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
            CorrectVisualsCountForBuffer(currentBuffer);
            
            for (int y = 0; y < currentBuffer.RowsCount && y < _rowVisuals.Count; y++)
            {
                RenderRowFromBuffer(y);
            }
            
            if (currentDecoder != null)
            {
                currentCursorPosition = currentDecoder.CursorPosition;
                UpdateCursorPositionFromBuffer();
            }
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
    public void Characters(IBufferedDecoder sender, ReadOnlySpan<char> chars)
    {
        if (sender?.Buffer == null)
            return;
            
        currentDecoder = sender;
        currentBuffer = sender.Buffer;

        // Invalidate affected rows - must be on UI thread
        InvalidateRowsFromBuffer();
    }

    /// <inheritdoc/>
    public void SaveCursor(IBufferedDecoder sernder)
    {
        // No visual change needed
    }

    /// <inheritdoc/>
    public void RestoreCursor(IBufferedDecoder sender)
    {
        if (sender?.Buffer == null)
            return;
            
        currentDecoder = sender;
        currentBuffer = sender.Buffer;
        currentCursorPosition = sender.CursorPosition;
        
        InvalidateCursor();
    }

    /// <inheritdoc/>
    public Size GetSize(IBufferedDecoder sender)
    {
        if (sender?.Buffer == null)
            return new Size(0, 0);
            
        return new Size(sender.Buffer.ColumnsCount, sender.Buffer.RowsCount);
    }

    /// <inheritdoc/>
    public void MoveCursor(IBufferedDecoder sender, Direction direction, int amount)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => MoveCursor(sender, direction, amount));
            return;
        }

        if (sender?.Buffer == null)
            return;
            
        currentDecoder = sender;
        currentBuffer = sender.Buffer;
        currentCursorPosition = sender.CursorPosition;

        InvalidateCursor();
    }

    /// <inheritdoc/>
    public void MoveCursorToBeginningOfLineBelow(IBufferedDecoder sender, int lineNumberRelativeToCurrentLine)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => MoveCursorToBeginningOfLineAbove(sender, lineNumberRelativeToCurrentLine));
            return;
        }

        if (sender?.Buffer == null)
            return;
            
        currentDecoder = sender;
        currentBuffer = sender.Buffer;
        currentCursorPosition = sender.CursorPosition;

        InvalidateCursor();
    }

    /// <inheritdoc/>
    public void MoveCursorToBeginningOfLineAbove(IBufferedDecoder sender, int lineNumberRelativeToCurrentLine)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => MoveCursorToBeginningOfLineAbove(sender, lineNumberRelativeToCurrentLine));
            return;
        }

        if (sender?.Buffer == null)
            return;
            
        currentDecoder = sender;
        currentBuffer = sender.Buffer;
        currentCursorPosition = sender.CursorPosition;

        InvalidateCursor();
    }

    /// <inheritdoc/>
    public void MoveCursorToColumn(IBufferedDecoder sender, int columnNumber)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => MoveCursorToColumn(sender, columnNumber));
            return;
        }

        if (sender?.Buffer == null)
            return;
            
        currentDecoder = sender;
        currentBuffer = sender.Buffer;
        currentCursorPosition = sender.CursorPosition;

        InvalidateCursor();
    }

    /// <inheritdoc/>
    public void MoveCursorTo(IBufferedDecoder sender, Coord position)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => MoveCursorTo(sender, position));
            return;
        }

        if (sender?.Buffer == null)
            return;
            
        currentDecoder = sender;
        currentBuffer = sender.Buffer;
        currentCursorPosition = sender.CursorPosition;

        InvalidateCursor();
    }

    /// <inheritdoc/>
    public void ClearScreen(IBufferedDecoder sender, ClearDirection direction)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ClearScreen(sender, direction));
            return;
        }

        if (sender?.Buffer == null)
            return;
            
        currentDecoder = sender;
        currentBuffer = sender.Buffer;
        needsFullInvalidation = true;

        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <inheritdoc/>
    public void ClearLine(IBufferedDecoder sender, ClearDirection direction)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ClearLine(sender, direction));
            return;
        }

        if (sender?.Buffer == null)
            return;
            
        currentDecoder = sender;
        currentBuffer = sender.Buffer;
        currentCursorPosition = sender.CursorPosition;

        // Invalidate the affected line
        InvalidateRow(currentCursorPosition.Y);
    }

    /// <inheritdoc/>
    public void ScrollPageUpwards(IBufferedDecoder sender, int linesToScroll)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ScrollPageUpwards(sender, linesToScroll));
            return;
        }

        if (sender?.Buffer == null)
            return;
            
        currentDecoder = sender;
        currentBuffer = sender.Buffer;
        needsFullInvalidation = true;

        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <inheritdoc/>
    public void ScrollPageDownwards(IBufferedDecoder sender, int linesToScroll)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ScrollPageDownwards(sender, linesToScroll));
            return;
        }

        if (sender?.Buffer == null)
            return;
            
        currentDecoder = sender;
        currentBuffer = sender.Buffer;
        needsFullInvalidation = true;

        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <inheritdoc/>
    public Coord GetCursorPosition(IBufferedDecoder sender)
    {
        if (sender?.Buffer == null)
            return new Coord(0, 0);
            
        return new Coord(sender.CursorPosition.X, sender.CursorPosition.Y);
    }

    /// <inheritdoc/>
    public void SetGraphicRendition(IBufferedDecoder sender, GraphicRendition[] commands)
    {
        if (sender?.Buffer == null)
            return;
            
        currentDecoder = sender;
        currentBuffer = sender.Buffer;
        
        // Invalidate current row where cursor is
        int row = sender.CursorPosition.Y;
        InvalidateRow(row);
    }

    /// <inheritdoc/>
    public void ModeChanged(IBufferedDecoder sender, AnsiMode mode)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ModeChanged(sender, mode));
            return;
        }

        if (sender?.Buffer == null)
            return;
            
        currentDecoder = sender;
        currentBuffer = sender.Buffer;

        // Some modes might require full invalidation
        switch (mode)
        {
            case AnsiMode.HideCursor:
                {
                    CursorVisible = false;
                    InvalidateCursor();
                    break;
                }

            case AnsiMode.ShowCursor:
                {
                    CursorVisible = true;
                    InvalidateCursor();
                    break;
                }
        }
    }
    
    /// <summary>
    /// Sets the decoder and buffer for rendering. Called when session changes.
    /// </summary>
    public void SetDecoder(IBufferedDecoder? decoder)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetDecoder(decoder));
            return;
        }

        currentDecoder = decoder;
        currentBuffer = decoder?.Buffer;

        if (currentBuffer == null)
            return;
        
        lock (_renderLock)
        {
            CorrectVisualsCountForBuffer(currentBuffer);
            needsFullInvalidation = true;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }
    
    private void CorrectVisualsCountForBuffer(TerminalScreenBuffer buffer)
    {
        int targetRows = buffer.RowsCount;
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
    
    private void InvalidateRowsFromBuffer()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => InvalidateRowsFromBuffer());
            return;
        }
        
        if (currentBuffer == null || currentDecoder == null)
            return;
            
        currentCursorPosition = currentDecoder.CursorPosition;
        InvalidateRow(currentCursorPosition.Y);
    }
    
    private void InvalidateRow(int row)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => InvalidateRow(row));
            return;
        }
        
        if (currentBuffer == null || row < 0 || row >= currentBuffer.RowsCount)
            return;
            
        lock (_renderLock)
        {
            if (row < _rowVisuals.Count)
            {
                RenderRowFromBuffer(row);
            }
        }
    }
    
    private void InvalidateCursor()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => InvalidateCursor());
            return;
        }
        
        if (currentBuffer == null || currentDecoder == null)
            return;
            
        currentCursorPosition = currentDecoder.CursorPosition;
        UpdateCursorPositionFromBuffer();
        RenderCursorFromBuffer();
    }
    
    private void RenderRowFromBuffer(int row)
    {
        if (currentBuffer == null || row < 0 || row >= currentBuffer.RowsCount || row >= _rowVisuals.Count)
            return;
            
        try
        {
            Size cellSize = GetCellSize();
            double verticalOffset = row * cellSize.Height;
            
            DrawingVisual visual = _rowVisuals[row];
            visual.Offset = new Vector(0, verticalOffset);
            
            int rowStart = row * currentBuffer.ColumnsCount;
            Span<TerminalCellInfo> rowSpan = currentBuffer.Cells.AsSpan(rowStart, currentBuffer.ColumnsCount);
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

            for (int x = 0; x < rowSpan.Length;)
            {
                try
                {
                    Span<TerminalCellInfo> remainingSlice = rowSpan.Slice(x);
                    int runLength = TakeWhileSameAttributes(remainingSlice);
                    x += runLength;

                    Span<TerminalCellInfo> runSpan = remainingSlice.Slice(0, runLength);
                    Span<char> textSpan = new char[runLength];

                    for (int i = 0; i < runLength; i++)
                        textSpan[i] = runSpan[i].Character;

                    TerminalCellInfo firstCell = runSpan[0];
                    
                    // Apply bold if needed
                    FontWeight weight = firstCell.Bold ? FontWeights.Bold : FontWeights.Normal;
                    Typeface runTypeface = new Typeface(FontFamily, FontStyles.Normal, weight, FontStretches.Normal);
                    
                    FormattedText formatted = new FormattedText(
                        new string(textSpan), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        runTypeface, FontSize, new SolidColorBrush(firstCell.Foreground == defaultForeground ? Foreground : TerminalCellInfo.TextColorToColor(firstCell.Foreground)),
                        VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    
                    if (firstCell.Background != defaultBackground)
                        dc.DrawRectangle(new SolidColorBrush(
                            TerminalCellInfo.TextColorToColor(firstCell.Foreground)),
                            new Pen(),
                            new Rect(horizontalOffset, 0, formatted.WidthIncludingTrailingWhitespace, formatted.Height));

                    dc.DrawText(formatted, new Point(horizontalOffset, 0));
                    horizontalOffset += formatted.Width;
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
        Point point = new Point(currentCursorPosition.X * cellSize.Width, currentCursorPosition.Y * cellSize.Height);
        _cursorVisual.Offset = (Vector)point;
    }
    
    private void RenderCursorFromBuffer()
    {
        if (currentBuffer == null || currentDecoder == null)
            return;
            
        lock (_renderLock)
        {
            try
            {
                UpdateCursorPositionFromBuffer();
                
                int linearIndex = currentCursorPosition.Y * currentBuffer.ColumnsCount + currentCursorPosition.X;
                if (linearIndex >= 0 && linearIndex < currentBuffer.Cells.Length)
                {
                    TerminalCellInfo cell = currentBuffer.Cells[linearIndex];
                    char charToShow = cell.Character;
                    
                    using DrawingContext dc = _cursorVisual.RenderOpen();
                    FormattedText formatted = Format(new string(charToShow, 1), new SolidColorBrush(Foreground));

                    SolidColorBrush background = new SolidColorBrush(cursorState ? Foreground : Colors.Transparent);
                    dc.DrawRectangle(background, new Pen(), new Rect(0, 0, 1, formatted.Height));
                    dc.DrawText(formatted, new Point(0, 0));
                }
            }
            catch
            {
                // fucked up somewhere
                _ = 0xBAD + 0xC0DE;
            }
        }
    }

    private void DispatcherBlinkHandler(object? sender, EventArgs e)
    {
        if (currentBuffer == null || currentDecoder == null)
            return;

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
        return new FormattedText(
            text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface, FontSize, foreground,  VisualTreeHelper.GetDpi(this).PixelsPerDip);
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
