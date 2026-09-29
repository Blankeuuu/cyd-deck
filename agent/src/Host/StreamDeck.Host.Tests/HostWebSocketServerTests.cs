using System.Net;
using Xunit;

namespace StreamDeck.Host.Tests;

public sealed class HostWebSocketServerTests
{
    [Fact]
    public void DefaultTransportPrefixBindsToLoopback()
    {
        var prefix = HostWebSocketServer.CreatePrefix(new TransportOptions());

        Assert.Equal("http://127.0.0.1:8765/", prefix);
    }

    [Fact]
    public void RemoteEndpointRequiresExplicitLanOptIn()
    {
        var options = new TransportOptions();

        Assert.True(HostWebSocketServer.IsAllowedRemoteEndPoint(new IPEndPoint(IPAddress.Loopback, 50000), options));
        Assert.False(HostWebSocketServer.IsAllowedRemoteEndPoint(new IPEndPoint(IPAddress.Parse("192.168.1.50"), 50000), options));
        Assert.True(HostWebSocketServer.IsAllowedRemoteEndPoint(new IPEndPoint(IPAddress.Parse("192.168.1.50"), 50000), options with { AllowRemoteClients = true }));
    }
}
