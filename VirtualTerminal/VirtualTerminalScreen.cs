using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using VirtualTerminal.Helpers;
using VirtualTerminal.Interop;

namespace VirtualTerminal;

/// <summary>
/// Low-level WPF rendering element that draws a Windows console screen buffer (<see cref="CHAR_INFO"/>)
/// using a row-per-visual strategy for performance.
/// </summary>
public class VirtualTerminalScreen : FrameworkElement
{
    //private static readonly COORD INVALID_CURSOR = new COORD(-1, -1);
    private static readonly Color defaultForeground = Color.FromArgb(0xFF, 0x80, 0x80, 0x80);
    private static readonly Color defaultBackground = Color.FromArgb(0xFF, 0x00, 0x00, 0x00);

    private readonly DispatcherTimer? _blinkTimer;
    private readonly VisualCollection _children;
    private readonly List<DrawingVisual> _rowVisuals;
    private readonly DrawingVisual _cursorVisual;
    private readonly Lock _renderLock;

    private CONSOLE_SCREEN_BUFFER_INFO? lastInfo = null;
    private CHAR_INFO[] lastBuffer = null!;
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
        if (lastInfo == null)
            return new Size(0, 0);

        Size size = GetCellSize();
        double width = size.Width * lastInfo.Value.dwSize.X;
        double height = size.Height * (lastInfo.Value.srWindow.Bottom + 3);
        return new Size(width, height);
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(new SolidColorBrush(Background), new Pen(), new Rect(0, 0, RenderSize.Width, RenderSize.Height));
        if (DesignerProperties.GetIsInDesignMode(this))
            drawingContext.DrawText(Format("If you see this message, that means you're in a Designer mode! :P", new SolidColorBrush(Foreground)), new Point(0, 0));

        if (lastInfo == null || lastBuffer == null)
            return;

        //RenderScreen(lastInfo.Value, lastBuffer);
        //InvalidateScreen();
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

    /// <summary>
    /// Updates the rendered content to match the provided console screen buffer snapshot.
    /// </summary>
    /// <param name="newInfo">Console buffer info (size, window region, etc.).</param>
    /// <param name="buffer">Character and attribute data for the visible region.</param>
    public void UpdateBuffer(CONSOLE_SCREEN_BUFFER_INFO newInfo, CHAR_INFO[] buffer)
    {
        lock (_renderLock)
        {
            bool lastCursorVisible = CursorVisible;
            try
            {
                CursorVisible = false;
                CorrectVisualsCount(newInfo);

                if (RequiresInvalidation(newInfo, out _, out _))
                {
                    lastInfo = newInfo;
                    lastBuffer = buffer;

                    InvalidateMeasure();
                    InvalidateScreen();
                    return;
                }

                /*
                if (RequiresRemeasure(newInfo, out _, out _))
                {
                    lastInfo = newInfo;

                    InvalidateMeasure();
                    RenderScreen(newInfo, buffer);
                }
                */

                lastInfo = newInfo;
                InvalidateMeasure();
                RenderScreen(newInfo, buffer);
            }
            finally
            {
                CursorVisible = lastCursorVisible;
            }
        }
    }

    private bool RequiresInvalidation(CONSOLE_SCREEN_BUFFER_INFO newInfo, out bool invalidateRows, out bool invalidateColumns)
    {
        if (lastInfo == null)
        {
            invalidateColumns = true;
            invalidateRows = true;
            return true;
        }

        invalidateColumns = lastInfo.Value.dwSize.X != newInfo.dwSize.X;
        invalidateRows = lastInfo.Value.dwSize.Y != newInfo.dwSize.Y;
        return invalidateColumns || invalidateRows;
    }

