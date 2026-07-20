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
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
