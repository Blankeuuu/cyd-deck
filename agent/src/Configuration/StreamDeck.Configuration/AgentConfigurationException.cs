namespace StreamDeck.Configuration;

public sealed class AgentConfigurationException : Exception
{
    public AgentConfigurationException(IReadOnlyList<ConfigurationValidationIssue> issues)
        : base("Agent configuration is invalid.")
    {
        Issues = issues;
    }

    public IReadOnlyList<ConfigurationValidationIssue> Issues { get; }
}
