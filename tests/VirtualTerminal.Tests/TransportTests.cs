using System.Buffers;
using System.Text;
using VirtualTerminal.Interfaces;
using VirtualTerminal.Session;
using VirtualTerminal.Transport;

namespace VirtualTerminal.Tests;

public class TransportTests
{
    private sealed class TestSession : TerminalSession
    {
        public TestSession(ITerminalTransport? transport = null) : base(transport) { }
        protected override void Dispose(bool disposing) { }
    }

    [Fact]
    public async Task CreateInMemoryPair_AllowsBidirectionalCommunication()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, server) = StreamTerminalTransport.CreateInMemoryPair();

        byte[] payload = "Hello Transport"u8.ToArray();
        await client.Output.WriteAsync(payload, ct);
        await client.Output.FlushAsync(ct);

        var readResult = await server.Input.ReadAsync(ct);
        Assert.Equal(payload, BuffersExtensions.ToArray(readResult.Buffer));
        server.Input.AdvanceTo(readResult.Buffer.End);

        await client.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task Session_WithAttachedTransport_IngestsStreamDirectlyIntoBuffer()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, server) = StreamTerminalTransport.CreateInMemoryPair();
        using TestSession session = new(client);

        bool updated = false;
        using AutoResetEvent waitEvent = new(false);
        session.BufferUpdated += (_, _) =>
        {
            updated = true;
            waitEvent.Set();
        };

        // Write VT sequence from "server" (child process / remote) to client transport input
        await server.Output.WriteAsync("Hello Pipe\r\n"u8.ToArray(), ct);
        await server.Output.FlushAsync(ct);

        Assert.True(waitEvent.WaitOne(TimeSpan.FromSeconds(2)), "BufferUpdated should fire within 2 seconds.");
        Assert.True(updated);

        Assert.Equal('H', session.Buffer.GetCell(0, 0).Character.Value);
        Assert.Equal('e', session.Buffer.GetCell(0, 1).Character.Value);
        Assert.Equal('l', session.Buffer.GetCell(0, 2).Character.Value);

        await server.DisposeAsync();
    }

    [Fact]
    public async Task Session_Write_SendsBytesToAttachedTransportOutput()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, server) = StreamTerminalTransport.CreateInMemoryPair();
        using TestSession session = new(client);

        session.Write("User Input"u8);

        var readResult = await server.Input.ReadAsync(ct);
        Assert.Equal("User Input", Encoding.UTF8.GetString(BuffersExtensions.ToArray(readResult.Buffer)));
        server.Input.AdvanceTo(readResult.Buffer.End);

        await server.DisposeAsync();
    }

    [Fact]
    public async Task Session_DetachTransport_StopsPipelinePumpCleanly()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, server) = StreamTerminalTransport.CreateInMemoryPair();
        using TestSession session = new();

        await session.AttachTransportAsync(client, ct);
        Assert.Same(client, session.Transport);

        await session.DetachTransportAsync();
        Assert.Null(session.Transport);

        await client.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task Session_TransportDisconnect_RaisesDisconnectedEvent()
    {
        var (client, server) = StreamTerminalTransport.CreateInMemoryPair();
        using TestSession session = new(client);

        bool disconnected = false;
        using AutoResetEvent waitEvent = new(false);
        session.Disconnected += (_, _) =>
        {
            disconnected = true;
            waitEvent.Set();
        };

        // Complete server output to signal EOF on client input
        await server.Output.CompleteAsync();

        Assert.True(waitEvent.WaitOne(TimeSpan.FromSeconds(2)), "Disconnected event should fire on stream EOF.");
        Assert.True(disconnected);

        await server.DisposeAsync();
    }
}
