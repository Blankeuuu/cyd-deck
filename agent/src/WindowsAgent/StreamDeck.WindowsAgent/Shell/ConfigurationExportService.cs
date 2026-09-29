using System.Text.Json;
using System.Text.Json.Serialization;
using StreamDeck.Configuration;

namespace StreamDeck.WindowsAgent.Shell;

internal static class ConfigurationExportService
{
    private const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Export(AgentConfiguration configuration)
    {
        return JsonSerializer.Serialize(new ConfigurationExport(CurrentFormatVersion, configuration), JsonOptions);
    }

    public static AgentConfiguration Import(string json)
    {
        ConfigurationExport? export;
        try
        {
            export = JsonSerializer.Deserialize<ConfigurationExport>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new AgentConfigurationException([new ConfigurationValidationIssue("$", $"Configuration export is malformed: {exception.Message}")]);
        }

        if (export is null)
        {
            throw new AgentConfigurationException([new ConfigurationValidationIssue("$", "Configuration export was empty.")]);
        }

        if (export.FormatVersion != CurrentFormatVersion)
        {
            throw new AgentConfigurationException([new ConfigurationValidationIssue("$.formatVersion", "Unsupported configuration export version.")]);
        }

        if (export.Configuration is null)
        {
            throw new AgentConfigurationException([new ConfigurationValidationIssue("$.configuration", "Configuration payload is required.")]);
        }

        var issues = new AgentConfigurationValidator().Validate(export.Configuration);
        if (issues.Count > 0)
        {
            throw new AgentConfigurationException(issues);
        }

        return export.Configuration;
    }

    private sealed record ConfigurationExport(
        [property: JsonPropertyName("formatVersion")] int FormatVersion,
        [property: JsonPropertyName("configuration")] AgentConfiguration? Configuration);
}
