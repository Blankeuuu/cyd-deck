using System.Text.Json;

namespace StreamDeck.Shortcuts;

public static class ShortcutJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static ShortcutConfiguration DeserializeConfiguration(string json)
    {
        return JsonSerializer.Deserialize<ShortcutConfiguration>(json, Options)
            ?? throw new ShortcutConfigurationException([new ShortcutValidationIssue("$", "Configuration JSON was empty.")]);
    }

    public static string SerializeDeviceConfiguration(DeviceShortcutConfiguration configuration)
    {
        return JsonSerializer.Serialize(configuration, Options);
    }
}
