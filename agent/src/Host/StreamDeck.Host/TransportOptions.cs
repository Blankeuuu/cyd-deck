using System.Net;

namespace StreamDeck.Host;

public sealed record TransportOptions
{
    public IPAddress BindAddress { get; init; } = IPAddress.Loopback;

    public int Port { get; init; } = 8765;

    public string WebSocketPath { get; init; } = "/ws";

    public int MaxMessageBytes { get; init; } = 16 * 1024;

    public bool AllowRemoteClients { get; init; }
}
