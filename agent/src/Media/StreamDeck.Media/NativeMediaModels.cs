namespace StreamDeck.Media;

public sealed record NativeMediaProperties(
    string? Title,
    string? Artist,
    string? AlbumTitle,
    IMediaThumbnailReference? Thumbnail);

public sealed record NativePlaybackInfo(MediaPlaybackState PlaybackState);

public sealed record NativeTimelineProperties(
    TimeSpan StartTime,
    TimeSpan EndTime,
    TimeSpan Position,
    TimeSpan MinSeekTime,
    TimeSpan MaxSeekTime);
