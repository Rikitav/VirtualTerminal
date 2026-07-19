using System.Diagnostics;
using System.IO;
using System.Windows;

namespace VirtualTerminal.TestApp;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vt-debug.log");
        Trace.Listeners.Add(new TextWriterTraceListener(logPath));
        Trace.AutoFlush = true;
    }
}
