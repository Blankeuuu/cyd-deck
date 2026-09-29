namespace StreamDeck.Media;

public interface IWindowsMediaSessionSource : IAsyncDisposable
{
    event EventHandler? SessionsChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<IWindowsMediaSession> GetSessions();

    IWindowsMediaSession? GetCurrentSession();
}

public interface IWindowsMediaSession : IDisposable
{
    event EventHandler? MediaPropertiesChanged;

    event EventHandler? PlaybackInfoChanged;

    event EventHandler? TimelinePropertiesChanged;

    string? SourceAppUserModelId { get; }

    Task<NativeMediaProperties> GetMediaPropertiesAsync(CancellationToken cancellationToken = default);

    NativePlaybackInfo GetPlaybackInfo();

    NativeTimelineProperties GetTimelineProperties();

    Task<bool> TrySkipPreviousAsync(CancellationToken cancellationToken = default);

    Task<bool> TryPlayAsync(CancellationToken cancellationToken = default);

    Task<bool> TryPauseAsync(CancellationToken cancellationToken = default);

    Task<bool> TryTogglePlayPauseAsync(CancellationToken cancellationToken = default);

    Task<bool> TrySkipNextAsync(CancellationToken cancellationToken = default);
}

public interface IMediaSessionSelector
{
    IWindowsMediaSession? Select(IWindowsMediaSession? currentSession, IReadOnlyList<IWindowsMediaSession> sessions);
}

public interface IMediaSessionMapper
{
    Task<MediaSnapshot> MapAsync(IWindowsMediaSession session, CancellationToken cancellationToken = default);
}
