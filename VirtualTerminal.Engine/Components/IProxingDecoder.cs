namespace VirtualTerminal.Engine.Components;

public interface IProxingDecoder : IBufferedDecoder, ITerminalScreenView
{
    public ITerminalScreenView? OuterView { get; set; }
}
