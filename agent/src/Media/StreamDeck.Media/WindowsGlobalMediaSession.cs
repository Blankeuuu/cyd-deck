using Windows.Media.Control;
using Windows.Storage.Streams;

namespace StreamDeck.Media;

internal sealed class WindowsGlobalMediaSession : IWindowsMediaSession
{
    private readonly GlobalSystemMediaTransportControlsSession _session;

    public WindowsGlobalMediaSession(GlobalSystemMediaTransportControlsSession session)
    {
        _session = session;
        _session.MediaPropertiesChanged += HandleMediaPropertiesChanged;
        _session.PlaybackInfoChanged += HandlePlaybackInfoChanged;
        _session.TimelinePropertiesChanged += HandleTimelinePropertiesChanged;
    }

    public event EventHandler? MediaPropertiesChanged;

    public event EventHandler? PlaybackInfoChanged;

    public event EventHandler? TimelinePropertiesChanged;

    public string? SourceAppUserModelId => _session.SourceAppUserModelId;

    public async Task<NativeMediaProperties> GetMediaPropertiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var properties = await _session.TryGetMediaPropertiesAsync();

        cancellationToken.ThrowIfCancellationRequested();

        return new NativeMediaProperties(
            properties.Title,
            properties.Artist,
            properties.AlbumTitle,
            properties.Thumbnail is null ? null : new WindowsMediaThumbnailReference(properties.Thumbnail));
    }

    public NativePlaybackInfo GetPlaybackInfo()
    {
        var playbackInfo = _session.GetPlaybackInfo();
        var playbackState = playbackInfo is null
            ? MediaPlaybackState.Unknown
            : MapPlaybackState(playbackInfo.PlaybackStatus);

        return new NativePlaybackInfo(playbackState, playbackInfo?.PlaybackRate);
    }

    public NativeTimelineProperties GetTimelineProperties()
    {
        var timeline = _session.GetTimelineProperties();

        return new NativeTimelineProperties(
            timeline.StartTime,
            timeline.EndTime,
            timeline.Position,
            timeline.MinSeekTime,
            timeline.MaxSeekTime,
            timeline.LastUpdatedTime);
    }

    public async Task<bool> TrySkipPreviousAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _session.TrySkipPreviousAsync();
    }

    public async Task<bool> TryPlayAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _session.TryPlayAsync();
    }

    public async Task<bool> TryPauseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _session.TryPauseAsync();
    }

    public async Task<bool> TryTogglePlayPauseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _session.TryTogglePlayPauseAsync();
    }

    public async Task<bool> TrySkipNextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _session.TrySkipNextAsync();
    }

    public void Dispose()
    {
        _session.MediaPropertiesChanged -= HandleMediaPropertiesChanged;
        _session.PlaybackInfoChanged -= HandlePlaybackInfoChanged;
        _session.TimelinePropertiesChanged -= HandleTimelinePropertiesChanged;
    }

    private static MediaPlaybackState MapPlaybackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus status)
    {
        return status switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => MediaPlaybackState.Closed,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => MediaPlaybackState.Opened,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => MediaPlaybackState.Changing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackState.Stopped,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackState.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackState.Paused,
            _ => MediaPlaybackState.Unknown
        };
    }

    private void HandleMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        MediaPropertiesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandlePlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        PlaybackInfoChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        TimelinePropertiesChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class WindowsMediaThumbnailReference(IRandomAccessStreamReference thumbnail) : IMediaThumbnailReference
    {
        public async ValueTask<Stream?> OpenReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var randomAccessStream = await thumbnail.OpenReadAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return randomAccessStream.AsStreamForRead();
        }
    }
}
