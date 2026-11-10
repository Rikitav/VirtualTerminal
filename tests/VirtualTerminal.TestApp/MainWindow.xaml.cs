using VirtualTerminal.Interfaces;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VirtualTerminal.Controls;
using VirtualTerminal.Cursors;
using VirtualTerminal.Session;

namespace VirtualTerminal.TestApp;

public partial class MainWindow : Window
{
    /// <summary>
    /// MainWindow of testing application demonstrating multiplexing, tabs, splits, and terminal sessions.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        // Classic pane cursor
        PART_Terminal_Left.CursorRenderer = new LandingTerminalCursorRenderer();

        // Setup Multiplexer Demo
        SetupMultiplexerDemo();
    }

    private void SetupMultiplexerDemo()
    {
        PART_Multiplexer.TerminalControlFactory = () => CreateDemoTerminal();

        // Close initial default blank tab
        if (PART_Multiplexer.TabControl.Tabs.Count > 0)
        {
            PART_Multiplexer.TabControl.CloseTab(PART_Multiplexer.TabControl.Tabs[0]);
        }

        // Tab 1: PowerShell shell
        var shellTerm = CreateDemoTerminal();
        PART_Multiplexer.TabControl.AddTab("PowerShell", shellTerm);

        // Tab 2: Split workspace with TUI Demo and Shell
        var tuiTerm = CreateDemoTerminal(new SimpleTuiDemoSession());
        var splitShell = CreateDemoTerminal();
        var split = new TerminalSplitControl(tuiTerm, splitShell, Orientation.Vertical);
        PART_Multiplexer.TabControl.AddTab("Split Workspace (TUI + Shell)", split);

        // Select the split demo tab by default
        if (PART_Multiplexer.TabControl.Tabs.Count > 1)
        {
            PART_Multiplexer.TabControl.SelectedTab = PART_Multiplexer.TabControl.Tabs[1];
        }
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

    private void Button_NewTab_Click(object sender, RoutedEventArgs e)
    {
        PART_Multiplexer.AddTab();
    }

    private void Button_SplitVertical_Click(object sender, RoutedEventArgs e)
    {
        PART_Multiplexer.SplitActivePane(Orientation.Vertical);
    }

    private void Button_SplitHorizontal_Click(object sender, RoutedEventArgs e)
    {
        PART_Multiplexer.SplitActivePane(Orientation.Horizontal);
    }

    private void Button_ClosePane_Click(object sender, RoutedEventArgs e)
    {
        PART_Multiplexer.CloseActivePane();
    }

    private void Button_LaunchTui_Click(object sender, RoutedEventArgs e)
    {
        var tab = PART_Multiplexer.TabControl.SelectedTab;
        if (tab?.Content is TerminalSplitControl)
        {
            PART_Multiplexer.SplitActivePane(Orientation.Horizontal);
            if (PART_Multiplexer.ActiveTerminal != null)
                PART_Multiplexer.ActiveTerminal.Session = new SimpleTuiDemoSession();
        }
        else
        {
            PART_Multiplexer.SplitActivePane(Orientation.Vertical);
            if (PART_Multiplexer.ActiveTerminal != null)
                PART_Multiplexer.ActiveTerminal.Session = new SimpleTuiDemoSession();
        }
    }

    private void Button_Click_Start(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PART_CommandLineName.Text))
            return;

        try
        {
            PART_Terminal_Left.Session?.Dispose();
            CommandLineSession session = new CommandLineSession(PART_CommandLineName.Text);
            PART_Terminal_Left.Session = session;
            PART_Prompt_Left.Session = session;
        }
        catch
        {
        }
    }

    private void Button_Click_Invalidate(object sender, RoutedEventArgs e)
    {
        PART_Terminal_Left.InvalidateVisual();
    }
}
