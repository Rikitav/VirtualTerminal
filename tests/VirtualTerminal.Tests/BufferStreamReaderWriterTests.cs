using System.Text;
using VirtualTerminal.Buffer;
using VirtualTerminal.Session;

namespace VirtualTerminal.Tests;

public class BufferStreamReaderWriterTests
{
    private sealed class TestSession : TerminalSession
    {
        public void SendResponse(ReadOnlySpan<byte> data) => AddToInputQueue(data);

        protected override void Dispose(bool disposing) { }
    }

    [Fact]
    public void BufferStreamWriter_WritesTextToScreenBuffer()
    {
        using TestSession session = new();
        using BufferStreamWriter writer = new(session);

        writer.Write("Hello VT");

        Assert.Equal('H', session.Buffer.GetCell(0, 0).Character.Value);
        Assert.Equal('e', session.Buffer.GetCell(0, 1).Character.Value);
        Assert.Equal('l', session.Buffer.GetCell(0, 2).Character.Value);
        Assert.Equal('l', session.Buffer.GetCell(0, 3).Character.Value);
        Assert.Equal('o', session.Buffer.GetCell(0, 4).Character.Value);
        Assert.Equal(' ', session.Buffer.GetCell(0, 5).Character.Value);
        Assert.Equal('V', session.Buffer.GetCell(0, 6).Character.Value);
        Assert.Equal('T', session.Buffer.GetCell(0, 7).Character.Value);
    }

    [Fact]
    public void BufferStreamWriter_LargePayload_DoesNotStackOverflowAndWritesAccurately()
    {
        using TestSession session = new();
        using BufferStreamWriter writer = new(session);

        // 10,000 characters would easily overflow an unconditional stackalloc
        string large = new('X', 10000);
        writer.Write(large);

        // Just verify first character on row 0 was written without exception
        Assert.Equal('X', session.Buffer.GetCell(0, 0).Character.Value);
    }

    [Fact]
    public void BufferStreamReader_ReadsCharactersAndLinesCorrectly()
    {
        using TestSession session = new();
        using BufferStreamReader reader = new(session);

        session.SendResponse("Line 1\r\nLine 2\nLine 3"u8);

        Assert.Equal('L', (char)reader.Peek());
        Assert.Equal("Line 1", reader.ReadLine());
        Assert.Equal("Line 2", reader.ReadLine());

        Span<char> buf = stackalloc char[6];
        int read = reader.Read(buf);
        Assert.Equal(6, read);
        Assert.Equal("Line 3", new string(buf));
        Assert.Equal(-1, reader.Read());
    }

    [Fact]
    public void TerminalSession_RingBufferWrapAround_PreservesDataIntegrity()
    {
        using TestSession session = new();

        // Push and read multiple times across buffer boundaries to exercise wrap-around
        for (int i = 0; i < 50; i++)
        {
            byte[] chunk = Encoding.UTF8.GetBytes($"chunk-{i};");
            session.SendResponse(chunk);

            byte[] read = new byte[chunk.Length];
            int readBytes = session.Read(read);
            Assert.Equal(chunk.Length, readBytes);
            Assert.Equal(chunk, read);
        }

        Assert.Equal(0, session.AvailableDataLength);
    }

    [Fact]
    public void TerminalSession_ReadAll_ReturnsAllQueuedBytes()
    {
        using TestSession session = new();
        session.SendResponse("ABC"u8);
        session.SendResponse("DEF"u8);

        byte[] all = session.ReadAll();
        Assert.Equal("ABCDEF"u8.ToArray(), all);
        Assert.Equal(0, session.AvailableDataLength);
    }
}
