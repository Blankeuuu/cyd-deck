using System.Text.Json.Serialization;

namespace StreamDeck.Shortcuts;

public sealed record ShortcutConfiguration
{
    [JsonPropertyName("shortcuts")]
    public IReadOnlyList<ShortcutDefinition> Shortcuts { get; init; } = [];
}

public sealed record ShortcutDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; init; } = string.Empty;

    [JsonPropertyName("displayMode")]
    public string DisplayMode { get; init; } = ShortcutDisplayModes.Label;

    [JsonPropertyName("action")]
    public ShortcutActionDefinition Action { get; init; } = new();
}

public sealed record ShortcutActionDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("executablePath")]
    public string? ExecutablePath { get; init; }

    [JsonPropertyName("arguments")]
    public IReadOnlyList<string> Arguments { get; init; } = [];

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("keys")]
    public IReadOnlyList<string> Keys { get; init; } = [];

    [JsonPropertyName("scriptPath")]
    public string? ScriptPath { get; init; }

    [JsonPropertyName("adapterKey")]
    public string? AdapterKey { get; init; }
}

public static class ShortcutDisplayModes
{
    public const string Label = "label";
    public const string Icon = "icon";
    public const string Both = "both";

    public static bool IsKnown(string? value)
    {
        return value is Label or Icon or Both;
    }
}

public sealed record DeviceShortcutDefinition(string Id, string Name, string Icon, string DisplayMode);

public sealed record DeviceShortcutConfiguration(IReadOnlyList<DeviceShortcutDefinition> Shortcuts);

public sealed record ShortcutExecuteRequest(string Id);
