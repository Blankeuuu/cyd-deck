namespace StreamDeck.Shortcuts;

public sealed class ShortcutCatalog
{
    private readonly IReadOnlyDictionary<string, ShortcutDefinition> shortcuts;
    private ShortcutCatalog? replacement;

    private ShortcutCatalog(ShortcutConfiguration configuration)
    {
        shortcuts = configuration.Shortcuts.ToDictionary(shortcut => shortcut.Id, StringComparer.OrdinalIgnoreCase);
        DeviceConfiguration = new DeviceShortcutConfiguration(
            configuration.Shortcuts
                .Select(shortcut => new DeviceShortcutDefinition(
                    shortcut.Id,
                    shortcut.Name,
                    shortcut.Icon,
                    NormalizeDisplayMode(shortcut.DisplayMode)))
                .ToArray());
    }

    private DeviceShortcutConfiguration initialDeviceConfiguration = new([]);
    public DeviceShortcutConfiguration DeviceConfiguration
    {
        get => Volatile.Read(ref replacement)?.DeviceConfiguration ?? initialDeviceConfiguration;
        private set => initialDeviceConfiguration = value;
    }

    public void Replace(ShortcutConfiguration configuration)
    {
        // Validate and build before atomically publishing the complete new catalog.
        Volatile.Write(ref replacement, Create(configuration));
    }

    public static ShortcutCatalog Create(ShortcutConfiguration configuration, ShortcutConfigurationValidator? validator = null)
    {
        validator ??= new ShortcutConfigurationValidator();
        var issues = validator.Validate(configuration);
        if (issues.Count > 0)
        {
            throw new ShortcutConfigurationException(issues);
        }

        return new ShortcutCatalog(configuration);
    }

    public bool TryGetAction(string id, out ShortcutActionDefinition action)
    {
        var current = Volatile.Read(ref replacement);
        if (current is not null) return current.TryGetAction(id, out action);
        action = new ShortcutActionDefinition();
        if (string.IsNullOrWhiteSpace(id) || !shortcuts.TryGetValue(id, out var shortcut))
        {
            return false;
        }

        action = shortcut.Action;
        return true;
    }

    private static string NormalizeDisplayMode(string? displayMode)
    {
        return ShortcutDisplayModes.IsKnown(displayMode) ? displayMode! : ShortcutDisplayModes.Label;
    }
}
