namespace StreamDeck.Shortcuts;

public sealed class ShortcutCatalog
{
    private readonly IReadOnlyDictionary<string, ShortcutDefinition> shortcuts;

    private ShortcutCatalog(ShortcutConfiguration configuration)
    {
        shortcuts = configuration.Shortcuts.ToDictionary(shortcut => shortcut.Id, StringComparer.OrdinalIgnoreCase);
        DeviceConfiguration = new DeviceShortcutConfiguration(
            configuration.Shortcuts
                .Select(shortcut => new DeviceShortcutDefinition(
                    shortcut.Id,
                    shortcut.Name,
                    shortcut.Icon,
                    shortcut.Action.Type))
                .ToArray());
    }

    public DeviceShortcutConfiguration DeviceConfiguration { get; }

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
        action = new ShortcutActionDefinition();
        if (string.IsNullOrWhiteSpace(id) || !shortcuts.TryGetValue(id, out var shortcut))
        {
            return false;
        }

        action = shortcut.Action;
        return true;
    }
}
