using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VirtualTerminal.Engine;
using VirtualTerminal.Engine.Components;
using VirtualTerminal.Helpers;
using VirtualTerminal.Interop;
using VirtualTerminal.Session;

namespace VirtualTerminal;

/// <summary>
/// WPF control that renders a <see cref="ITerminalSession"/>'s <see cref="TerminalScreenBuffer"/> and optionally
/// forwards direct keyboard input to the session using VT key sequences.
/// </summary>
public partial class VirtualTerminalView : UserControl, IDisposable
{
    private readonly Lock _renderLock = new Lock();
    //private readonly DispatcherTimer? _renderTimer;

    private bool renderPending;

    /// <summary>
    /// Gets or sets the active <see cref="ITerminalSession"/> displayed by this control.
    /// The control subscribes to <see cref="ITerminalSession.BufferUpdated"/> to schedule re-rendering.
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
    /// Gets or sets the terminal background color. Must be a valid console palette color.
    /// </summary>
    public Color ScreenBackground
    {
        get => (Color)GetValue(ScreenForegroundProperty);
        set => SetValue(ScreenForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the terminal foreground color. Must be a valid console palette color.
    /// </summary>
    public Color ScreenForeground
    {
        get => (Color)GetValue(ScreenForegroundProperty);
        set => SetValue(ScreenForegroundProperty, value);
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
    /// Gets whether the scroll viewer is currently pinned to the bottom (auto-scrolling enabled).
    /// </summary>
    public bool AutoScrolling
    {
        get => (bool)GetValue(AutoScrollingProperty);
        private set => SetValue(AutoScrollingProperty, value);
    }

    /// <summary>
    /// Gets the encoding used by the underlying console buffer.
    /// </summary>
    public Encoding? Encoding
    {
        get => Session?.InputEncoding;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VirtualTerminalView"/> control.
    /// </summary>
    public VirtualTerminalView()
    {
        InitializeComponent();
        AutoScrolling = true;
        PART_Output.CursorVisible = false;

        //_renderTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, DispatcherRenderHandler, Dispatcher);
        //_renderTimer.Start();

        Unloaded += (o, e) => Dispose();
    }

    /*
    private void DispatcherRenderHandler(object? sender, EventArgs e)
    {
        lock (_renderLock)
        {
            if (renderPending)
            {
                renderPending = false;
                RenderOutput();
            }
        }
    }

    private void RenderOutput()
    {
        lock (_renderLock)
        {
            if (Session?.Buffer == null)
                return;

            try
            {
                CONSOLE_SCREEN_BUFFER_INFO info = Session.Buffer.GetBufferInfo();
                CHAR_INFO[] buffer = Session.Buffer.ReadBuffer(info);
                PART_Output.UpdateBuffer(info, buffer);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }
    }
    */

    private void ScheduleRender()
    {
        renderPending = true;
    }

    private void UpdateScrollDownVisibility()
    {
        if (!ScrollDownVisible)
        {
            PART_ScrollToBottomButton.Visibility = Visibility.Collapsed;
            return;
        }

        PART_ScrollToBottomButton.Visibility = IsScrollAtBottom() ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PART_ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        AutoScrolling = IsScrollAtBottom();
        UpdateScrollDownVisibility();
    }

    private void PART_ScrollToBottomButton_Click(object sender, RoutedEventArgs e)
    {
        PART_ScrollViewer.ScrollToEnd();
    }

    private bool IsScrollAtBottom()
    {
        return PART_ScrollViewer.VerticalOffset + PART_ScrollViewer.ViewportHeight >= PART_ScrollViewer.ExtentHeight - 1.0;
    }

    /// <inheritdoc/>
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        PART_Output.CursorVisible = false;
    }

    /// <inheritdoc/>
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        PART_Output.CursorVisible = true;
    }

    /// <inheritdoc/>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!AllowDirectInput)
            return;

        if (KeyHelper.IsModifier(e.Key))
            return;

        try
        {
            string? result = KeyHelper.ConvertToVT(e);
            if (result == null)
                return;

            if (Session == null)
                return;

            Session.Append(result);
            e.Handled = true;

        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error sending key: {ex.Message}");
        }
        finally
        {
            base.OnPreviewKeyDown(e);
        }
    }

    /// <inheritdoc/>
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            int step = e.Delta / 120;
            if (PART_Output.FontSize + step >= 1)
                PART_Output.FontSize += step;

            e.Handled = true;
        }

