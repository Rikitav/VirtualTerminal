using System.Windows;
using System.Reflection;

namespace VirtualTerminal.TestApp;

public partial class MainWindow : Window
{
    /// <summary>
    /// MainWindow of testing application
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        SecureShellSession session = new SecureShellSession("shardscript.ru", 2222, "tim", "tim");
        PART_Terminal_Left.Session = session;
        PART_Prompt_Left.Session = session;

        _ = session.ConnectAsync();
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
        VirtualTerminalScreen? screen = PART_Terminal_Left.GetType().GetField("PART_Output", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(PART_Terminal_Left) as VirtualTerminalScreen;
        screen?.InvalidateVisual();
    }
}