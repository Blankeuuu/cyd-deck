namespace StreamDeck.Shortcuts;

public sealed record ShortcutValidationIssue(string Path, string Message);

public sealed class ShortcutConfigurationException : Exception
{
    public ShortcutConfigurationException(IReadOnlyList<ShortcutValidationIssue> issues)
        : base(CreateMessage(issues))
    {
        Issues = issues;
    }

    public IReadOnlyList<ShortcutValidationIssue> Issues { get; }

    private static string CreateMessage(IReadOnlyList<ShortcutValidationIssue> issues)
    {
        return issues.Count == 0
            ? "Shortcut configuration is invalid."
            : $"Shortcut configuration is invalid: {string.Join("; ", issues.Select(issue => $"{issue.Path}: {issue.Message}"))}";
    }
}
