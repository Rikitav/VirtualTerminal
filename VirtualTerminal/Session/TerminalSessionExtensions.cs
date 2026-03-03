using System.IO;
using System.Windows.Input;
using VirtualTerminal.Interop;

namespace VirtualTerminal.Session;

/// <summary>
/// Convenience extension methods for working with <see cref="ITerminalSession"/>:
/// sending text, clearing the screen, and redirecting <see cref="Console.Out"/> into the terminal buffer.
/// </summary>
public static class TerminalSessionExtensions
{
    private static readonly string _newLine = KeyHelper.ConvertToVT(Key.Enter)!;
    private static readonly string _clearSeq = "\x1b[2J\x1b[H";

    /// <summary>
    /// Appends text to the session input, encoding it with <see cref="ITerminalSession.InputEncoding"/>.
    /// </summary>
    /// <param name="session">Target session.</param>
    /// <param name="text">Text to send.</param>
    public static void Append(this ITerminalSession session, string text)
    {
        /*
        Span<byte> data = stackalloc byte[text.Length * session.InputEncoding.GetCharSize()];
        int count = session.InputEncoding.GetBytes(text, data);

        if (count == 0)
            return;
        */

        Span<byte> data = session.InputEncoding.GetBytes(text);
        session.WriteInput(data);
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
    /// Redirects <see cref="Console.Out"/> to the session buffer, so <see cref="Console.WriteLine(string?)"/>
    /// output becomes visible in the terminal UI.
    /// </summary>
    /// <param name="session">Target session.</param>
    public static void RedirectConsole(this ITerminalSession session)
    {
        TextWriter writer = session.CreateBufferWriter();
        Console.SetOut(writer);
    }
}
