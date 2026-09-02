using Avalonia.Controls;
using Avalonia.Interactivity;
using VirtualTerminal.Cursors;

namespace VirtualTerminal.AvaloniaTestApp;

/// <summary>
/// Main window of the Avalonia test application hosting a terminal control and prompt box.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        // Demo: animated cursor that glides between cells (cosine ease) instead of teleporting.
        PART_Terminal_Left.CursorRenderer = new AnimatedTerminalCursorRenderer();
    }

    private void Button_Click_Start(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PART_CommandLineName.Text))
            return;

        try
        {
            PART_Terminal_Left.CurrentSession?.Dispose();

            //SimpleTuiDemoSession session = new();
            CommandLineSession session = new CommandLineSession(PART_CommandLineName.Text);

            PART_Terminal_Left.CurrentSession = session;
            PART_Prompt_Left.Session = session;
        }
        catch
        {
            // ignored in demo
        }
    }

    private void Button_Click_Invalidate(object? sender, RoutedEventArgs e)
    {
        PART_Terminal_Left.InvalidateVisual();
    }
}
