using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VirtualTerminal.Controls;
using VirtualTerminal.Cursors;
using VirtualTerminal.Interfaces;
using VirtualTerminal.Session;

namespace VirtualTerminal.TestApp;

public partial class MainWindow : Window
{
    private MultiplexerLayoutNode? _savedLayout;

    /// <summary>
    /// MainWindow demonstrating generic control multiplexing, smooth animations, public APIs, and command bindings.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        SetupMultiplexerDemo();
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

        // 3. Split the right pane horizontally with a generic non-terminal WPF control
        var genericControl = CreateGenericWpfControl("Editor & Inspector Pane");
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
            var term2 = CreateGenericWpfControl("Test Pane 2");
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

            if (!PART_Multiplexer.RestoreLayoutState(layout, id => id == "Terminal" ? CreateDemoTerminal() : CreateGenericWpfControl("Restored")))
                throw new InvalidOperationException("Failed to restore layout state");

            Console.WriteLine("SPLIT_TEST_SUCCESS");
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
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            CursorRenderer = new LandingTerminalCursorRenderer(),
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

    private UIElement CreateGenericWpfControl(string title)
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
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
        };
        sp.Children.Add(titleBlock);

        var descBlock = new TextBlock
        {
            Text = "The MultiplexerControl tiles and manages ANY WPF control (Buttons, TextBoxes, Canvases, UserControls) with smooth animated splits and layout persistence.",
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

    private void Button_AddCustomControl_Click(object sender, RoutedEventArgs e)
    {
        var custom = CreateGenericWpfControl($"Custom Pane #{PART_Multiplexer.PaneCount + 1}");
        PART_Multiplexer.SplitActive(custom, Orientation.Vertical);
    }

    private void Button_AddTuiTerminal_Click(object sender, RoutedEventArgs e)
    {
        var tui = CreateDemoTerminal(new SimpleTuiDemoSession());
        PART_Multiplexer.SplitActive(tui, Orientation.Horizontal);
    }

    private void Button_SaveLayout_Click(object sender, RoutedEventArgs e)
    {
        _savedLayout = PART_Multiplexer.GetLayoutState(c => c is TerminalControl ? "Terminal" : "Generic");
        MessageBox.Show("Multiplexer layout captured successfully!", "Layout Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Button_RestoreLayout_Click(object sender, RoutedEventArgs e)
    {
        if (_savedLayout == null)
        {
            MessageBox.Show("Please save a layout first.", "No Layout Saved", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        PART_Multiplexer.RestoreLayoutState(_savedLayout, id =>
            id == "Terminal" ? CreateDemoTerminal() : CreateGenericWpfControl("Restored Pane"));
    }
}
