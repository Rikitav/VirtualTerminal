using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using VirtualTerminal.Controls;
using VirtualTerminal.Cursors;
using VirtualTerminal.Interfaces;
using VirtualTerminal.Session;

namespace VirtualTerminal.AvaloniaTestApp;

/// <summary>
/// Main window of the Avalonia test application demonstrating generic control multiplexing, smooth animations, public APIs, and command bindings.
/// </summary>
public partial class MainWindow : Window
{
    private MultiplexerLayoutNode? _savedLayout;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        SetupMultiplexerDemo();

        string? autoCmd = App.AutoStartCommand;
        if (!string.IsNullOrEmpty(autoCmd))
        {
            var term = CreateDemoTerminal(new CommandLineSession(autoCmd));
            PART_Multiplexer.Root = term;
        }
    }

    private void SetupMultiplexerDemo()
    {
        PART_Multiplexer.NewPaneFactory = () => CreateDemoTerminal();
        PART_Multiplexer.ActiveControlChanged += (s, active) => UpdateStatus();
        PART_Multiplexer.PaneOpened += (s, e) => UpdateStatus();
        PART_Multiplexer.PaneClosed += (s, e) => UpdateStatus();

        // 1. Initial primary pane: PowerShell terminal
        var shell = CreateDemoTerminal();
        PART_Multiplexer.Root = shell;

        // 2. Split vertically with TUI Menu terminal
        var tui = CreateDemoTerminal(new SimpleTuiDemoSession());
        PART_Multiplexer.Split(shell, tui, Orientation.Vertical, 0.5, animate: false);

        // 3. Split the right pane horizontally with a generic non-terminal Avalonia control
        var genericControl = CreateGenericAvaloniaControl("Editor & Inspector Pane");
        PART_Multiplexer.Split(tui, genericControl, Orientation.Horizontal, 0.5, animate: false);

        UpdateStatus();

        // Automated test hook for headless verification
        if (Environment.GetCommandLineArgs().Contains("--test-splits"))
        {
            // Test 1: Split vertical on active pane
            var term1 = CreateDemoTerminal();
            if (!PART_Multiplexer.SplitActive(term1, Orientation.Vertical, animate: false))
                throw new InvalidOperationException("Failed to split vertical");

            // Test 2: Split horizontal on active pane
            var term2 = CreateGenericAvaloniaControl("Test Pane 2");
            if (!PART_Multiplexer.SplitActive(term2, Orientation.Horizontal, animate: false))
                throw new InvalidOperationException("Failed to split horizontal");

            // Test 3: Close active pane
            if (!PART_Multiplexer.CloseActive())
                throw new InvalidOperationException("Failed to close active pane");

            // Test 4: Focus navigation
            PART_Multiplexer.FocusNext();
            PART_Multiplexer.FocusPrevious();

            // Test 5: Layout state capture and restore
            var layout = PART_Multiplexer.GetLayoutState(c => c is TerminalControl ? "Terminal" : "Generic");
            if (layout == null)
                throw new InvalidOperationException("Failed to capture layout state");

            if (!PART_Multiplexer.RestoreLayoutState(layout, id => id == "Terminal" ? CreateDemoTerminal() : CreateGenericAvaloniaControl("Restored")))
                throw new InvalidOperationException("Failed to restore layout state");

            Console.WriteLine("AVALONIA_SPLIT_TEST_SUCCESS");
            Environment.Exit(0);
        }
    }

    private void UpdateStatus()
    {
        PART_StatusText.Text = $"Panes: {PART_Multiplexer.PaneCount} | Active: {PART_Multiplexer.ActiveControl?.GetType().Name ?? "None"}";
    }

    private TerminalControl CreateDemoTerminal(ITerminalSession? session = null)
    {
        var terminal = new TerminalControl
        {
            AllowDirectInput = true,
            Background = Brushes.Black,
            TerminalFontFamily = "Cascadia Mono, Consolas, Courier New",
            CursorRenderer = new AnimatedTerminalCursorRenderer(),
        };

        if (session != null)
        {
            terminal.Session = session;
        }
        else
        {
            try
            {
                terminal.Session = new CommandLineSession("powershell.exe");
            }
            catch
            {
                try
                {
                    terminal.Session = new CommandLineSession("cmd.exe");
                }
                catch
                {
                    terminal.Session = new SimpleTuiDemoSession();
                }
            }
        }

        return terminal;
    }

    private Control CreateGenericAvaloniaControl(string title)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 32)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(2),
            Padding = new Thickness(12),
        };

        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var titleBlock = new TextBlock
        {
            Text = $"Generic Non-Terminal Control:\n{title}",
            Foreground = Brushes.LightGreen,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
        };
        sp.Children.Add(titleBlock);

        var descBlock = new TextBlock
        {
            Text = "The MultiplexerControl tiles and manages ANY Avalonia control (Buttons, TextBoxes, Canvases, UserControls) with smooth animated splits and layout persistence.",
            Foreground = Brushes.LightGray,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
        };
        sp.Children.Add(descBlock);

        var sampleBox = new TextBox
        {
            Text = "Type inside this generic control...",
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
            Foreground = Brushes.White,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 8),
        };
        sp.Children.Add(sampleBox);

        var sampleBtn = new Button
        {
            Content = "Interactive Button",
            Padding = new Thickness(8, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        sp.Children.Add(sampleBtn);

        border.Child = sp;
        return border;
    }

    private void Button_AddCustomControl_Click(object? sender, RoutedEventArgs e)
    {
        var custom = CreateGenericAvaloniaControl($"Custom Pane #{PART_Multiplexer.PaneCount + 1}");
        PART_Multiplexer.SplitActive(custom, Orientation.Vertical);
    }

    private void Button_AddTuiTerminal_Click(object? sender, RoutedEventArgs e)
    {
        var tui = CreateDemoTerminal(new SimpleTuiDemoSession());
        PART_Multiplexer.SplitActive(tui, Orientation.Horizontal);
    }

    private void Button_SaveLayout_Click(object? sender, RoutedEventArgs e)
    {
        _savedLayout = PART_Multiplexer.GetLayoutState(c => c is TerminalControl ? "Terminal" : "Generic");
    }

    private void Button_RestoreLayout_Click(object? sender, RoutedEventArgs e)
    {
        if (_savedLayout == null)
            return;

        PART_Multiplexer.RestoreLayoutState(_savedLayout, id =>
            id == "Terminal" ? CreateDemoTerminal() : CreateGenericAvaloniaControl("Restored Pane"));
    }
}
