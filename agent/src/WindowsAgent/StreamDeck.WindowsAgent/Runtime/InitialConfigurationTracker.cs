using StreamDeck.Protocol;

namespace StreamDeck.WindowsAgent.Runtime;

internal sealed class InitialConfigurationTracker(AgentState state)
{
    private long generation;
    private bool shortcutsSent;
    private bool finished;

    public async ValueTask SendAsync(string message, Func<ValueTask> send, bool isActiveSession = true)
    {
        var type = ProtocolSerializer.Parse(message).Envelope?.Type;
        var tracked = isActiveSession && !finished && type is "shortcuts.update" or "device.settings";
        if (tracked && generation == 0) generation = state.BeginConfigurationSync();
        try
        {
            await send().ConfigureAwait(false);
            if (!tracked) return;
            if (type == "shortcuts.update") shortcutsSent = true;
            if (type == "device.settings" && shortcutsSent)
            {
                finished = true;
                state.CompleteConfigurationSync(generation, ConfigurationSyncStatus.SentUnconfirmed);
            }
        }
        catch
        {
            if (tracked) state.CompleteConfigurationSync(generation, ConfigurationSyncStatus.Failed);
            throw;
        }
    }
}
