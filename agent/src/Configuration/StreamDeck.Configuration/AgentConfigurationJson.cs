using System.Text.Json;

namespace StreamDeck.Configuration;

public static class AgentConfigurationJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static AgentConfiguration Deserialize(string json)
    {
        try
        {
            var configuration = JsonSerializer.Deserialize<AgentConfiguration>(json, Options)
                ?? throw new AgentConfigurationException([new ConfigurationValidationIssue("$", "Configuration JSON was empty.")]);
            return ConfigurationColorNormalizer.Normalize(configuration);
        }
        catch (JsonException exception)
        {
            throw new AgentConfigurationException([new ConfigurationValidationIssue("$", $"Configuration JSON is malformed: {exception.Message}")]);
        }
    }

    public static string Serialize(AgentConfiguration configuration)
    {
        return JsonSerializer.Serialize(configuration, Options);
    }
}
