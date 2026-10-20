using System.Text.Encodings.Web;
using System.Text.Json;

namespace StreamDeck.Shortcuts;

public static class ShortcutJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static ShortcutConfiguration DeserializeConfiguration(string json)
    {
        return JsonSerializer.Deserialize<ShortcutConfiguration>(json, Options)
            ?? throw new ShortcutConfigurationException([new ShortcutValidationIssue("$", "Configuration JSON was empty.")]);
    }

    public static string SerializeConfiguration(ShortcutConfiguration configuration)
    {
        return JsonSerializer.Serialize(configuration, Options);
    }

    public static string SerializeDeviceConfiguration(DeviceShortcutConfiguration configuration)
    {
        return JsonSerializer.Serialize(configuration, Options);
    }
}
