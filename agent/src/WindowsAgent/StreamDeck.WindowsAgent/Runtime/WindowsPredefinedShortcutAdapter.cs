using StreamDeck.Shortcuts;

namespace StreamDeck.WindowsAgent.Runtime;

internal sealed class WindowsPredefinedShortcutAdapter(Func<bool> toggleMicrophoneMute) : IPredefinedShortcutAdapter
{
    public Task ExecuteAsync(string adapterKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(adapterKey, "audio:toggle-mic", StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Predefined shortcut adapter '{adapterKey}' is not supported.");
        }

        if (!toggleMicrophoneMute())
        {
            throw new InvalidOperationException("The default microphone mute operation failed.");
        }

        return Task.CompletedTask;
    }
}
