using StreamDeck.Media;
using Xunit;

namespace StreamDeck.Media.Tests;

public sealed class MediaSessionTests
{
    [Fact]
    public void SelectorPrefersCurrentSession()
    {
        var current = new FakeSession("current", MediaPlaybackState.Paused);
        var playing = new FakeSession("playing", MediaPlaybackState.Playing);
        var selector = new DefaultMediaSessionSelector();

        var selected = selector.Select(current, [playing]);

        Assert.Same(current, selected);
    }

    [Fact]
    public void SelectorFallsBackToPlayingSession()
    {
        var paused = new FakeSession("paused", MediaPlaybackState.Paused);
        var playing = new FakeSession("playing", MediaPlaybackState.Playing);
        var selector = new DefaultMediaSessionSelector();

        var selected = selector.Select(null, [paused, playing]);

        Assert.Same(playing, selected);
    }

    [Fact]
    public async Task MapperCreatesCompactSnapshot()
    {
        var session = new FakeSession("SpotifyAB.SpotifyMusic", MediaPlaybackState.Playing)
        {
            MediaProperties = new NativeMediaProperties("  ", "Artist", "Album", null),
            TimelineProperties = new NativeTimelineProperties(
                TimeSpan.Zero,
                TimeSpan.FromMinutes(3),
                TimeSpan.FromSeconds(42),
                TimeSpan.Zero,
                TimeSpan.FromMinutes(3))
        };

        var snapshot = await new WindowsMediaSessionMapper().MapAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal("SpotifyAB.SpotifyMusic", snapshot.SourceAppUserModelId);
        Assert.Null(snapshot.Title);
        Assert.Equal("Artist", snapshot.Artist);
        Assert.Equal("Album", snapshot.AlbumTitle);
        Assert.Equal(MediaPlaybackState.Playing, snapshot.PlaybackState);
        Assert.Equal(TimeSpan.FromSeconds(42), snapshot.Timeline.Position);
    }

    [Fact]
    public async Task MonitorRefreshesOnlyFromStartAndEvents()
    {
        var session = new FakeSession("player", MediaPlaybackState.Playing);
        var source = new FakeSessionSource(session);
        var mapper = new CountingMapper();
        var monitor = new MediaSessionMonitor(source, mapper: mapper);
        var snapshots = new List<MediaSnapshot?>();
        monitor.SnapshotChanged += (_, snapshot) => snapshots.Add(snapshot);

        await monitor.StartAsync(TestContext.Current.CancellationToken);
        session.RaisePlaybackInfoChanged();

        Assert.Equal(2, mapper.MapCount);
        Assert.Single(snapshots);
        Assert.Equal("player", monitor.CurrentSnapshot?.SourceAppUserModelId);
    }

    [Fact]
    public async Task MonitorReportsRefreshFailureTypeAndKeepsLastGoodSnapshot()
    {
        var session = new FakeSession("player", MediaPlaybackState.Playing);
        var mapper = new CountingMapper();
        var monitor = new MediaSessionMonitor(new FakeSessionSource(session), mapper: mapper);
        var failure = new TaskCompletionSource<MediaSessionRefreshFailedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.RefreshFailed += (_, args) => failure.TrySetResult(args);

        await monitor.StartAsync(TestContext.Current.CancellationToken);
        mapper.ThrowOnMap = true;
        session.RaisePlaybackInfoChanged();

        var failureArgs = await failure.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal("MapSession", failureArgs.Operation);
        Assert.Equal(nameof(InvalidOperationException), failureArgs.ExceptionType);
        Assert.Equal("player", monitor.CurrentSnapshot?.SourceAppUserModelId);
    }

    [Theory]
    [InlineData(nameof(ArgumentNullException))]
    [InlineData(nameof(ArgumentException))]
    public async Task MonitorTreatsEnumerationExceptionsAsTransientAndDoesNotPublishEmptySnapshot(string exceptionType)
    {
        var session = new FakeSession("player", MediaPlaybackState.Playing);
        var source = new FakeSessionSource(session);
        var monitor = new MediaSessionMonitor(source);
        var snapshots = new List<MediaSnapshot?>();
        var failure = new TaskCompletionSource<MediaSessionRefreshFailedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.SnapshotChanged += (_, snapshot) => snapshots.Add(snapshot);
        monitor.RefreshFailed += (_, args) => failure.TrySetResult(args);

        await monitor.StartAsync(TestContext.Current.CancellationToken);
        source.ThrowOnGetSessions = exceptionType;
        source.RaiseSessionsChanged();

        var failureArgs = await failure.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal("GetSessions", failureArgs.Operation);
        Assert.Equal(exceptionType, failureArgs.ExceptionType);
        Assert.Equal("sessions", failureArgs.ParamName);
        Assert.Single(snapshots);
        Assert.NotNull(snapshots[0]);
        Assert.Equal("player", monitor.CurrentSnapshot?.SourceAppUserModelId);
    }

