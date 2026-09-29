namespace StreamDeck.WindowsAgent;

public sealed record AgentOptions(bool MockMode)
{
    public static AgentOptions FromArgs(IReadOnlyList<string> args)
    {
        return new AgentOptions(args.Any(arg => string.Equals(arg, "--mock", StringComparison.OrdinalIgnoreCase)));
    }
}
