namespace StreamDeck.Media;

public sealed class WindowsMediaSessionMapper(TimeProvider? timeProvider = null) : IMediaSessionMapper
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<MediaSnapshot> MapAsync(IWindowsMediaSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var mediaProperties = await session.GetMediaPropertiesAsync(cancellationToken).ConfigureAwait(false);
        var playbackInfo = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();

        var position = CalculateLivePosition(timeline, playbackInfo, _timeProvider.GetUtcNow());

        return new MediaSnapshot(
            session.SourceAppUserModelId,
            EmptyToNull(mediaProperties.Title),
            EmptyToNull(mediaProperties.Artist),
            EmptyToNull(mediaProperties.AlbumTitle),
            playbackInfo.PlaybackState,
            new MediaTimeline(
                timeline.StartTime,
                timeline.EndTime,
                position,
                timeline.MinSeekTime,
                timeline.MaxSeekTime),
            mediaProperties.Thumbnail);
    }

    public static TimeSpan CalculateLivePosition(NativeTimelineProperties timeline, NativePlaybackInfo playbackInfo, DateTimeOffset nowUtc)
    {
        var position = timeline.Position;
        if (playbackInfo.PlaybackState == MediaPlaybackState.Playing &&
            timeline.LastUpdatedTime is { } lastUpdated &&
            lastUpdated != default)
        {
            var elapsed = nowUtc - lastUpdated;
            if (elapsed > TimeSpan.Zero)
            {
                var rate = playbackInfo.PlaybackRate ?? 1.0;
                if (!double.IsNaN(rate) && !double.IsInfinity(rate) && rate > 0.0)
                {
                    var elapsedMs = elapsed.TotalMilliseconds * rate;
                    if (!double.IsNaN(elapsedMs) && !double.IsInfinity(elapsedMs) && elapsedMs > 0.0 && elapsedMs < TimeSpan.FromDays(7).TotalMilliseconds)
                    {
                        position += TimeSpan.FromMilliseconds(elapsedMs);
                    }
                }
            }
        }

        if (timeline.EndTime > TimeSpan.Zero && position > timeline.EndTime)
        {
            position = timeline.EndTime;
        }

        if (position < timeline.StartTime)
        {
            position = timeline.StartTime;
        }

        return position;
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
