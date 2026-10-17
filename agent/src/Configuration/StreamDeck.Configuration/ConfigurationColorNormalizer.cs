using System.Drawing;
using StreamDeck.Shortcuts;

namespace StreamDeck.Configuration;

public static class ConfigurationColorNormalizer
{
    public static string? Normalize(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Equals("default", StringComparison.OrdinalIgnoreCase) || value.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return null;
        value = value.Trim();
        if (value.StartsWith('#') && value.Skip(1).All(Uri.IsHexDigit)) {
            if (value.Length == 7) return value.ToUpperInvariant();
            if (value.Length == 9) return "#" + value[3..].ToUpperInvariant();
            if (value.Length == 4) return "#" + string.Concat(value.Skip(1).Select(c => new string(char.ToUpperInvariant(c), 2)));
        }
        var named = Color.FromName(value);
        return named.IsKnownColor ? $"#{named.R:X2}{named.G:X2}{named.B:X2}" : value;
    }
    public static AgentConfiguration Normalize(AgentConfiguration configuration)
    {
        if (configuration is null) return null!;
        IReadOnlyList<ShortcutDefinition> Shortcuts(IReadOnlyList<ShortcutDefinition> values) => values.Select(s => s is null ? s! : s with { Color = Normalize(s.Color) }).ToArray();
        return configuration with {
            Shortcuts = configuration.Shortcuts is null ? null : Shortcuts(configuration.Shortcuts),
            Profiles = configuration.Profiles?.Select(p => p is null ? p! : p with { Shortcuts = p.Shortcuts is null ? null! : Shortcuts(p.Shortcuts) }).ToArray()!,
            Appearance = configuration.Appearance is not { } a ? null : a with { Accent = Normalize(a.Accent) ?? a.Accent, Background = Normalize(a.Background) ?? a.Background },
            DesktopAppearance = configuration.DesktopAppearance is not { } d ? null : d with { Accent = Normalize(d.Accent) ?? d.Accent }
        };
    }
}
