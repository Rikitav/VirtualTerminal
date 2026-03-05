using System.Diagnostics;
using System.IO;
using System.Text;
using VirtualTerminal.Engine;
using VirtualTerminal.Interop;
using VirtualTerminal.Session;

namespace VirtualTerminal;

/// <summary>
/// A <see cref="TerminalSession"/> implementation backed by Windows ConPTY (pseudo console).
/// It starts a child process attached to a pseudoconsole and forwards IO between the process and
/// the session <see cref="TerminalSession.Buffer"/>.
/// </summary>
public sealed class CommandLineSession : TerminalSession
{
    private CancellationTokenSource readLoopToken;
    private PseudoConsole pseudoConsole;

    /// <inheritdoc />
    public override string Title { get; }

    /// <summary>
    /// Gets the underlying ConPTY wrapper (process + pipes).
    /// </summary>
    public PseudoConsole PseudoConsole => pseudoConsole;

    /// <summary>
    /// Starts a ConPTY session running the provided command line.
    /// </summary>
    /// <param name="application">Command line to start (for example, <c>cmd.exe</c> or PowerShell).</param>
    public CommandLineSession(string application) : base(Encoding.UTF8)
    {
        Title = Path.GetFileNameWithoutExtension(application).ToLower();

        readLoopToken = new CancellationTokenSource();
        pseudoConsole = PseudoConsoleFactory.Start(Buffer, new ProcessCreationInfo()
        {
            CommandLine = application
        });

        _ = Task.Factory.StartNew(ReadOutputLoop, TaskCreationOptions.LongRunning);
    }

    /// <summary>
    /// Starts a default ConPTY session using <c>cmd.exe</c>.
    /// </summary>
    public CommandLineSession()
        : this(@"C:\Windows\System32\cmd.exe") { }

    /// <inheritdoc />
    public override void Resize(ushort columns, ushort rows)
    {
        PseudoConsole.Resize(columns, rows);
        base.Resize(columns, rows);
    }

    private async Task ReadOutputLoop()
    {
        if (PseudoConsole?.Reader == null)
            return;

        await Task.Yield();
        Span<byte> data = stackalloc byte[1024];
        Span<byte> cleanupData = stackalloc byte[2];

        while (!readLoopToken.IsCancellationRequested)
        {
            try
            {
                int bytesRead = PseudoConsole.Reader.Read(data);
                if (bytesRead == 0)
                    break;

                ReadOnlySpan<byte> readed = data.Slice(0, bytesRead);
                if (readed is [27, 91, 63, 50, 53, 108, 27, 91, 50, 74, 27, 91, 109, 27, 91, 72, ..])
                {
                    while (!readLoopToken.IsCancellationRequested)
                    {
                        bytesRead = PseudoConsole.Reader.Read(cleanupData);
                        if (bytesRead == 0)
                            break;

                        if (cleanupData is not [10, 13, ..] && cleanupData is not [13, 10, ..])
                            break;
                    }

                    Decoder.WriteFromEncoding(InputEncoding, cleanupData);
                    NotifyBufferUpdated();
                    continue;
                }
#if DEBUG
                string dataStr = InputEncoding.GetString(readed);
                Debug.WriteLine(dataStr);
#endif
                Decoder.WriteFromEncoding(InputEncoding, readed);
                NotifyBufferUpdated();
            }
            catch
            {
                // fucked up somewhere
                _ = 0xBAD + 0xC0DE;
            }
        }
    }

    /// <inheritdoc />
    public override void WriteInput(ReadOnlySpan<byte> data)
    {
        if (PseudoConsole?.Writer == null)
            return;

        try
        {
            PseudoConsole.Writer.Write(data);
            NotifyBufferUpdated();
        }
        catch
        {
            // fucked up somewhere
            _ = 0xBAD + 0xC0DE;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        readLoopToken?.Cancel();
        readLoopToken?.Dispose();
        readLoopToken = null!;

        pseudoConsole?.Dispose();
        pseudoConsole = null!;
    }
}
