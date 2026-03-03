namespace VirtualTerminal.Engine.Components;

public interface IBufferedDecoder : IDecoder, ITerminalScreenView
{
    public TerminalScreenBuffer Buffer { get; }
    public ITerminalScreenView? OuterView { get; set; }
    public Coord CursorPosition { get; set; }
}
