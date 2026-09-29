using StreamDeck.Configuration;

namespace StreamDeck.Host;

public static class HostConfigurationMapper
{
    public static TransportOptions CreateTransportOptions(AgentConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new TransportOptions
        {
            Port = configuration.Server?.Port ?? AgentConfiguration.CreateDefault().Server!.Port
        };
    }
}
