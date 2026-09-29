namespace StreamDeck.Shortcuts;

public sealed class ShortcutExecutor
{
    private readonly ShortcutCatalog catalog;
    private readonly IApplicationLauncher applicationLauncher;
    private readonly IUrlOpener urlOpener;
    private readonly IHotkeySender hotkeySender;
    private readonly IScriptRunner scriptRunner;
    private readonly IPredefinedShortcutAdapter predefinedAdapter;

    public ShortcutExecutor(
        ShortcutCatalog catalog,
        IApplicationLauncher applicationLauncher,
        IUrlOpener urlOpener,
        IHotkeySender hotkeySender,
        IScriptRunner scriptRunner,
        IPredefinedShortcutAdapter predefinedAdapter)
    {
        this.catalog = catalog;
        this.applicationLauncher = applicationLauncher;
        this.urlOpener = urlOpener;
        this.hotkeySender = hotkeySender;
        this.scriptRunner = scriptRunner;
        this.predefinedAdapter = predefinedAdapter;
    }

    public async Task<ShortcutExecutionResult> ExecuteAsync(ShortcutExecuteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!catalog.TryGetAction(request.Id, out var action))
        {
            return ShortcutExecutionResult.UnknownShortcut;
        }

        switch (action.Type)
        {
            case ShortcutActionTypes.LaunchApplication:
                await applicationLauncher.LaunchAsync(action.ExecutablePath!, action.Arguments, cancellationToken);
                break;
            case ShortcutActionTypes.OpenUrl:
                await urlOpener.OpenAsync(new Uri(action.Url!, UriKind.Absolute), cancellationToken);
                break;
            case ShortcutActionTypes.Hotkey:
                await hotkeySender.SendAsync(action.Keys, cancellationToken);
                break;
            case ShortcutActionTypes.Script:
                await scriptRunner.RunScriptAsync(action.ScriptPath!, action.Arguments, cancellationToken);
                break;
            case ShortcutActionTypes.PowerShell:
                await scriptRunner.RunPowerShellAsync(action.ScriptPath!, action.Arguments, cancellationToken);
                break;
            case ShortcutActionTypes.PredefinedAdapter:
                await predefinedAdapter.ExecuteAsync(action.AdapterKey!, cancellationToken);
                break;
            default:
                return ShortcutExecutionResult.UnknownShortcut;
        }

        return ShortcutExecutionResult.Executed;
    }
}