    [Fact]
    public async Task MonitorReportsCurrentSessionNativeBoundaryWithoutPublishingEmptySnapshot()
    {
        var session = new FakeSession("player", MediaPlaybackState.Playing);
        var source = new FakeSessionSource(session);
        var monitor = new MediaSessionMonitor(source);
        var snapshots = new List<MediaSnapshot?>();
        var failure = new TaskCompletionSource<MediaSessionRefreshFailedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.SnapshotChanged += (_, snapshot) => snapshots.Add(snapshot);
        monitor.RefreshFailed += (_, args) => failure.TrySetResult(args);

        await monitor.StartAsync(TestContext.Current.CancellationToken);
        source.ThrowOnGetCurrentSession = true;
        source.RaiseSessionsChanged();

        var failureArgs = await failure.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal("GetCurrentSession", failureArgs.Operation);
        Assert.Equal(nameof(ArgumentNullException), failureArgs.ExceptionType);
        Assert.Equal("currentSession", failureArgs.ParamName);
        Assert.Single(snapshots);
        Assert.Equal("player", monitor.CurrentSnapshot?.SourceAppUserModelId);
    }

    [Fact]
    public void RefreshFailureArgsCarryArgumentNameWithoutArbitraryMessage()
    {
        var exception = new ArgumentNullException("bad param/value\r\nwith overflow", "Sensitive path C:/Users/example/secret.txt");

        var args = MediaSessionRefreshFailedEventArgs.FromException("GetSessions", exception);

        Assert.Equal("GetSessions", args.Operation);
        Assert.Equal(nameof(ArgumentNullException), args.ExceptionType);
        Assert.Equal("bad_param_value__with_overflow", args.ParamName);
        Assert.DoesNotContain(args.GetType().GetProperties(), property => property.Name == "Message");
    }

    [Fact]
    public async Task MonitorKeepsConcurrentFailureOperationLabelsIsolated()
    {
        var session = new FakeSession("player", MediaPlaybackState.Playing);
        var source = new FakeSessionSource(session);
        var mapper = new CountingMapper();
        var monitor = new MediaSessionMonitor(source, mapper: mapper);
        var failures = new List<MediaSessionRefreshFailedEventArgs>();
        var firstFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.RefreshFailed += (_, args) =>
        {
            lock (failures)
            {
                failures.Add(args);
                if (failures.Count == 1)
                {
                    firstFailure.TrySetResult();
                }

                if (failures.Count == 2)
                {
                    secondFailure.TrySetResult();
                }
            }
        };

        await monitor.StartAsync(TestContext.Current.CancellationToken);

        mapper.ThrowOnMap = true;
        mapper.HoldMapFailure = true;
        session.RaisePlaybackInfoChanged();
        await mapper.MapFailureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        source.ThrowOnGetSessions = nameof(ArgumentException);
        source.RaiseSessionsChanged();
        await firstFailure.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        mapper.ReleaseMapFailure.SetResult();
        await secondFailure.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Contains(failures, failure => failure.Operation == "GetSessions" && failure.ExceptionType == nameof(ArgumentException));
        Assert.Contains(failures, failure => failure.Operation == "MapSession" && failure.ExceptionType == nameof(InvalidOperationException));
        Assert.Equal("player", monitor.CurrentSnapshot?.SourceAppUserModelId);
    }

    [Fact]
    public async Task MonitorClearsSnapshotOnceWhenNoSessionsRemain()
    {
        var session = new FakeSession("player", MediaPlaybackState.Playing);
        var source = new FakeSessionSource(session);
        var monitor = new MediaSessionMonitor(source);
        var snapshots = new List<MediaSnapshot?>();
        monitor.SnapshotChanged += (_, snapshot) => snapshots.Add(snapshot);

        await monitor.StartAsync(TestContext.Current.CancellationToken);
        source.ReplaceSessions();
        source.ReplaceSessions();

        Assert.Equal(2, snapshots.Count);
        Assert.NotNull(snapshots[0]);
        Assert.Null(snapshots[1]);
        Assert.Null(monitor.CurrentSnapshot);
    }

