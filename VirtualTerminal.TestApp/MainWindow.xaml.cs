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

    /// <inheritdoc/>
    protected override async void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);

        CommandLineSession session = new CommandLineSession("cmd.exe");
        PART_Terminal_Left.Session = session;
        PART_Prompt_Left.Session = session;
    }
}