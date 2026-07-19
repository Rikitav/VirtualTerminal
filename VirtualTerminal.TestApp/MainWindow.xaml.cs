using System.Windows;

namespace VirtualTerminal.TestApp;

public partial class MainWindow : Window
{
    /// <summary>
    /// MainWindow of testing application
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
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
