using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System.IO;
using System.Diagnostics;

namespace VirtualTerminal.AvaloniaTestApp;

/// <summary>
/// Avalonia application entry point for the test application.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Gets the command passed on the command line (via the first application argument) that should be
    /// auto-started by the main window, or <see langword="null"/> when no auto-start command was given.
    /// </summary>
    public static string? AutoStartCommand { get; private set; }

    /// <summary>
    /// Initializes the Avalonia application by loading XAML resources.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Called when the framework initialization is complete. Configures debug tracing and creates the main window.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vt-avalonia-debug.log");
        Trace.Listeners.Add(new TextWriterTraceListener(logPath));
        Trace.AutoFlush = true;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.Args is { Length: > 0 } args && !string.IsNullOrEmpty(args[0]))
                AutoStartCommand = args[0];

            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