    /*
    private bool RequiresRemeasure(CONSOLE_SCREEN_BUFFER_INFO newInfo, out bool invalidateRows, out bool invalidateColumns)
    {
        if (lastInfo == null)
        {
            invalidateColumns = true;
            invalidateRows = true;
            return true;
        }

        int oldWidth = lastInfo.Value.srWindow.Left + lastInfo.Value.srWindow.Right;
        int oldHeight = lastInfo.Value.srWindow.Top + lastInfo.Value.srWindow.Bottom;

        int newWidth = newInfo.srWindow.Left + newInfo.srWindow.Right;
        int newHeight = newInfo.srWindow.Top + newInfo.srWindow.Bottom;

        invalidateColumns = oldWidth != newWidth;
        invalidateRows = oldHeight != newHeight;
        return invalidateColumns || invalidateRows;
    }
    */

    private void CorrectVisualsCount(CONSOLE_SCREEN_BUFFER_INFO info)
    {
        int difference = info.dwSize.Y;
        if (lastInfo != null)
            difference -= lastInfo.Value.dwSize.Y;

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
            for (int i = 0; i < difference; i++)
            {
                _rowVisuals.RemoveAt(_rowVisuals.Count - 1);
                _children.RemoveAt(_children.Count - 2);
            }
        }
    }

    private void InvalidateScreen()
    {
        if (lastBuffer == null)
            return;

        if (lastInfo == null)
            return;

        lock (_renderLock)
        {
            int height = lastInfo.Value.dwSize.Y; //srWindow.Bottom;
            int width = lastInfo.Value.dwSize.X;
            Size cellSize = GetCellSize();

            for (int y = 0; y < height; y++)
            {
                try
                {
                    int bufferOffset = y * width;
                    double verticalOffset = y * cellSize.Height;

                    DrawingVisual visual = _rowVisuals[y];
                    visual.Offset = new Vector(0, verticalOffset);

                    Span<CHAR_INFO> rowSpan = lastBuffer.AsSpan(bufferOffset, width);
                    RenderRow(visual, rowSpan, cellSize);

                    if (lastInfo.Value.dwCursorPosition.Y == y)
                        UpdateCursorPosition(lastInfo.Value.dwCursorPosition, lastInfo.Value.dwSize);
                }
                catch
                {
                    // fucked up somewhere
                    _ = 0xBAD + 0xC0DE;
                }
            }
        }
    }

    private void RenderScreen(CONSOLE_SCREEN_BUFFER_INFO info, CHAR_INFO[] buffer)
    {
        lock (_renderLock)
        {
            int height = info.dwSize.Y; //info.srWindow.Bottom;
            int width = info.dwSize.X;
            Size cellSize = GetCellSize();

            for (int y = 0; y < height; y++)
            {
                try
                {
                    int bufferOffset = y * width;
                    double verticalOffset = y * cellSize.Height;

                    DrawingVisual visual = _rowVisuals[y];
                    visual.Offset = new Vector(0, verticalOffset);

                    Span<CHAR_INFO> rowSpan = lastBuffer.AsSpan(bufferOffset, width);
                    Span<CHAR_INFO> newRowSpan = buffer.AsSpan(bufferOffset, width);

                    if (!rowSpan.SequenceEqual(newRowSpan))
                        newRowSpan.CopyTo(rowSpan);

                    RenderRow(visual, rowSpan, cellSize);
                    if (info.dwCursorPosition.Y == y)
                        UpdateCursorPosition(info.dwCursorPosition, info.dwSize);
                }
                catch
                {
                    // fucked up somewhere
                    _ = 0xBAD + 0xC0DE;
                }
            }
        }
    }

    private void RenderRow(DrawingVisual visual, Span<CHAR_INFO> rowSpan, Size cellSize)
    {
        lock (_renderLock)
        {
            using DrawingContext dc = visual.RenderOpen();
            double horizontalOffset = 0;

            for (int x = 0; x < rowSpan.Length;)
            {
                try
                {
                    Span<CHAR_INFO> remainingSlice = rowSpan.Slice(x);
                    int runLength = TakeWhileSameAttributes(remainingSlice);
                    x += runLength;

                    Span<CHAR_INFO> runSpan = remainingSlice.Slice(0, runLength);
                    Span<char> textSpan = new char[runLength];

                    for (int i = 0; i < runLength; i++)
                        textSpan[i] = (char)runSpan[i].Char;

                    ConsoleCharacterAttributes attr = remainingSlice[0].Attributes;
                    Color fore = ColorHelper.ConvertToColor(attr, false);
                    Color back = ColorHelper.ConvertToColor(attr, true);

                    FormattedText formatted = Format(new string(textSpan), new SolidColorBrush(fore == defaultForeground ? Foreground : fore));
                    if (back != defaultBackground)
                        dc.DrawRectangle(new SolidColorBrush(back), new Pen(), new Rect(horizontalOffset, 0, formatted.WidthIncludingTrailingWhitespace, formatted.Height));

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

    private static int TakeWhileSameAttributes(ReadOnlySpan<CHAR_INFO> span)
    {
        if (span.IsEmpty)
            return 0;

        ConsoleCharacterAttributes attrs = span[0].Attributes;
        int length = span.Length;

        for (int i = 1; i < length; i++)
        {
            if (span[i].Attributes != attrs)
                return i;
        }

        return span.Length;
    }

    private void DispatcherBlinkHandler(object? sender, EventArgs e)
    {
        if (!lastInfo.HasValue)
            return;

        if (lastBuffer == null)
            return;

        lock (_renderLock)
        {
            if (!CursorVisible)
            {
                cursorState = false;
                RenderCursor(
                    lastInfo.Value.dwCursorPosition,
                    lastInfo.Value.dwSize);

                return;
            }

            cursorState = !cursorState;
            RenderCursor(
                lastInfo.Value.dwCursorPosition,
                lastInfo.Value.dwSize);
        }
    }

    private void UpdateCursorPosition(COORD cursorPos, COORD bufferSize)
    {
        Size cellSize = GetCellSize();
        Point point = new Point(cursorPos.X * cellSize.Width, cursorPos.Y * cellSize.Height);
        _cursorVisual.Offset = (Vector)point;
    }

    private void RenderCursorState(COORD cursorPos, COORD bufferSize)
    {
        cursorPos.X -= 1;
        int linearIndex = bufferSize.X * cursorPos.Y + cursorPos.X;
        CHAR_INFO charInfo = lastBuffer.ElementAt(linearIndex + 1);

        // Classic style cursor
        /*
        SolidColorBrush foreground = new SolidColorBrush(cursorState ? Background : Foreground);
        SolidColorBrush background = new SolidColorBrush(cursorState ? Foreground : Background);

        using DrawingContext dc = _cursorVisual.RenderOpen();
        FormattedText formatted = Format(new string((char)charInfo.Char, 1), foreground);

        dc.DrawRectangle(background, new Pen(), new Rect(0, 0, formatted.WidthIncludingTrailingWhitespace, formatted.Height));
        dc.DrawText(formatted, new Point(0, 0));
        */

        // Modern style cursor
        using DrawingContext dc = _cursorVisual.RenderOpen();
        FormattedText formatted = Format(new string((char)charInfo.Char, 1), new SolidColorBrush(Foreground));

        SolidColorBrush background = new SolidColorBrush(cursorState ? Foreground : Colors.Transparent);
        dc.DrawRectangle(background, new Pen(), new Rect(0, 0, 1, formatted.Height));
        dc.DrawText(formatted, new Point(0, 0));
    }

    private void RenderCursor(COORD cursorPos, COORD bufferSize)
    {
        lock (_renderLock)
        {
            try
            {
                UpdateCursorPosition(cursorPos, bufferSize);
                RenderCursorState(cursorPos, bufferSize);
            }
            catch
            {
                // fucked up somewhere
                _ = 0xBAD + 0xC0DE;
            }
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
