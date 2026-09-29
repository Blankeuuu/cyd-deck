using StreamDeck.Audio;
using StreamDeck.Media;
using StreamDeck.Protocol;
using StreamDeck.Shortcuts;

namespace StreamDeck.Host;

public sealed class MediaSessionMonitorController(MediaSessionMonitor monitor) : IHostMediaController
{
    public Task<bool> ExecuteAsync(MediaCommand command, CancellationToken cancellationToken = default)
    {
        return command switch
        {
            MediaCommand.PlayPause => monitor.TogglePlayPauseAsync(cancellationToken),
            MediaCommand.Play => monitor.PlayAsync(cancellationToken),
            MediaCommand.Pause => monitor.PauseAsync(cancellationToken),
            MediaCommand.Previous => monitor.PreviousAsync(cancellationToken),
            MediaCommand.Next => monitor.NextAsync(cancellationToken),
            MediaCommand.Stop => Task.FromResult(false),
            _ => Task.FromResult(false)
        };
    }
}

public sealed class MasterVolumeController(WindowsMasterVolumeService volumeService) : IHostAudioController
{
    public Task<bool> SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(volumeService.SetVolume(volumePercent));
    }

    public Task<bool> ToggleMuteAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(volumeService.ToggleMute());
    }
}

public sealed class ShortcutExecutorController(ShortcutExecutor shortcutExecutor) : IHostShortcutExecutor
{
    public async Task<bool> ExecuteAsync(string shortcutId, CancellationToken cancellationToken = default)
    {
        var result = await shortcutExecutor.ExecuteAsync(new ShortcutExecuteRequest(shortcutId), cancellationToken).ConfigureAwait(false);
        return result.Status == ShortcutExecutionStatus.Executed;
    }
}