    [Fact]
    public async Task CommandsReturnFalseWhenSessionIsMissingOrFails()
    {
        var source = new FakeSessionSource();
        var monitor = new MediaSessionMonitor(source);

        await monitor.StartAsync(TestContext.Current.CancellationToken);

        Assert.False(await monitor.NextAsync(TestContext.Current.CancellationToken));

        var session = new FakeSession("player", MediaPlaybackState.Playing)
        {
            ThrowOnNext = true
        };

        source.ReplaceSessions(session);

        Assert.False(await monitor.NextAsync(TestContext.Current.CancellationToken));
    }

    private sealed class CountingMapper : IMediaSessionMapper
    {
        public int MapCount { get; private set; }
        public bool ThrowOnMap { get; set; }

        public bool HoldMapFailure { get; set; }

        public TaskCompletionSource MapFailureStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseMapFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<MediaSnapshot> MapAsync(IWindowsMediaSession session, CancellationToken cancellationToken = default)
        {
            MapCount++;
            if (ThrowOnMap)
            {
                if (HoldMapFailure)
                {
                    MapFailureStarted.TrySetResult();
                    await ReleaseMapFailure.Task.WaitAsync(cancellationToken);
                }

                throw new InvalidOperationException("Media metadata unavailable.");
            }

            return new MediaSnapshot(
                session.SourceAppUserModelId,
                "Title",
                "Artist",
                "Album",
                session.GetPlaybackInfo().PlaybackState,
                new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(1), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMinutes(1)),
                null);
        }
    }

    private sealed class FakeSessionSource : IWindowsMediaSessionSource
    {
        private IReadOnlyList<IWindowsMediaSession> _sessions;
        private IWindowsMediaSession? _currentSession;

        public FakeSessionSource(params IWindowsMediaSession[] sessions)
        {
            _sessions = sessions;
            _currentSession = sessions.FirstOrDefault();
        }

        public event EventHandler? SessionsChanged;

        public string? ThrowOnGetSessions { get; set; }

        public bool ThrowOnGetCurrentSession { get; set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public IReadOnlyList<IWindowsMediaSession> GetSessions()
        {
            if (ThrowOnGetSessions == nameof(ArgumentNullException))
            {
                throw new ArgumentNullException("sessions");
            }

            if (ThrowOnGetSessions == nameof(ArgumentException))
            {
                throw new ArgumentException("Native media session enumeration failed.", "sessions");
            }

            return _sessions;
        }

        public IWindowsMediaSession? GetCurrentSession()
        {
            if (ThrowOnGetCurrentSession)
            {
                throw new ArgumentNullException("currentSession");
            }

            return _currentSession;
        }

        public void ReplaceSessions(params IWindowsMediaSession[] sessions)
        {
            _sessions = sessions;
            _currentSession = sessions.FirstOrDefault();
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RaiseSessionsChanged()
        {
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSession(string? sourceAppUserModelId, MediaPlaybackState playbackState) : IWindowsMediaSession
    {
        public event EventHandler? MediaPropertiesChanged;

        public event EventHandler? PlaybackInfoChanged;

        public event EventHandler? TimelinePropertiesChanged;

        public string? SourceAppUserModelId { get; } = sourceAppUserModelId;

        public NativeMediaProperties MediaProperties { get; set; } = new("Title", "Artist", "Album", null);

        public NativeTimelineProperties TimelineProperties { get; set; } = new(
            TimeSpan.Zero,
            TimeSpan.FromMinutes(1),
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(1));

        public bool ThrowOnNext { get; set; }

        public Task<NativeMediaProperties> GetMediaPropertiesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(MediaProperties);
        }

        public NativePlaybackInfo GetPlaybackInfo()
        {
            return new NativePlaybackInfo(playbackState);
        }

        public NativeTimelineProperties GetTimelineProperties()
        {
            return TimelineProperties;
        }

        public Task<bool> TrySkipPreviousAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> TryPlayAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> TryPauseAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> TryTogglePlayPauseAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> TrySkipNextAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnNext)
            {
                throw new InvalidOperationException("Native command failed.");
            }

            return Task.FromResult(true);
        }

        public void Dispose()
        {
        }

        public void RaisePlaybackInfoChanged()
        {
            PlaybackInfoChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RaiseMediaPropertiesChanged()
        {
            MediaPropertiesChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RaiseTimelinePropertiesChanged()
        {
            TimelinePropertiesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
