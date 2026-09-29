using StreamDeck.Shortcuts;

namespace StreamDeck.Configuration;

public sealed class AgentConfigurationValidator
{
    private static readonly string[] ValidPages = ["media", "shortcuts", "pc", "info"];
    private static readonly HashSet<string> ValidPageSet = new(ValidPages, StringComparer.Ordinal);
    private static readonly HashSet<string> ValidThemes = new(["dark", "light"], StringComparer.Ordinal);
    private static readonly HashSet<string> ValidButtonStyles = new(["flat", "rounded", "pill"], StringComparer.Ordinal);

    private readonly ShortcutConfigurationValidator _shortcutValidator;

    public AgentConfigurationValidator(ShortcutConfigurationValidator? shortcutValidator = null)
    {
        _shortcutValidator = shortcutValidator ?? new ShortcutConfigurationValidator();
    }

    public IReadOnlyList<ConfigurationValidationIssue> Validate(AgentConfiguration? configuration)
    {
        var issues = new List<ConfigurationValidationIssue>();

        if (configuration is null)
        {
            issues.Add(new ConfigurationValidationIssue("$", "Configuration is required."));
            return issues;
        }

        if (configuration.Server is null)
        {
            issues.Add(new ConfigurationValidationIssue("$.server", "Server configuration is required."));
        }
        else
        {
            ValidateRange(configuration.Server.Port, 1, 65535, "$.server.port", "Server port must be between 1 and 65535.", issues);
        }

        if (configuration.Monitoring is null)
        {
            issues.Add(new ConfigurationValidationIssue("$.monitoring", "Monitoring configuration is required."));
        }
        else
        {
            ValidateRange(configuration.Monitoring.IntervalMs, 250, 60_000, "$.monitoring.intervalMs", "Monitoring interval must be between 250 and 60000 ms.", issues);
        }

        if (configuration.Covers is null)
        {
            issues.Add(new ConfigurationValidationIssue("$.covers", "Covers configuration is required."));
        }
        else
        {
            ValidateRange(configuration.Covers.Size, 32, 512, "$.covers.size", "Cover size must be between 32 and 512 px.", issues);
            ValidateRange(configuration.Covers.JpegQuality, 1, 100, "$.covers.jpegQuality", "JPEG quality must be between 1 and 100.", issues);
        }

        if (configuration.Appearance is null)
        {
            issues.Add(new ConfigurationValidationIssue("$.appearance", "Appearance configuration is required."));
        }
        else
        {
            ValidateAppearance(configuration.Appearance, issues);
        }

        if (configuration.Shortcuts is null)
        {
            issues.Add(new ConfigurationValidationIssue("$.shortcuts", "Shortcuts configuration is required."));
        }
        else
        {
            var shortcutIssues = _shortcutValidator.Validate(new ShortcutConfiguration { Shortcuts = configuration.Shortcuts });
            issues.AddRange(shortcutIssues.Select(issue => new ConfigurationValidationIssue(issue.Path, issue.Message)));
        }

        return issues;
    }

    private static void ValidateAppearance(AppearanceConfiguration appearance, List<ConfigurationValidationIssue> issues)
    {
        ValidateEnum(appearance.Theme, ValidThemes, "$.appearance.theme", "Appearance theme must be one of: dark, light.", issues);
        ValidateEnum(appearance.ButtonStyle, ValidButtonStyles, "$.appearance.buttonStyle", "Appearance button style must be one of: flat, rounded, pill.", issues);
        ValidateColor(appearance.Accent, "$.appearance.accent", "Appearance accent must be #RRGGBB.", issues);
        ValidateColor(appearance.Background, "$.appearance.background", "Appearance background must be #RRGGBB.", issues);
        ValidateRange(appearance.Brightness, 10, 100, "$.appearance.brightness", "Appearance brightness must be between 10 and 100.", issues);
        ValidateRange(appearance.DimAfterSec, 0, 86_400, "$.appearance.dimAfterSec", "Appearance dim timeout must be between 0 and 86400 seconds.", issues);
        ValidateRange(appearance.DimBrightness, 1, 100, "$.appearance.dimBrightness", "Appearance dim brightness must be between 1 and 100.", issues);
        if (appearance.DimBrightness > appearance.Brightness)
        {
            issues.Add(new ConfigurationValidationIssue("$.appearance.dimBrightness", "Appearance dim brightness must be less than or equal to brightness."));
        }

        ValidateRange(appearance.ScreenOffAfterSec, 0, 86_400, "$.appearance.screenOffAfterSec", "Appearance screen-off timeout must be between 0 and 86400 seconds.", issues);
        ValidatePageOrder(appearance.PageOrder, issues);
        ValidatePageVisibility(appearance.PageVisibility, issues);
    }

    private static void ValidateEnum(string? value, HashSet<string> allowedValues, string path, string message, List<ConfigurationValidationIssue> issues)
    {
        if (value is null || !allowedValues.Contains(value))
        {
            issues.Add(new ConfigurationValidationIssue(path, message));
        }
    }

    private static void ValidateColor(string? value, string path, string message, List<ConfigurationValidationIssue> issues)
    {
        if (value is null || value.Length != 7 || value[0] != '#' || !value.Skip(1).All(Uri.IsHexDigit))
        {
            issues.Add(new ConfigurationValidationIssue(path, message));
        }
    }

    private static void ValidatePageOrder(IReadOnlyList<string>? pages, List<ConfigurationValidationIssue> issues)
    {
        if (pages is null)
        {
            issues.Add(new ConfigurationValidationIssue("$.appearance.pageOrder", "Appearance page order is required."));
            return;
        }

        if (pages.Count != ValidPages.Length || pages.Distinct(StringComparer.Ordinal).Count() != ValidPages.Length || pages.Any(page => !ValidPageSet.Contains(page)))
        {
            issues.Add(new ConfigurationValidationIssue("$.appearance.pageOrder", "Appearance page order must include media, shortcuts, pc, and info exactly once."));
        }
    }

    private static void ValidatePageVisibility(IReadOnlyList<string>? pages, List<ConfigurationValidationIssue> issues)
    {
        if (pages is null)
        {
            issues.Add(new ConfigurationValidationIssue("$.appearance.pageVisibility", "Appearance page visibility is required."));
            return;
        }

        if (pages.Distinct(StringComparer.Ordinal).Count() != pages.Count || pages.Any(page => !ValidPageSet.Contains(page)))
        {
            issues.Add(new ConfigurationValidationIssue("$.appearance.pageVisibility", "Appearance page visibility may include only media, shortcuts, pc, and info once."));
        }

        if (!pages.Contains("info", StringComparer.Ordinal))
        {
            issues.Add(new ConfigurationValidationIssue("$.appearance.pageVisibility", "Info page must always be visible."));
        }
    }

    private static void ValidateRange(
        int value,
        int min,
        int max,
        string path,
        string message,
        List<ConfigurationValidationIssue> issues)
    {
        if (value < min || value > max)
        {
            issues.Add(new ConfigurationValidationIssue(path, message));
        }
    }
}
