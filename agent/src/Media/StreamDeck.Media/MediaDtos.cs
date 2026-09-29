namespace StreamDeck.Media;

public sealed record MediaSnapshot(
    string? SourceAppUserModelId,
    string? Title,
    string? Artist,
    string? AlbumTitle,
    MediaPlaybackState PlaybackState,
    MediaTimeline Timeline,
    IMediaThumbnailReference? Thumbnail);

public sealed record MediaTimeline(
    TimeSpan StartTime,
    TimeSpan EndTime,
    TimeSpan Position,
    TimeSpan MinSeekTime,
    TimeSpan MaxSeekTime);

public enum MediaPlaybackState
{
    Unknown = 0,
    Closed,
    Opened,
    Changing,
    Stopped,
    Playing,
    Paused
}

public interface IMediaThumbnailReference
{
    ValueTask<Stream?> OpenReadAsync(CancellationToken cancellationToken = default);
}
