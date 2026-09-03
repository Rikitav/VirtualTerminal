using System.Windows;
using VirtualTerminal.Cursors;

namespace VirtualTerminal.TestApp;

public partial class MainWindow : Window
{
    /// <summary>
    /// MainWindow of testing application
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        // Demo: animated cursor that glides between cells (cosine ease) instead of teleporting.
        PART_Terminal_Left.CursorRenderer = new AnimatedTerminalCursorRenderer() { MaxGlideCells = 9999999 };
    }

    private void Button_Click_Start(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PART_CommandLineName.Text))
            return;

        try
        {
            PART_Terminal_Left.Session?.Dispose();

            CommandLineSession session = new CommandLineSession(PART_CommandLineName.Text);
            //SimpleTuiDemoSession session = new SimpleTuiDemoSession();

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
