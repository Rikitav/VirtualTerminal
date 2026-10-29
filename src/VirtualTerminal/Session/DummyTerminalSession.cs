namespace VirtualTerminal.Session;

/// <summary>
/// Minimal <see cref="TerminalSession"/> implementation useful for testing UI and rendering without
/// an external backend (process/SSH/etc.).
/// </summary>
public sealed class DummyTerminalSession : TerminalSession
{
    /// <summary>Gets the session title.</summary>
    public override string Title => "Dummy :P";

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        // Im dummy :P
    }
}
