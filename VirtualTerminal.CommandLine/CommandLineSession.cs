using System;
using System.Runtime.InteropServices;
using System.Text;
using VirtualTerminal.Interop;
using VirtualTerminal.Session;

namespace VirtualTerminal;

/// <summary>
/// A <see cref="TerminalSession"/> implementation backed by Windows ConPTY (pseudo console).
/// It starts a child process attached to a pseudoconsole and forwards IO between the process and
/// the session <see cref="TerminalSession.Buffer"/>.
/// </summary>
public sealed partial class CommandLineSession : TerminalSession
{
    private CancellationTokenSource readLoopToken;
    private PseudoConsole pseudoConsole;

    /// <inheritdoc />
    public override string Title => "ConPTY";

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
        pseudoConsole = PseudoConsoleFactory.Start(Buffer, new ProcessCreationInfo()
        {
            CommandLine = application
        });

        readLoopToken = new CancellationTokenSource();
        Task.Factory.StartNew(ReadOutputLoop, TaskCreationOptions.LongRunning);
    }

    /// <summary>
    /// Starts a default ConPTY session using <c>cmd.exe</c>.
    /// </summary>
    public CommandLineSession()
        : this(@"C:\Windows\System32\cmd.exe") { }

    /// <inheritdoc />
    public override void Resize(int columns, int rows)
    {
        Buffer.ResizeBuffer(columns, rows);
        PseudoConsole.Resize(columns, rows);
    }

    private async Task ReadOutputLoop()
    {
        if (PseudoConsole?.Reader == null)
            return;

        await Task.Yield();
        byte[] data = new byte[1024];

        while (!readLoopToken.IsCancellationRequested)
        {
            try
            {
                int bytesRead = PseudoConsole.Reader.Read(data);
                if (bytesRead == 0)
                    break;

                byte[] conv = Encoding.Convert(InputEncoding, VirtualTerminalBuffer.Encoding, data, 0, bytesRead);
                Buffer.Write(conv);
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
