using System;
using System.IO;
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
    private readonly string _application;
    private readonly string _applicationName;

    private CancellationTokenSource readLoopToken;
    private PseudoConsole pseudoConsole;

    /// <inheritdoc />
    public override string Title => _application;

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
        _application = application;
        _applicationName = Path.GetFileNameWithoutExtension(application).ToLower();

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
        byte[] data = new byte[4096];

        while (!readLoopToken.IsCancellationRequested)
        {
            try
            {
                int bytesRead = PseudoConsole.Reader.Read(data);
                if (bytesRead == 0)
                    break;

                ReadOnlySpan<byte> readed = data.AsSpan(0, bytesRead);
                string dataStr = InputEncoding.GetString(readed);

                // checking for 'clear screen' sequence. Due to strange behaivour of this command, we required to handle it cutsomly
                COORD cursorPos = IsCmdClearScreen(readed);
                if (cursorPos != COORD.Invalid)
                {
                    Buffer.WriteFromEncoding(InputEncoding, readed);
                    Buffer.SetCursorPosition(cursorPos);
                    NotifyBufferUpdated();
                    continue;
                }

                Buffer.WriteFromEncoding(InputEncoding, readed);
                NotifyBufferUpdated();
            }
            catch
            {
                // fucked up somewhere
                _ = 0xBAD + 0xC0DE;
            }
        }
    }

    private COORD IsCmdClearScreen(ReadOnlySpan<byte> data)
    {
        try
        {
            // matching "H\u001[?25h"
            if (data is not [.., 72, 27, 91, 63, 50, 53, 104])
                return COORD.Invalid;

            int backing = data.Length - 1 - 7;
            for (int i = backing; i != backing - 6; i--)
            {
                // Trying to locate start of cursor jump escape sequence
                if (data[i..(i + 2)] is not [27, 91])
                    continue;

                // Moving position right for 2 bytes of "\u001["
                backing = i + 2;

                // Checking if jumping to default position
                if (data[backing] == 72) // H
                    return new COORD(0, 0);

                // Parsing indexes
                COORD cursorPos = COORD.Invalid;
                cursorPos.Y = TryFindIndex(data, ref backing, [72, 59]); // 'H', ';'
                cursorPos.X = TryFindIndex(data, ref backing, [72, 27]); // 'H', '\u001'

                return cursorPos;
            }

            // Invalid escape sequense or jump escape sequence not found
            return COORD.Invalid;
        }
        catch
        {
            // fucked up somewhere
            _ = 0xBAD + 0xC0DE;
            return COORD.Invalid;
        }
    }

    private short TryFindIndex(ReadOnlySpan<byte> data, ref int backing, params byte[] edge)
    {
        for (int j = 0; j < 3; j++)
        {
            if (!edge.Contains(data[backing + j]))
                continue;

            string indexStr = InputEncoding.GetString(data.Slice(backing, j));
            backing += j + 1;

            if (j == 0)
                return 0;

            if (!short.TryParse(indexStr, out short index))
                return -1;

            return (short)(index);
        }

        return -1;
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
