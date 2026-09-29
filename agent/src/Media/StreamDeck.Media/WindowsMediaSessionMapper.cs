namespace StreamDeck.Media;

public sealed class WindowsMediaSessionMapper : IMediaSessionMapper
{
    public async Task<MediaSnapshot> MapAsync(IWindowsMediaSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var mediaProperties = await session.GetMediaPropertiesAsync(cancellationToken).ConfigureAwait(false);
        var playbackInfo = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();

        return new MediaSnapshot(
            session.SourceAppUserModelId,
            EmptyToNull(mediaProperties.Title),
            EmptyToNull(mediaProperties.Artist),
            EmptyToNull(mediaProperties.AlbumTitle),
            playbackInfo.PlaybackState,
            new MediaTimeline(
                timeline.StartTime,
                timeline.EndTime,
                timeline.Position,
                timeline.MinSeekTime,
                timeline.MaxSeekTime),
            mediaProperties.Thumbnail);
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
