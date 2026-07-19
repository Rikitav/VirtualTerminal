using System.Diagnostics;
using System.IO;
using System.Text;
using VirtualTerminal.Extensions;
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
    /// <param name="process"></param>
    public CommandLineSession(ProcessCreationInfo process) : base(Encoding.UTF8)
    {
        Title = Path.GetFileNameWithoutExtension(process.CommandLine ?? process.ApplicationName ?? string.Empty).ToLower();

        readLoopToken = new CancellationTokenSource();
        pseudoConsole = PseudoConsoleFactory.Start(Buffer, process);

        _ = Task.Factory.StartNew(ReadOutputLoop, TaskCreationOptions.LongRunning);
    }
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
            CommandLine = application,
            CurrentDirectory = Path.GetDirectoryName(application)
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
        // For ConPTY we let the pseudo-console own reflow. Resize only the local buffer
        // geometry without moving rows into scrollback; ConPTY will repaint/overwrite
        // the visible area itself, so avoid a full clear that causes blank-frame flicker.
        base.Resize(columns, rows, pushScrollback: false);
        PseudoConsole.Resize(columns, rows);
    }

    private async Task ReadOutputLoop()
    {
        if (PseudoConsole?.Reader == null)
            return;

        await Task.Yield();
        Span<byte> data = stackalloc byte[8192];

        try
        {
            while (readLoopToken?.IsCancellationRequested is false)
            {
                try
                {
                    int bytesRead = PseudoConsole.Reader.Read(data);
                    if (bytesRead == 0)
                        break;

                    ReadOnlySpan<byte> readed = data.Slice(0, bytesRead);
                    Decoder.WriteFromEncoding(InputEncoding, readed);
                    NotifyBufferUpdated();
                }
                catch (Exception exc)
                {
                    // Log so we can see when the read/decode loop misbehaves instead of silently looping.
                    Debug.WriteLine($"[{GetType().Name}] ReadOutputLoop inner error: {exc}");
                }
            }
        }
        catch (Exception exc)
        {
            Debug.WriteLine(exc);
            throw;
        }
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> data)
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
