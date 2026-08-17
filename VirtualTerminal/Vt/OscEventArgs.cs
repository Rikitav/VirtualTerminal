using System;

namespace VirtualTerminal.Vt;

/// <summary>
/// Arguments for an OSC 52 clipboard request.
/// </summary>
public sealed class ClipboardRequestEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new clipboard request.
    /// </summary>
    /// <param name="selection">The selection character (e.g. <c>c</c>, <c>p</c>, <c>q</c>, <c>s</c>).</param>
    /// <param name="data">The base64-encoded clipboard data, or <see langword="null"/> when the terminal is querying the clipboard.</param>
    public ClipboardRequestEventArgs(string selection, string? data)
    {
        Selection = selection;
        Data = data;
    }

    /// <summary>The clipboard selection identifier.</summary>
    public string Selection { get; }

    /// <summary>The base64-encoded data to write, or <see langword="null"/> for a query.</summary>
    public string? Data { get; }

    /// <summary><see langword="true"/> when the terminal is asking for the current clipboard contents.</summary>
    public bool IsQuery => Data is null;
}
