using StreamDeck.Configuration;
using Xunit;

namespace StreamDeck.Host.Tests;

public sealed class HostConfigurationMapperTests
{
    [Fact]
    public void MapsConfiguredPortWithoutExposingLanByDefault()
    {
        var options = HostConfigurationMapper.CreateTransportOptions(new AgentConfiguration
        {
            Server = new ServerConfiguration { Port = 9123 }
        });

        Assert.Equal(9123, options.Port);
        Assert.False(options.AllowRemoteClients);
    }
}