        base.OnMouseWheel(e);
    }

    private void OnBufferUpdated(object? sender, EventArgs args)
    {
        ScheduleRender();
    }

    private static void OnScreenBackgroundPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not VirtualTerminalView terminal)
            return;

        lock (terminal._renderLock)
        {
            if (e.NewValue is not Color color)
                return;

            terminal.PART_Output.Background = color;
            terminal.ScheduleRender();
        }
    }

    private static void OnScreenForegroundPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not VirtualTerminalView terminal)
            return;

        lock (terminal._renderLock)
        {
            if (e.NewValue is not Color color)
                return;

            terminal.PART_Output.Foreground = color;
            terminal.ScheduleRender();
        }
    }

    private static void OnSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not VirtualTerminalView terminal)
            return;

        lock (terminal._renderLock)
        {
            if (e.OldValue is ITerminalSession oldSess)
            {
                oldSess.BufferUpdated -= terminal.OnBufferUpdated;
                
                // Disconnect old decoder from screen
                if (oldSess.Decoder is IProxingDecoder proxing)
                    proxing.OuterView = null;
            }

            if (e.NewValue is ITerminalSession newSess)
            {
                newSess.BufferUpdated += terminal.OnBufferUpdated;

                // Connect new decoder to screen
                if (newSess.Decoder is IProxingDecoder proxing)
                    proxing.OuterView = terminal.PART_Output;
            }

            terminal.ScheduleRender();
        }
    }

    private static bool IsValidConsoleColor(object value)
    {
        if (value is not Color color)
            return false;

        if (!ColorHelper.IsValidConsoleColor(color))
            return false;

        return true;
    }

    /// <summary>
    /// Disposes timers and detaches internal handlers. Note that the session is not owned by this control.
    /// </summary>
    public void Dispose()
    {
        // We do not own session, do not dispose it
        //_renderTimer?.Stop();
        //_blinkTimer?.Stop();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Identifies the <see cref="Session"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(ITerminalSession), typeof(VirtualTerminalView),
        new PropertyMetadata(defaultValue: null, OnSessionChanged));

    /// <summary>
    /// Identifies the <see cref="AllowDirectInput"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty AllowDirectInputProperty = DependencyProperty.Register(
        nameof(AllowDirectInput), typeof(bool), typeof(VirtualTerminalView),
        new PropertyMetadata(defaultValue: true));

    /// <summary>
    /// Identifies the <see cref="CursorBlinking"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty CursorBlinkingProperty = DependencyProperty.Register(
        nameof(CursorBlinking), typeof(bool), typeof(VirtualTerminalView),
        new PropertyMetadata(defaultValue: true));

    /// <summary>
    /// Identifies the <see cref="ScrollDownVisible"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ScrollDownVisibleProperty = DependencyProperty.Register(
        nameof(ScrollDownVisible), typeof(bool), typeof(VirtualTerminalView),
        new PropertyMetadata(defaultValue: true));

    /// <summary>
    /// Identifies the <see cref="ScreenBackground"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ScreenBackgroundProperty = DependencyProperty.Register(
        nameof(ScreenBackground), typeof(Color), typeof(VirtualTerminalView),
        new PropertyMetadata(defaultValue: Colors.Black, OnScreenBackgroundPropertyChanged), IsValidConsoleColor);

    /// <summary>
    /// Identifies the <see cref="ScreenForeground"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ScreenForegroundProperty = DependencyProperty.Register(
        nameof(ScreenForeground), typeof(Color), typeof(VirtualTerminalView),
        new PropertyMetadata(defaultValue: Colors.White, OnScreenForegroundPropertyChanged), IsValidConsoleColor);

    /// <summary>
    /// Identifies the <see cref="OutputText"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty OutputTextProperty = DependencyProperty.Register(
        nameof(OutputText), typeof(string), typeof(VirtualTerminalView),
        new PropertyMetadata(defaultValue: string.Empty));

    /// <summary>
    /// Identifies the <see cref="AutoScrolling"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty AutoScrollingProperty = DependencyProperty.Register(
        nameof(AutoScrolling), typeof(bool), typeof(VirtualTerminalView),
        new PropertyMetadata(defaultValue: true));
}
