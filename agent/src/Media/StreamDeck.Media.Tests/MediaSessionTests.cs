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

    [Fact]
    public async Task MapperCalculatesLivePositionUsingLastUpdatedTimeAndPlaybackRate()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new FakeSession("chrome.exe", MediaPlaybackState.Playing)
        {
            MediaProperties = new NativeMediaProperties("YouTube Video", "Channel", null, null),
            PlaybackInfo = new NativePlaybackInfo(MediaPlaybackState.Playing, PlaybackRate: 1.0),
            TimelineProperties = new NativeTimelineProperties(
                TimeSpan.Zero,
                TimeSpan.FromMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.FromMinutes(5),
                LastUpdatedTime: now.AddSeconds(-30))
        };

        var snapshot = await new WindowsMediaSessionMapper().MapAsync(session, TestContext.Current.CancellationToken);

        Assert.True(snapshot.Timeline.Position >= TimeSpan.FromSeconds(29), $"Expected >= 29s but was {snapshot.Timeline.Position}");
        Assert.True(snapshot.Timeline.Position <= TimeSpan.FromSeconds(35), $"Expected <= 35s but was {snapshot.Timeline.Position}");
    }

    [Fact]
    public async Task MapperPreservesPausedPositionWithoutAdvancing()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new FakeSession("Spotify.exe", MediaPlaybackState.Paused)
        {
            MediaProperties = new NativeMediaProperties("Song", "Artist", null, null),
            PlaybackInfo = new NativePlaybackInfo(MediaPlaybackState.Paused, PlaybackRate: 1.0),
            TimelineProperties = new NativeTimelineProperties(
                TimeSpan.Zero,
                TimeSpan.FromMinutes(3),
                TimeSpan.FromSeconds(45),
                TimeSpan.Zero,
                TimeSpan.FromMinutes(3),
                LastUpdatedTime: now.AddSeconds(-60))
        };

        var snapshot = await new WindowsMediaSessionMapper().MapAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromSeconds(45), snapshot.Timeline.Position);
    }

    [Fact]
    public async Task MapperSupportsBackwardSeekWithoutClamp()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new FakeSession("Spotify.exe", MediaPlaybackState.Playing)
        {
            MediaProperties = new NativeMediaProperties("Song", "Artist", null, null),
            PlaybackInfo = new NativePlaybackInfo(MediaPlaybackState.Playing, PlaybackRate: 1.0),
            TimelineProperties = new NativeTimelineProperties(
                TimeSpan.Zero,
                TimeSpan.FromMinutes(3),
                TimeSpan.FromSeconds(10),
                TimeSpan.Zero,
                TimeSpan.FromMinutes(3),
                LastUpdatedTime: now)
        };

        var snapshot = await new WindowsMediaSessionMapper().MapAsync(session, TestContext.Current.CancellationToken);

        Assert.True(Math.Abs((snapshot.Timeline.Position - TimeSpan.FromSeconds(10)).TotalMilliseconds) < 50);
        var directCalculated = WindowsMediaSessionMapper.CalculateLivePosition(session.TimelineProperties, session.PlaybackInfo, now);
        Assert.Equal(TimeSpan.FromSeconds(10), directCalculated);
    }

    [Fact]
    public void MapperValidatesPlaybackRateAndAbsurdTimestampWithoutOverflow()
    {
        var timeline = new NativeTimelineProperties(
            TimeSpan.Zero,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(30),
            TimeSpan.Zero,
            TimeSpan.FromMinutes(5),
            LastUpdatedTime: DateTimeOffset.UtcNow.AddDays(-100));

        var invalidRateInfo = new NativePlaybackInfo(MediaPlaybackState.Playing, PlaybackRate: double.NaN);
        var livePos1 = WindowsMediaSessionMapper.CalculateLivePosition(timeline, invalidRateInfo, DateTimeOffset.UtcNow);
        Assert.Equal(TimeSpan.FromSeconds(30), livePos1);

        var infRateInfo = new NativePlaybackInfo(MediaPlaybackState.Playing, PlaybackRate: double.PositiveInfinity);
        var livePos2 = WindowsMediaSessionMapper.CalculateLivePosition(timeline, infRateInfo, DateTimeOffset.UtcNow);
        Assert.Equal(TimeSpan.FromSeconds(30), livePos2);
    }

    [Fact]
    public async Task MonitorPreservesSequentialSnapshotOrderWhenEarlierRefreshFinishesLater()
    {
        var session = new FakeSession("Spotify.exe", MediaPlaybackState.Playing);
        var source = new FakeSessionSource(session);

        var tcsFirstMapStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsAllowFirstMapToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var callCount = 0;
        var mapper = new CallbackMediaSessionMapper(async (s, ct) =>
        {
            var callIndex = Interlocked.Increment(ref callCount);
            if (callIndex == 1)
            {
                tcsFirstMapStarted.TrySetResult(true);
                await tcsAllowFirstMapToFinish.Task.ConfigureAwait(false);
                return new MediaSnapshot(
                    s.SourceAppUserModelId,
                    "First Song",
                    "Artist",
                    null,
                    MediaPlaybackState.Playing,
                    new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.FromMinutes(3)),
                    null);
            }

            return new MediaSnapshot(
                s.SourceAppUserModelId,
                "Second Song",
                "Artist",
                null,
                MediaPlaybackState.Playing,
                new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(2), TimeSpan.Zero, TimeSpan.FromMinutes(3)),
                null);
        });

        await using var monitor = new MediaSessionMonitor(source, mapper: mapper);
        var emittedSnapshots = new List<MediaSnapshot?>();
        monitor.SnapshotChanged += (_, snapshot) =>
        {
            lock (emittedSnapshots)
            {
                emittedSnapshots.Add(snapshot);
            }
        };

        var firstStartTask = monitor.StartAsync(TestContext.Current.CancellationToken);
        await tcsFirstMapStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        session.RaisePlaybackInfoChanged();

        await Task.Yield();
        tcsAllowFirstMapToFinish.TrySetResult(true);

        await firstStartTask;

        Assert.NotNull(monitor.CurrentSnapshot);
        Assert.Equal(TimeSpan.FromSeconds(2), monitor.CurrentSnapshot.Timeline.Position);

        lock (emittedSnapshots)
        {
            Assert.NotEmpty(emittedSnapshots);
            var lastEvent = emittedSnapshots.Last();
            Assert.NotNull(lastEvent);
            Assert.Equal(TimeSpan.FromSeconds(2), lastEvent.Timeline.Position);

            var sawSecond = false;
            foreach (var snap in emittedSnapshots)
            {
                if (snap?.Timeline.Position == TimeSpan.FromSeconds(2))
                {
                    sawSecond = true;
                }
                else if (sawSecond && snap?.Timeline.Position == TimeSpan.FromSeconds(1))
                {
                    Assert.Fail("Stale snapshot was emitted after newer snapshot!");
                }
            }
        }
    }

    [Fact]
    public async Task MonitorDoesNotEmitSnapshotChangedAfterDispose()
    {
        var session = new FakeSession("Spotify.exe", MediaPlaybackState.Playing);
        var source = new FakeSessionSource(session);

        var monitor = new MediaSessionMonitor(source);
        await monitor.StartAsync(TestContext.Current.CancellationToken);

        var eventsAfterDispose = 0;
        monitor.SnapshotChanged += (_, _) => Interlocked.Increment(ref eventsAfterDispose);

        await monitor.DisposeAsync();

        session.RaisePlaybackInfoChanged();
        monitor.TryRefreshTimeline();

        Assert.Equal(0, eventsAfterDispose);
    }

    [Fact]
    public async Task TryRefreshTimelineAdvancesPlayingPositionAndUpdatesSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new FakeSession("chrome.exe", MediaPlaybackState.Playing)
        {
            MediaProperties = new NativeMediaProperties("YouTube Video", "Channel", null, null),
            PlaybackInfo = new NativePlaybackInfo(MediaPlaybackState.Playing, PlaybackRate: 1.0),
            TimelineProperties = new NativeTimelineProperties(
                TimeSpan.Zero,
                TimeSpan.FromMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.FromMinutes(5),
                LastUpdatedTime: now.AddSeconds(-10))
        };
        var source = new FakeSessionSource(session);

        await using var monitor = new MediaSessionMonitor(source);
        await monitor.StartAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(monitor.CurrentSnapshot);
        var initialPosition = monitor.CurrentSnapshot.Timeline.Position;

        session.TimelineProperties = session.TimelineProperties with
        {
            LastUpdatedTime = now.AddSeconds(-20)
        };

        var refreshed = monitor.TryRefreshTimeline();

        Assert.True(refreshed);
        Assert.True(monitor.CurrentSnapshot.Timeline.Position > initialPosition);
    }

    [Fact]
    public async Task TimelinePollDoesNotPreemptPendingSessionSwitch()
    {
        var sessionA = new FakeSession("appA", MediaPlaybackState.Playing)
        {
            MediaProperties = new NativeMediaProperties("Song A", "Artist A", null, null),
            TimelineProperties = new NativeTimelineProperties(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromMinutes(3))
        };
        var sessionB = new FakeSession("appB", MediaPlaybackState.Playing)
        {
            MediaProperties = new NativeMediaProperties("Song B", "Artist B", null, null),
            TimelineProperties = new NativeTimelineProperties(TimeSpan.Zero, TimeSpan.FromMinutes(4), TimeSpan.FromSeconds(20), TimeSpan.Zero, TimeSpan.FromMinutes(4))
        };

        var source = new FakeSessionSource(sessionA);

        var tcsMapBStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsAllowMapBToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var mapper = new CallbackMediaSessionMapper(async (s, ct) =>
        {
            if (s.SourceAppUserModelId == "appB")
            {
                tcsMapBStarted.TrySetResult(true);
                await tcsAllowMapBToFinish.Task.ConfigureAwait(false);
                return new MediaSnapshot(
                    s.SourceAppUserModelId,
                    "Song B",
                    "Artist B",
                    null,
                    MediaPlaybackState.Playing,
                    new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(4), TimeSpan.FromSeconds(20), TimeSpan.Zero, TimeSpan.FromMinutes(4)),
                    null);
            }

            return new MediaSnapshot(
                s.SourceAppUserModelId,
                "Song A",
                "Artist A",
                null,
                MediaPlaybackState.Playing,
                new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromMinutes(3)),
                null);
        });

        await using var monitor = new MediaSessionMonitor(source, mapper: mapper);
        await monitor.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Song A", monitor.CurrentSnapshot?.Title);

        // Switch to sessionB
        source.ReplaceSessions(sessionB);
        await tcsMapBStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // While map B is pending, try to poll timeline
        var polled = monitor.TryRefreshTimeline();
        Assert.False(polled, "Timeline poll must not preempt pending session switch");

        // Release map B
        tcsAllowMapBToFinish.TrySetResult(true);

        // Wait for B to settle
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (monitor.CurrentSnapshot?.Title != "Song B" && DateTime.UtcNow < timeout)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.NotNull(monitor.CurrentSnapshot);
        Assert.Equal("appB", monitor.CurrentSnapshot.SourceAppUserModelId);
        Assert.Equal("Song B", monitor.CurrentSnapshot.Title);
        Assert.Equal(TimeSpan.FromSeconds(20), monitor.CurrentSnapshot.Timeline.Position);
    }

    [Fact]
    public async Task TimelinePollDuringNewTrackMappingDoesNotDisplaceNewTrackMetadata()
    {
        var session = new FakeSession("app", MediaPlaybackState.Playing)
        {
            MediaProperties = new NativeMediaProperties("Old Track", "Old Artist", null, null),
            TimelineProperties = new NativeTimelineProperties(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromMinutes(3))
        };

        var getterStartedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowGetterToFinishTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mapperStartedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowMapperToFinishTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var getterBlockActive = false;

        session.OnGetTimelineProperties = () =>
        {
            if (getterBlockActive)
            {
                getterStartedTcs.TrySetResult(true);
                allowGetterToFinishTcs.Task.GetAwaiter().GetResult();
            }
            return session.TimelineProperties;
        };

        var currentTrackTitle = "Old Track";

        var mapper = new CallbackMediaSessionMapper(async (s, ct) =>
        {
            if (currentTrackTitle == "New Track")
            {
                mapperStartedTcs.TrySetResult(true);
                await allowMapperToFinishTcs.Task.ConfigureAwait(false);
                return new MediaSnapshot(
                    s.SourceAppUserModelId,
                    "New Track",
                    "New Artist",
                    null,
                    MediaPlaybackState.Playing,
                    new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(4), TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.FromMinutes(4)),
                    null);
            }

            return new MediaSnapshot(
                s.SourceAppUserModelId,
                "Old Track",
                "Old Artist",
                null,
                MediaPlaybackState.Playing,
                new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromMinutes(3)),
                null);
        });

        var source = new FakeSessionSource(session);
        await using var monitor = new MediaSessionMonitor(source, mapper: mapper);

        var emittedSnapshots = new List<MediaSnapshot?>();
        monitor.SnapshotChanged += (_, snap) =>
        {
            lock (emittedSnapshots)
            {
                emittedSnapshots.Add(snap);
            }
        };

        await monitor.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Old Track", monitor.CurrentSnapshot?.Title);

        // 1. Arm getter blocking and simulate new position on old track
        getterBlockActive = true;
        session.TimelineProperties = session.TimelineProperties with { Position = TimeSpan.FromSeconds(11) };

        // 2. Start timeline poll on background thread
        var pollTask = Task.Run(() => monitor.TryRefreshTimeline());

        // 3. Wait until poll is blocked inside GetTimelineProperties
        await getterStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // 4. While getter is blocked, event for new track arrives
        currentTrackTitle = "New Track";
        session.MediaProperties = new NativeMediaProperties("New Track", "New Artist", null, null);
        session.RaiseMediaPropertiesChanged();

        // 5. Wait until mapper starts mapping new track
        await mapperStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // 6. Release getter/poll
        allowGetterToFinishTcs.TrySetResult(true);
        var pollAdvanced = await pollTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Poll should not commit because new track mapping is active / sequence changed
        Assert.False(pollAdvanced, "Timeline poll must be aborted when active refresh is running");

        // 7. Release mapper
        allowMapperToFinishTcs.TrySetResult(true);

        // 8. Wait for new track to settle
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (monitor.CurrentSnapshot?.Title != "New Track" && DateTime.UtcNow < timeout)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        // 9. Final metadata and event must be NEW
        Assert.NotNull(monitor.CurrentSnapshot);
        Assert.Equal("New Track", monitor.CurrentSnapshot.Title);
        Assert.Equal("New Artist", monitor.CurrentSnapshot.Artist);

        lock (emittedSnapshots)
        {
            var lastEmitted = emittedSnapshots.LastOrDefault();
            Assert.NotNull(lastEmitted);
            Assert.Equal("New Track", lastEmitted.Title);
        }
    }

    [Fact]
    public async Task TimelinePollRecoversAfterFailedMetadataRefreshAndPreservesLastGoodSnapshot()
    {
        var session = new FakeSession("app", MediaPlaybackState.Playing)
        {
            MediaProperties = new NativeMediaProperties("Track 1", "Artist 1", null, null),
            TimelineProperties = new NativeTimelineProperties(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromMinutes(3))
        };

        var failNext = false;
        var mapper = new CallbackMediaSessionMapper((s, ct) =>
        {
            if (failNext)
            {
                failNext = false;
                throw new IOException("Simulated transient metadata error");
            }

            return Task.FromResult(new MediaSnapshot(
                s.SourceAppUserModelId,
                "Track 1",
                "Artist 1",
                null,
                MediaPlaybackState.Playing,
                new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), session.TimelineProperties.Position, TimeSpan.Zero, TimeSpan.FromMinutes(3)),
                null));
        });

        var source = new FakeSessionSource(session);
        await using var monitor = new MediaSessionMonitor(source, mapper: mapper);

        var failedEvents = new List<string>();
        monitor.RefreshFailed += (_, args) => failedEvents.Add(args.Operation);

        var emittedSnapshots = new List<MediaSnapshot?>();
        monitor.SnapshotChanged += (_, snap) => emittedSnapshots.Add(snap);

        await monitor.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Track 1", monitor.CurrentSnapshot?.Title);
        Assert.Equal(TimeSpan.FromSeconds(10), monitor.CurrentSnapshot?.Timeline.Position);

        // Trigger failed metadata refresh
        failNext = true;
        session.RaiseMediaPropertiesChanged();

        // Wait briefly for failure to be handled
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (failedEvents.Count == 0 && DateTime.UtcNow < timeout)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Contains("MapSession", failedEvents);
        // Last good snapshot preserved
        Assert.Equal("Track 1", monitor.CurrentSnapshot?.Title);
        Assert.Equal(TimeSpan.FromSeconds(10), monitor.CurrentSnapshot?.Timeline.Position);

        // Now advance position and poll
        session.TimelineProperties = session.TimelineProperties with { Position = TimeSpan.FromSeconds(12) };
        var polled = monitor.TryRefreshTimeline();

        Assert.True(polled, "Timeline poll must recover and advance after transient metadata failure");
        Assert.Equal(TimeSpan.FromSeconds(12), monitor.CurrentSnapshot?.Timeline.Position);
        Assert.Equal(TimeSpan.FromSeconds(12), emittedSnapshots.Last()?.Timeline.Position);
    }

    private sealed class CallbackMediaSessionMapper(Func<IWindowsMediaSession, CancellationToken, Task<MediaSnapshot>> callback) : IMediaSessionMapper
    {
        public Task<MediaSnapshot> MapAsync(IWindowsMediaSession session, CancellationToken cancellationToken = default)
        {
            return callback(session, cancellationToken);
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

        public NativePlaybackInfo PlaybackInfo { get; set; } = new(playbackState);

        public NativePlaybackInfo GetPlaybackInfo()
        {
            return PlaybackInfo;
        }

        public Func<NativeTimelineProperties>? OnGetTimelineProperties { get; set; }

        public NativeTimelineProperties GetTimelineProperties()
        {
            return OnGetTimelineProperties?.Invoke() ?? TimelineProperties;
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
