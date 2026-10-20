namespace StreamDeck.WindowsAgent.Runtime;

/// <summary>Tracks transport completion, never infers device application from telemetry or a settings-only ACK.</summary>
public sealed class ConfigurationSynchronizer(AgentState state)
{
    public async Task<bool> SendAsync(Func<CancellationToken, Task<bool>> send,
        CancellationToken cancellationToken = default, TimeSpan? timeout = null)
    {
        var generation = state.BeginConfigurationSync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(15));
        try
        {
            var sent = await send(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
            state.CompleteConfigurationSync(generation, sent ? ConfigurationSyncStatus.SentUnconfirmed : ConfigurationSyncStatus.Disconnected);
            var current = state.Snapshot().Synchronization;
            return sent && current?.Generation == generation && current.Status == ConfigurationSyncStatus.SentUnconfirmed;
        }
        catch (OperationCanceledException)
        {
            state.CompleteConfigurationSync(generation, cancellationToken.IsCancellationRequested
                ? ConfigurationSyncStatus.Superseded : ConfigurationSyncStatus.TimedOut);
            if (cancellationToken.IsCancellationRequested) throw;
            return false;
        }
        catch (Exception)
        {
            // Transport errors can contain peer-supplied text; keep diagnostics out of UI state.
            state.CompleteConfigurationSync(generation, ConfigurationSyncStatus.Failed, "transport_send_failed");
            return false;
        }
    }
}
