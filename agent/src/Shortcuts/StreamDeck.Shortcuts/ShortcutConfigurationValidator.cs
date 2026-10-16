using System.Text.RegularExpressions;

namespace StreamDeck.Shortcuts;

public sealed class ShortcutConfigurationValidator
{
    private static readonly Regex StableIdPattern = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.Compiled);
    private static readonly Regex DisplayTokenPattern = new("^[A-Za-z0-9][A-Za-z0-9 ._-]{0,63}$", RegexOptions.Compiled);
    private static readonly Regex AdapterKeyPattern = new("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "CTRL", "CONTROL", "ALT", "SHIFT", "WIN", "WINDOWS",
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
        "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "ENTER", "ESC", "ESCAPE", "TAB", "SPACE", "BACKSPACE", "DELETE", "INSERT",
        "HOME", "END", "PAGEUP", "PAGEDOWN", "UP", "DOWN", "LEFT", "RIGHT",
        "PRINTSCREEN", "PAUSE", "MEDIA_PLAY_PAUSE", "MEDIA_NEXT", "MEDIA_PREVIOUS",
        "VOLUME_UP", "VOLUME_DOWN", "VOLUME_MUTE"
    };

    public IReadOnlyList<ShortcutValidationIssue> Validate(ShortcutConfiguration? configuration)
    {
        var issues = new List<ShortcutValidationIssue>();
        if (configuration is null)
        {
            issues.Add(new ShortcutValidationIssue("$", "Configuration is required."));
            return issues;
        }

        if (configuration.Shortcuts is null)
        {
            issues.Add(new ShortcutValidationIssue("$.shortcuts", "Shortcuts list is required."));
            return issues;
        }

        if (configuration.Shortcuts.Count > 64)
        {
            issues.Add(new ShortcutValidationIssue("$.shortcuts", "At most 64 shortcuts can be configured."));
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < configuration.Shortcuts.Count; index++)
        {
            var shortcut = configuration.Shortcuts[index];
            var path = $"$.shortcuts[{index}]";
            ValidateShortcut(shortcut, path, issues);

            if (shortcut is not null && !string.IsNullOrWhiteSpace(shortcut.Id) && !ids.Add(shortcut.Id))
            {
                issues.Add(new ShortcutValidationIssue($"{path}.id", "Duplicate shortcut id."));
            }
        }

        return issues;
    }

    private static void ValidateShortcut(ShortcutDefinition? shortcut, string path, List<ShortcutValidationIssue> issues)
    {
        if (shortcut is null)
        {
            issues.Add(new ShortcutValidationIssue(path, "Shortcut entry is required."));
            return;
        }

        if (!StableIdPattern.IsMatch(shortcut.Id ?? string.Empty))
        {
            issues.Add(new ShortcutValidationIssue($"{path}.id", "Shortcut id must be a stable 1-64 character token."));
        }

        if (!DisplayTokenPattern.IsMatch(shortcut.Name ?? string.Empty))
        {
            issues.Add(new ShortcutValidationIssue($"{path}.name", "Name must be a non-empty display token up to 64 characters."));
        }

        if (!DisplayTokenPattern.IsMatch(shortcut.Icon ?? string.Empty))
        {
            issues.Add(new ShortcutValidationIssue($"{path}.icon", "Icon must be a non-empty token up to 64 characters."));
        }

        if (shortcut.IconData is not null) {
            var bytes = new byte[1728];
            if (shortcut.IconData.Length != 2304 || !Convert.TryFromBase64String(shortcut.IconData, bytes, out var count) || count != bytes.Length)
                issues.Add(new ShortcutValidationIssue($"{path}.iconData", "Icon must be a 24x24 RGB565A8 image."));
        }
        if (shortcut.IconUrl is not null && (!Uri.TryCreate(shortcut.IconUrl, UriKind.Absolute, out var iconUri) || iconUri.Scheme is not ("http" or "https") || shortcut.IconUrl.Length > 2048))
            issues.Add(new ShortcutValidationIssue($"{path}.iconUrl", "Icon URL must use HTTP or HTTPS."));

        if (!ShortcutDisplayModes.IsKnown(shortcut.DisplayMode))
        {
            issues.Add(new ShortcutValidationIssue($"{path}.displayMode", "Display mode must be label, icon or both."));
        }

        if (shortcut.Color is { } color && (color.Length != 7 || color[0] != '#' || !color.Skip(1).All(Uri.IsHexDigit)))
            issues.Add(new ShortcutValidationIssue($"{path}.color", "Tile color must be #RRGGBB."));
        ValidateAction(shortcut.Action, $"{path}.action", issues);
    }

    private static void ValidateAction(ShortcutActionDefinition? action, string path, List<ShortcutValidationIssue> issues)
    {
        if (action is null)
        {
            issues.Add(new ShortcutValidationIssue(path, "Shortcut action is required."));
            return;
        }

        if (!ShortcutActionTypes.IsKnown(action.Type))
        {
            issues.Add(new ShortcutValidationIssue($"{path}.type", "Unknown shortcut action type."));
            return;
        }

        switch (action.Type)
        {
            case ShortcutActionTypes.LaunchApplication:
                ValidateLaunchExecutablePath(action.ExecutablePath, $"{path}.executablePath", issues);
                ValidateArguments(action.Arguments, $"{path}.arguments", issues);
                break;
            case ShortcutActionTypes.OpenUrl:
                ValidateUrl(action.Url, $"{path}.url", issues);
                break;
            case ShortcutActionTypes.Hotkey:
                ValidateHotkey(action.Keys, $"{path}.keys", issues);
                break;
            case ShortcutActionTypes.Script:
                ValidateAbsoluteLocalPath(action.ScriptPath, $"{path}.scriptPath", issues);
                ValidateArguments(action.Arguments, $"{path}.arguments", issues);
                break;
            case ShortcutActionTypes.PowerShell:
                ValidatePowerShellPath(action.ScriptPath, $"{path}.scriptPath", issues);
                ValidateArguments(action.Arguments, $"{path}.arguments", issues);
                break;
            case ShortcutActionTypes.PredefinedAdapter:
                if (string.IsNullOrWhiteSpace(action.AdapterKey) || !AdapterKeyPattern.IsMatch(action.AdapterKey))
                {
                    issues.Add(new ShortcutValidationIssue($"{path}.adapterKey", "Adapter key must be a stable predefined local token."));
                }

                break;
        }
    }

    private static void ValidateAbsoluteLocalPath(string? value, string path, List<ShortcutValidationIssue> issues)
    {
        var normalizedValue = NormalizeQuotedPath(value);
        if (string.IsNullOrWhiteSpace(normalizedValue) || !Path.IsPathFullyQualified(normalizedValue))
        {
            issues.Add(new ShortcutValidationIssue(path, "A fully-qualified local path is required."));
            return;
        }

        if (Uri.TryCreate(normalizedValue, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            issues.Add(new ShortcutValidationIssue(path, "Network URLs are not valid local paths."));
            return;
        }

        if (IsUncPath(normalizedValue))
        {
            issues.Add(new ShortcutValidationIssue(path, "Network share paths are not valid local paths."));
        }
    }

    private static void ValidateLaunchExecutablePath(string? value, string path, List<ShortcutValidationIssue> issues)
    {
        var normalizedValue = NormalizeQuotedPath(value);
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            issues.Add(new ShortcutValidationIssue(path, "A fully-qualified local path or bare executable name is required."));
            return;
        }

        if (Uri.TryCreate(normalizedValue, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            issues.Add(new ShortcutValidationIssue(path, "Network URLs are not valid local paths."));
            return;
        }

        if (IsUncPath(normalizedValue))
        {
            issues.Add(new ShortcutValidationIssue(path, "Network share paths are not valid local paths."));
            return;
        }

        if (Path.IsPathFullyQualified(normalizedValue) || IsBareExecutableName(normalizedValue))
        {
            return;
        }

        issues.Add(new ShortcutValidationIssue(path, "A fully-qualified local path or bare executable name is required."));
    }

    private static bool IsBareExecutableName(string value)
    {
        return !value.Any(character => character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar)
            && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUncPath(string value)
    {
        return value.StartsWith(@"\\", StringComparison.Ordinal)
            || value.StartsWith("//", StringComparison.Ordinal);
    }

    private static string? NormalizeQuotedPath(string? value)
    {
        var trimmed = value?.Trim();
        if (trimmed is { Length: >= 2 } && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    private static void ValidatePowerShellPath(string? value, string path, List<ShortcutValidationIssue> issues)
    {
        ValidateAbsoluteLocalPath(value, path, issues);
        if (!string.IsNullOrWhiteSpace(value) && !value.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new ShortcutValidationIssue(path, "PowerShell action must reference a fixed .ps1 file."));
        }
    }

    private static void ValidateUrl(string? value, string path, List<ShortcutValidationIssue> issues)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            issues.Add(new ShortcutValidationIssue(path, "Only absolute http or https URLs are allowed."));
        }
    }

    private static void ValidateHotkey(IReadOnlyList<string>? keys, string path, List<ShortcutValidationIssue> issues)
    {
        if (keys is null)
        {
            issues.Add(new ShortcutValidationIssue(path, "Hotkey keys list is required."));
            return;
        }

        if (keys.Count is < 1 or > 5)
        {
            issues.Add(new ShortcutValidationIssue(path, "Hotkey must contain 1-5 keys."));
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < keys.Count; index++)
        {
            var key = keys[index];
            if (string.IsNullOrWhiteSpace(key) || !AllowedKeys.Contains(key))
            {
                issues.Add(new ShortcutValidationIssue($"{path}[{index}]", "Hotkey contains an unsupported key."));
            }

            if (!string.IsNullOrWhiteSpace(key) && !seen.Add(key))
            {
                issues.Add(new ShortcutValidationIssue($"{path}[{index}]", "Hotkey contains a duplicate key."));
            }
        }
    }

    private static void ValidateArguments(IReadOnlyList<string>? arguments, string path, List<ShortcutValidationIssue> issues)
    {
        if (arguments is null)
        {
            issues.Add(new ShortcutValidationIssue(path, "Arguments list is required."));
            return;
        }

        if (arguments.Count > 16)
        {
            issues.Add(new ShortcutValidationIssue(path, "At most 16 fixed local arguments are allowed."));
        }

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument is null || argument.Length > 512 || argument.Any(char.IsControl))
            {
                issues.Add(new ShortcutValidationIssue($"{path}[{index}]", "Argument contains malformed content."));
            }
        }
    }
}
