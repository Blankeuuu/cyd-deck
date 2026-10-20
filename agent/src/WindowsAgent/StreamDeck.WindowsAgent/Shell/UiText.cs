using System.Collections.Concurrent;
using System.Globalization;
using System.Resources;

namespace StreamDeck.WindowsAgent.Shell;

/// <summary>Shared resource lookup. Feature.Key maps to Localization/Feature.resx.</summary>
internal static class UiText
{
    private static readonly ConcurrentDictionary<string, ResourceManager> Managers = new();
    private static CultureInfo culture = CultureInfo.GetCultureInfo("pl-PL");
    public static string Language => culture.TwoLetterISOLanguageName;
    public static event EventHandler? LanguageChanged;

    public static void SetLanguage(string? language)
    {
        var next = CultureInfo.GetCultureInfo(language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? "en" : "pl-PL");
        if (culture.Name == next.Name) return;
        culture = next;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Get(string key)
    {
        var separator = key.IndexOf('.');
        if (separator <= 0 || separator == key.Length - 1) throw new ArgumentException("Use Feature.Key resource names.", nameof(key));
        var manager = Managers.GetOrAdd(key[..separator], feature => new ResourceManager(
            $"StreamDeck.WindowsAgent.Localization.{feature}", typeof(UiText).Assembly));
        return manager.GetString(key[(separator + 1)..], culture)
            ?? throw new MissingManifestResourceException($"Missing UI resource: {key}");
    }

    public static string Format(string key, params object?[] values) => string.Format(culture, Get(key), values);
}
