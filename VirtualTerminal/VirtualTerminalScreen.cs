using System.Globalization;
using System.Windows;
using System.Windows.Media;
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
    private static readonly ConsoleCharacterAttributes defaultAttr = ConsoleCharacterAttributes.ForegroundBlue | ConsoleCharacterAttributes.ForegroundGreen | ConsoleCharacterAttributes.ForegroundRed;

    private readonly VisualCollection _children;
    private readonly List<DrawingVisual> _rowVisuals;
    private readonly Lock _renderLock = new Lock();

    private CONSOLE_SCREEN_BUFFER_INFO lastInfo = default;
    private CHAR_INFO[] lastBuffer = null!;

    private Typeface Typeface => new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

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
        _children = new VisualCollection(this);
        _rowVisuals = [];
    }

    /// <inheritdoc/>
    protected override int VisualChildrenCount => _children.Count;

    /// <inheritdoc/>
    protected override Visual GetVisualChild(int index) => _children[index];

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawingContext)
    {
        InvalidateScreen();
    }

    /// <inheritdoc/>
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        InvalidateScreen();
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
            //int width = newInfo.dwSize.X;
            

            CorrectVisualsCount(newInfo);
            if (NeedScreenInvalidation(newInfo, out _, out _))
            {
                lastInfo = newInfo;
                lastBuffer = buffer;
                InvalidateVisual();
                return;
            }

            RenderScreen(newInfo, buffer);
        }
    }

    private bool NeedScreenInvalidation(CONSOLE_SCREEN_BUFFER_INFO newInfo, out bool invalidateRows, out bool invalidateLines)
    {
        invalidateLines = lastInfo.dwSize.X != newInfo.dwSize.X;
        invalidateRows = lastInfo.dwSize.Y != newInfo.dwSize.Y;
        return invalidateLines || invalidateRows;
    }

    private void CorrectVisualsCount(CONSOLE_SCREEN_BUFFER_INFO info)
    {
        int difference = info.dwSize.Y - lastInfo.dwSize.Y;
        if (difference == 0)
            return;

        if (difference > 0)
        {
            for (int i = 0; i < difference; i++)
            {
                DrawingVisual newVisual = new DrawingVisual();
                _rowVisuals.Add(newVisual);
                _children.Add(newVisual);
            }
        }
        else if (difference < 0)
        {
            for (int i = 0; i < difference; i++)
            {
                _rowVisuals.RemoveAt(_rowVisuals.Count - 1);
                _children.RemoveAt(_rowVisuals.Count - 1);
            }
        }
    }

    private void RenderScreen(CONSOLE_SCREEN_BUFFER_INFO info, CHAR_INFO[] buffer)
    {
        lock (_renderLock)
        {
            int height = info.dwSize.Y;
            int width = info.dwSize.X;
            Size cellSize = GetCellSize();

            int bufferOffset = 0;
            double verticalOffset = 0;

            for (int y = 0; y < height; y++)
            {
                try
                {
                    DrawingVisual visual = _rowVisuals[y];
                    visual.Offset = new Vector(0, verticalOffset);

                    Span<CHAR_INFO> rowSpan = lastBuffer.AsSpan(bufferOffset, width);
                    Span<CHAR_INFO> newRowSpan = buffer.AsSpan(bufferOffset, width);

                    if (!rowSpan.SequenceEqual(newRowSpan))
                        newRowSpan.CopyTo(rowSpan);

                    RenderRow(visual, rowSpan, cellSize);
                }
                catch
                {
                    // fucked up somewhere
                    _ = 0xBAD + 0xC0DE;
                }
                finally
                {
                    bufferOffset += width;
                    verticalOffset += cellSize.Height;
                }
            }

            Height = verticalOffset;
        }
    }

    private void InvalidateScreen()
    {
        lock (_renderLock)
        {
            if (lastBuffer == null)
                return;

            int height = lastInfo.dwSize.Y;
            int width = lastInfo.dwSize.X;
            Size cellSize = GetCellSize();

            int bufferOffset = 0;
            double verticalOffset = 0;

            for (int y = 0; y < height; y++)
            {
                try
                {
                    DrawingVisual visual = _rowVisuals[y];
                    visual.Offset = new Vector(0, verticalOffset);

                    Span<CHAR_INFO> rowSpan = lastBuffer.AsSpan(bufferOffset, width);
                    RenderRow(visual, rowSpan, cellSize);
                }
                catch
                {
                    // fucked up somewhere
                    _ = 0xBAD + 0xC0DE;
                }
                finally
                {
                    bufferOffset += width;
                    verticalOffset += cellSize.Height;
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

                    ConsoleCharacterAttributes attr = remainingSlice[0].Attributes;
                    Brush foreground = new SolidColorBrush(attr == defaultAttr
                        ? Foreground : ColorHelper.ConvertToColor(attr, false));

                    Span<CHAR_INFO> runSpan = remainingSlice.Slice(0, runLength);
                    Span<char> textSpan = new char[runLength];

                    for (int i = 0; i < runLength; i++)
                        textSpan[i] = (char)runSpan[i].Char;

                    FormattedText formatted = new FormattedText(new string(textSpan),
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        Typeface, FontSize, foreground,
                        VisualTreeHelper.GetDpi(this).PixelsPerDip);

                    dc.DrawText(formatted, new Point(horizontalOffset, 0));
                }
                catch
                {
                    // fucked up somewhere
                    _ = 0xBAD + 0xC0DE;
                }

                horizontalOffset = x * cellSize.Width;
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

    /// <summary>
    /// Returns <see cref="Size"/> of terminals' cell from current Font settings
    /// </summary>
    /// <returns></returns>
    public Size GetCellSize()
    {
        FormattedText formattedText = new FormattedText("M",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface, FontSize, Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        return new Size(formattedText.WidthIncludingTrailingWhitespace, formattedText.Height);
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
