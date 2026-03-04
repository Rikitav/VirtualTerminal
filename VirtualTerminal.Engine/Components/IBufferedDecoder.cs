namespace VirtualTerminal.Engine.Components;

public interface IBufferedDecoder : ITerminalDecoder
{
    public TerminalScreenBuffer Buffer { get; }
}
