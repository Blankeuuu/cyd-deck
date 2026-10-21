using System.Diagnostics;
using StreamDeck.Media;
using Xunit;

namespace StreamDeck.Media.Tests;

public sealed class MediaCommandAckTests
{
    [Fact]
    public async Task SynchronousMapperWorkCannotDelayNativeAcceptance()
    {
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var session = new TestSession("app1", MediaPlaybackState.Playing);
        var mapper = new CallbackMapper((s, ct) =>
        {
            if (Interlocked.Increment(ref calls) > 1) release.Wait(ct);
            return Task.FromResult(new MediaSnapshot(s.SourceAppUserModelId, "Track", "Artist", "Album",
                MediaPlaybackState.Playing, new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3),
                    TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMinutes(3)), null));
        });
        await using var monitor = new MediaSessionMonitor(new TestSource(session), mapper: mapper);
        await monitor.StartAsync(TestContext.Current.CancellationToken);
        var command = Task.Run(() => monitor.PreviousAsync(TestContext.Current.CancellationToken));
        try
        {
            Assert.True(await command.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task CommandReturnsImmediatelyWithoutWaitingForSlowMetadataMapping()
    {
        var session = new TestSession("app1", MediaPlaybackState.Playing);
        var source = new TestSource(session);

        var mapperCalledTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMapperTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mapCount = 0;

        var mapper = new CallbackMapper(async (s, ct) =>
        {
            var count = Interlocked.Increment(ref mapCount);
            if (count > 1)
            {
                // Pierwsze mapowanie jest w StartAsync. Drugie jest wyzwalane przez komendę.
                mapperCalledTcs.TrySetResult(true);
                await releaseMapperTcs.Task.WaitAsync(ct).ConfigureAwait(false);
            }

            return new MediaSnapshot(
                s.SourceAppUserModelId,
                $"Track {count}",
                "Artist",
                "Album",
                s.GetPlaybackInfo().PlaybackState,
                new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromMinutes(3)),
                null);
        });

        await using var monitor = new MediaSessionMonitor(source, mapper: mapper);
        await monitor.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Track 1", monitor.CurrentSnapshot?.Title);

        // Wywołujemy komendę (np. PreviousAsync).
        // W zablokowanym starym kodzie komenda awaituje RefreshSafelyAsync, więc wisi dopóki mapper nie zostanie zwolniony!
        var sw = Stopwatch.StartNew();
        var commandTask = monitor.PreviousAsync(TestContext.Current.CancellationToken);

        // Czekamy na powrót komendy z krótkim limitem (np. 300ms).
        // Mapper jest NADAL ZABLOKOWANY (releaseMapperTcs nie jest ustawione!).
        var completedInTime = await Task.WhenAny(commandTask, Task.Delay(300, TestContext.Current.CancellationToken)) == commandTask;

        try
        {
            Assert.True(completedInTime, "Command must return accepted immediately without waiting for slow metadata/thumbnail mapping!");
            var accepted = await commandTask;
            Assert.True(accepted);
        }
        finally
        {
            // Zwalniamy mapper, aby monitor mógł dokończyć pracę i czysto się zamknąć
            releaseMapperTcs.TrySetResult(true);
        }

        // Po odblokowaniu mappera w tle snapshot powinien ostatecznie zostać zaktualizowany
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (monitor.CurrentSnapshot?.Title != "Track 2" && DateTime.UtcNow < timeout)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Equal("Track 2", monitor.CurrentSnapshot?.Title);
    }

    [Fact]
    public async Task RepeatedCommandsDoNotTriggerUnboundedRefreshStorm()
    {
        var session = new TestSession("app1", MediaPlaybackState.Playing);
        var source = new TestSource(session);

        var firstCommandMapStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstMap = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var totalMaps = 0;

        var mapper = new CallbackMapper(async (s, ct) =>
        {
            var count = Interlocked.Increment(ref totalMaps);
            if (count == 2)
            {
                // Pierwsza komenda wyzwala count == 2
                firstCommandMapStarted.TrySetResult(true);
                await releaseFirstMap.Task.WaitAsync(ct).ConfigureAwait(false);
            }

            return new MediaSnapshot(
                s.SourceAppUserModelId,
                $"Track {count}",
                "Artist",
                "Album",
                s.GetPlaybackInfo().PlaybackState,
                new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromMinutes(3)),
                null);
        });

        await using var monitor = new MediaSessionMonitor(source, mapper: mapper);
        await monitor.StartAsync(TestContext.Current.CancellationToken);

        // Wysyłamy pierwszą komendę
        var firstCmd = monitor.PreviousAsync(TestContext.Current.CancellationToken);

        // Czekamy aż mapper dla pierwszej komendy wystartuje i zablokuje się
        await firstCommandMapStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // W czasie gdy mapper pierwszej komendy jest zablokowany, wysyłamy serię kolejnych komend (np. 10 kliknięć)
        var burstTasks = new List<Task<bool>>();
        for (var i = 0; i < 10; i++)
        {
            burstTasks.Add(monitor.PreviousAsync(TestContext.Current.CancellationToken));
        }

        // Wszystkie komendy powinny natychmiast zwrócić accepted bez blokowania
        var allBurst = Task.WhenAll(burstTasks);
        var burstCompleted = await Task.WhenAny(allBurst, Task.Delay(500, TestContext.Current.CancellationToken)) == allBurst;
        Assert.True(burstCompleted, "All burst commands should return accepted immediately");

        var results = await allBurst;
        Assert.All(results, Assert.True);

        // Zwalniamy pierwszy zablokowany mapper
        releaseFirstMap.TrySetResult(true);
        await firstCmd;

        // Czekamy chwilę na ustabilizowanie się odświeżeń w tle
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Ponieważ było 10 komend w serii podczas trwania pierwszego mapowania,
        // powinny one zostać zcoalescowane (bounded) do co najwyżej 1 dodatkowego mapowania,
        // a nie 10 osobnych równoległych wywołań mappera!
        // totalMaps: 1 (start) + 1 (pierwsza komenda) + 1 (skonsolidowane kolejne komendy) = <= 3
        Assert.InRange(totalMaps, 2, 4);
    }

    private sealed class CallbackMapper(Func<IWindowsMediaSession, CancellationToken, Task<MediaSnapshot>> callback) : IMediaSessionMapper
    {
        public Task<MediaSnapshot> MapAsync(IWindowsMediaSession session, CancellationToken cancellationToken = default)
        {
            return callback(session, cancellationToken);
        }
    }

    private sealed class TestSource(IWindowsMediaSession session) : IWindowsMediaSessionSource
    {
        public event EventHandler? SessionsChanged { add { } remove { } }
        public IReadOnlyList<IWindowsMediaSession> Sessions { get; set; } = [session];
        public IWindowsMediaSession? CurrentSession { get; set; } = session;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public IReadOnlyList<IWindowsMediaSession> GetSessions() => Sessions;
        public IWindowsMediaSession? GetCurrentSession() => CurrentSession;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestSession(string sourceAppUserModelId, MediaPlaybackState state) : IWindowsMediaSession
    {
        public event EventHandler? MediaPropertiesChanged { add { } remove { } }
        public event EventHandler? PlaybackInfoChanged { add { } remove { } }
        public event EventHandler? TimelinePropertiesChanged { add { } remove { } }

        public string? SourceAppUserModelId { get; } = sourceAppUserModelId;
        public MediaPlaybackState State { get; set; } = state;

        public Task<NativeMediaProperties> GetMediaPropertiesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new NativeMediaProperties("Title", "Artist", "Album", null));

        public NativePlaybackInfo GetPlaybackInfo() => new(State);

        public NativeTimelineProperties GetTimelineProperties() => new(
            TimeSpan.Zero,
            TimeSpan.FromMinutes(3),
            TimeSpan.FromSeconds(10),
            TimeSpan.Zero,
            TimeSpan.FromMinutes(3));

        public Task<bool> TryPlayAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> TryPauseAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> TryTogglePlayPauseAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> TrySkipNextAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> TrySkipPreviousAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public void Dispose() { }
    }
}
