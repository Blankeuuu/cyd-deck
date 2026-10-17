using System.Text.Json.Serialization;
using StreamDeck.Shortcuts;

namespace StreamDeck.Configuration;

public sealed record AgentConfiguration
{
    [JsonPropertyName("server")]
    public ServerConfiguration? Server { get; init; } = new();

    [JsonPropertyName("monitoring")]
    public MonitoringConfiguration? Monitoring { get; init; } = new();

    [JsonPropertyName("covers")]
    public CoversConfiguration? Covers { get; init; } = new();

    [JsonPropertyName("desktopAppearance")]
    public DesktopAppearanceConfiguration? DesktopAppearance { get; init; } = new();

    [JsonPropertyName("appearance")]
    public AppearanceConfiguration? Appearance { get; init; } = new();

    [JsonPropertyName("activeProfile")]
    public string ActiveProfile { get; init; } = "Default";

    [JsonPropertyName("profiles")]
    public IReadOnlyList<DeckProfileConfiguration> Profiles { get; init; } = [];

    [JsonPropertyName("shortcuts")]
    public IReadOnlyList<ShortcutDefinition>? Shortcuts { get; init; } = [];

    public static AgentConfiguration CreateDefault()
    {
        return new AgentConfiguration();
    }
}

public sealed record DesktopAppearanceConfiguration
{
    public bool Clock24Hour { get; init; } = true;
    [JsonPropertyName("theme")]
    public string Theme { get; init; } = "dark";

    [JsonPropertyName("windowStyle")]
    public string WindowStyle { get; init; } = "acrylic";

    [JsonPropertyName("accent")]
    public string Accent { get; init; } = "#0A84FF";
}

public sealed record ServerConfiguration
{
    [JsonPropertyName("usbPort")]
    public string? UsbPort { get; init; }

    [JsonPropertyName("usbAutoConnect")]
    public bool UsbAutoConnect { get; init; } = true;

    [JsonPropertyName("port")]
    public int Port { get; init; } = 8765;
}

public sealed record MonitoringConfiguration
{
    [JsonPropertyName("intervalMs")]
    public int IntervalMs { get; init; } = 1000;
}

public sealed record CoversConfiguration
{
    [JsonPropertyName("size")]
    public int Size { get; init; } = 140;

    [JsonPropertyName("jpegQuality")]
    public int JpegQuality { get; init; } = 80;
}

public sealed record AppearanceConfiguration
{
    [JsonPropertyName("brightnessRevision")]
    public string? BrightnessRevision { get; init; }

    [JsonPropertyName("theme")]
    public string Theme { get; init; } = "dark";

    [JsonPropertyName("accent")]
    public string Accent { get; init; } = "#7EAFE3";

    [JsonPropertyName("background")]
    public string Background { get; init; } = "#101216";

    [JsonPropertyName("buttonStyle")]
    public string ButtonStyle { get; init; } = "rounded";

    [JsonPropertyName("tileColorScheme")]
    public string TileColorScheme { get; init; } = "control-center";

    [JsonPropertyName("standbyStyle")]
    public string StandbyStyle { get; init; } = "apple-standby";

    [JsonPropertyName("shortcutsLayout")]
    public string ShortcutsLayout { get; init; } = "auto";

    [JsonPropertyName("pageOrder")]
    public IReadOnlyList<string> PageOrder { get; init; } = ["media", "shortcuts", "pc", "info"];

    [JsonPropertyName("pageVisibility")]
    public IReadOnlyList<string> PageVisibility { get; init; } = ["media", "shortcuts", "pc", "info"];

    [JsonPropertyName("brightness")]
    public int Brightness { get; init; } = 70;

    [JsonPropertyName("dimAfterSec")]
    public int DimAfterSec { get; init; } = 300;

    [JsonPropertyName("dimBrightness")]
    public int DimBrightness { get; init; } = 20;

    [JsonPropertyName("screenOffAfterSec")]
    public int ScreenOffAfterSec { get; init; } = 1800;

    [JsonPropertyName("wakeOnTouch")]
    public bool WakeOnTouch { get; init; } = true;
}

public sealed record DeckProfileConfiguration(string Name, IReadOnlyList<ShortcutDefinition> Shortcuts, IReadOnlyList<string?>? AssignedShortcutIds = null);
