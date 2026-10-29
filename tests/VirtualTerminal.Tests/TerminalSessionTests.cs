using VirtualTerminal.Buffer;
using VirtualTerminal.Session;

namespace VirtualTerminal.Tests;

public class TerminalSessionTests
{
    [Fact]
    public void SessionBuffer_IsSameInstanceAsDecoderBuffer()
    {
        using DummyTerminalSession session = new();

        TerminalScreenBuffer decoderBuffer = ((TerminalDecoder)session.Decoder).Buffer;
        TerminalScreenBuffer sessionBuffer = session.Buffer;

        Assert.Same(sessionBuffer, decoderBuffer);
    }

    [Fact]
    public void WritingToSession_UpdatesExposedBuffer()
    {
        using DummyTerminalSession session = new();

        session.Write("hello"u8);

        TerminalScreenBuffer buffer = session.Buffer;
        Assert.Equal('h', buffer.GetCell(0, 0).Character.Value);
        Assert.Equal('e', buffer.GetCell(0, 1).Character.Value);
    }
}
