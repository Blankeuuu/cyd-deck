using StreamDeck.Protocol;

namespace StreamDeck.Host;

public interface IHostMediaController
{
    Task<bool> ExecuteAsync(MediaCommand command, CancellationToken cancellationToken = default);
}

public interface IHostAudioController
{
    Task<bool> SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default);

    Task<bool> ToggleMuteAsync(CancellationToken cancellationToken = default);
}

public interface IHostShortcutExecutor
{
    Task<bool> ExecuteAsync(string shortcutId, CancellationToken cancellationToken = default);
}

public interface IHostSystemCommandExecutor
{
    Task<bool> ExecuteAsync(SystemCommand command, CancellationToken cancellationToken = default);
}

public sealed class RejectingSystemCommandExecutor : IHostSystemCommandExecutor
{
    public Task<bool> ExecuteAsync(SystemCommand command, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }
}
