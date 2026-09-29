namespace StreamDeck.Shortcuts;

public interface IApplicationLauncher
{
    Task LaunchAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public interface IUrlOpener
{
    Task OpenAsync(Uri url, CancellationToken cancellationToken);
}

public interface IHotkeySender
{
    Task SendAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken);
}

public interface IScriptRunner
{
    Task RunScriptAsync(string scriptPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken);

    Task RunPowerShellAsync(string scriptPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public interface IPredefinedShortcutAdapter
{
    Task ExecuteAsync(string adapterKey, CancellationToken cancellationToken);
}

public sealed record ShortcutExecutionResult(ShortcutExecutionStatus Status)
{
    public static ShortcutExecutionResult Executed { get; } = new(ShortcutExecutionStatus.Executed);
    public static ShortcutExecutionResult UnknownShortcut { get; } = new(ShortcutExecutionStatus.UnknownShortcut);
}

public enum ShortcutExecutionStatus
{
    Executed,
    UnknownShortcut
}
