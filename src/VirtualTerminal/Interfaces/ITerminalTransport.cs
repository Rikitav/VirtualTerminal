using System.IO.Pipelines;

namespace VirtualTerminal.Interfaces;

/// <summary>
/// Represents a bidirectional, asynchronous byte transport (such as a pseudo-terminal,
/// SSH channel, socket, or in-memory pipe) connected to a terminal session.
/// </summary>
public interface ITerminalTransport : IAsyncDisposable, IDisposable
{
    /// <summary>Gets the pipe reader delivering incoming byte stream from the host/process into the terminal decoder.</summary>
    PipeReader Input { get; }

    /// <summary>Gets the pipe writer delivering outgoing byte stream from user input into the host/process.</summary>
    PipeWriter Output { get; }

    /// <summary>Gets a value indicating whether the transport is actively connected.</summary>
    bool IsConnected { get; }

    /// <summary>Asynchronously opens or connects the transport.</summary>
    ValueTask ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Asynchronously closes or disconnects the transport.</summary>
    ValueTask DisconnectAsync(CancellationToken cancellationToken = default);
}
