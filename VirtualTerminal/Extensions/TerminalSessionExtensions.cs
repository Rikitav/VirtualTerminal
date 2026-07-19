using System.Text;
using VirtualTerminal.Interfaces;

namespace VirtualTerminal.Extensions;

/// <summary>
/// Convenience extension methods for working with <see cref="ITerminalSession"/>:
/// sending text, clearing the screen, redirecting <see cref="Console.Out"/> into the terminal buffer,
/// and creating readers to read from the terminal buffer.
/// </summary>
public static class TerminalSessionExtensions
{
    // Terminals expect a single CR as the Enter key, not the OS newline (CRLF on Windows).
    private static readonly string _newLine = "\r";
    private static readonly string _clearSeq = "\x1b[2J\x1b[H";

    extension(Console)
    {
        /// <summary>
        /// Redirects <see cref="Console.Out"/> to the session buffer, so <see cref="Console.WriteLine(string?)"/> output becomes visible in the terminal UI.
        /// </summary>
        /// <param name="session"></param>
        public static void RedirectToSession(ITerminalSession session)
        {
            session.RedirectConsole();
        }

        /// <summary>
        /// Redirects <see cref="Console.In"/> to read from the session buffer.
        /// </summary>
        /// <param name="session"></param>
        public static void RedirectInputToSession(ITerminalSession session)
        {
            session.RedirectConsoleInput();
        }

        /// <summary>
        /// Redirects both <see cref="Console.In"/> and <see cref="Console.Out"/> to the session.
        /// </summary>
        /// <param name="session"></param>
        public static void RedirectConsoleToSession(ITerminalSession session)
        {
            session.RedirectFullConsole();
        }
    }

    /// <summary>
    /// Appends text to the session input, encoding it with <see cref="ITerminalSession.InputEncoding"/>.
    /// </summary>
    /// <param name="session">Target session.</param>
    /// <param name="text">Text to send.</param>
    public static void Append(this ITerminalSession session, string text)
    {
        byte[] data = session.InputEncoding.GetBytes(text);
        session.Write(data);
    }

    /// <summary>
    /// Appends a newline (Enter) to the session.
    /// </summary>
    public static void AppendLine(this ITerminalSession session)
    {
        session.Append(_newLine);
    }

    /// <summary>
    /// Appends text followed by a newline (Enter) to the session.
    /// </summary>
    /// <param name="session">Target session.</param>
    /// <param name="text">Text to send.</param>
    public static void AppendLine(this ITerminalSession session, string text)
    {
        session.Append(text);
        session.AppendLine();
    }

    /// <summary>
    /// Clears the terminal screen using an ANSI escape sequence (<c>ESC[2J ESC[H</c>).
    /// </summary>
    /// <param name="session">Target session.</param>
    public static void Clear(this ITerminalSession session)
    {
        session.Append(_clearSeq);
    }

    /// <summary>
    /// Creates a <see cref="TextWriter"/> that writes directly into <see cref="ITerminalSession.Buffer"/>.
    /// </summary>
    /// <param name="session">Target session.</param>
    /// <returns>A writer that writes to the terminal buffer.</returns>
    public static TextWriter CreateBufferWriter(this ITerminalSession session)
    {
        return new BufferStreamWriter(session.Buffer, session.Decoder);
    }

    /// <summary>
    /// Creates a <see cref="TextReader"/> that reads directly from <see cref="ITerminalSession.Buffer"/>.
    /// </summary>
    /// <param name="session">Target session.</param>
    /// <returns>A reader that reads from the terminal buffer.</returns>
    public static TextReader CreateBufferReader(this ITerminalSession session)
    {
        return new BufferStreamReader(session);
    }

    /// <summary>
    /// Redirects <see cref="Console.Out"/> to the session buffer, so <see cref="Console.WriteLine(string?)"/>
    /// output becomes visible in the terminal UI.
    /// </summary>
    /// <param name="session">Target session.</param>
    public static void RedirectConsole(this ITerminalSession session)
    {
        TextWriter writer = session.CreateBufferWriter();
        Console.SetOut(writer);
    }

    /// <summary>
    /// Redirects <see cref="Console.In"/> to read from the session buffer.
    /// </summary>
    /// <param name="session">Target session.</param>
    public static void RedirectConsoleInput(this ITerminalSession session)
    {
        TextReader reader = session.CreateBufferReader();
        Console.SetIn(reader);
    }

    /// <summary>
    /// Redirects both <see cref="Console.In"/> and <see cref="Console.Out"/> to the session.
    /// </summary>
    /// <param name="session">Target session.</param>
    public static void RedirectFullConsole(this ITerminalSession session)
    {
        RedirectConsole(session);
        RedirectConsoleInput(session);
    }

    /// <summary>
    /// Reads a string from the session input.
    /// </summary>
    /// <param name="session">Target session.</param>
    /// <returns>The read string, or empty if no data available.</returns>
    public static string ReadString(this ITerminalSession session)
    {
        byte[] data = session.ReadAll();
        return data.Length > 0 ? session.OutputEncoding.GetString(data) : string.Empty;
    }

    /// <summary>
    /// Reads a line from the session input.
    /// </summary>
    /// <param name="session">Target session.</param>
    /// <returns>The read line, or null if no complete line available.</returns>
    public static string? ReadLine(this ITerminalSession session)
    {
        StringBuilder line = new StringBuilder();
        bool lineBreakFound = false;

        while (session.AvailableDataLength > 0 && !lineBreakFound)
        {
            byte[] buffer = new byte[1];
            int bytesRead = session.Read(buffer);

            if (bytesRead > 0)
            {
                char c = session.OutputEncoding.GetChars(buffer, 0, 1)[0];

                if (c == '\r')
                {
                    // Skip carriage return, next character might be line feed
                    continue;
                }

                if (c == '\n')
                {
                    lineBreakFound = true;
                }
                else
                {
                    line.Append(c);
                }
            }
        }

        return lineBreakFound ? line.ToString() : null;
    }
}
