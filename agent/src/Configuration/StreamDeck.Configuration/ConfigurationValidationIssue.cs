namespace StreamDeck.Configuration;

public sealed record ConfigurationValidationIssue(string Path, string Message);
