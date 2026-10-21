using System.Text.Json;
using StreamDeck.Host;
using StreamDeck.Media;
using StreamDeck.Protocol;

var failures = 0;
foreach (var (name, test) in new (string, Func<Task>)[]
{
    ("Pause refreshes observed playback without a native event", PauseWithoutEvent),
    ("False result is a failed protocol1 ACK and keeps playing", RejectedPause),
    ("Current session changes without a sessions-list event", ChangedCurrentSession),
    ("Repeated click reads actual playback, not predicted state", RepeatedClick),
    ("Accepted command does not fabricate paused state", AcceptedWithoutPlaybackChange),
    ("Session switch while a command is pending keeps the new snapshot", SwitchWhilePending),
    ("Native exception is a failed ACK", NativeException),
    ("Metadata failure cannot turn accepted pause into rejection", MetadataFailure),
    ("Blocked mapper does not stall protocol1 ACK", BlockedMapperAck)
})
{
    try { await test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
return failures == 0 ? 0 : 1;

static void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

static async Task WaitForSnapshotAsync(MediaSessionMonitor monitor, Func<MediaSnapshot?, bool> predicate, TimeSpan timeout = default)
{
    if (timeout == default) timeout = TimeSpan.FromSeconds(2);
    var start = DateTime.UtcNow;
    while (!predicate(monitor.CurrentSnapshot) && DateTime.UtcNow - start < timeout)
    {
        await Task.Delay(10);
    }
}

static async Task PauseWithoutEvent()
{
    var session = new Session();
    await using var monitor = new MediaSessionMonitor(new Source(session));
    await monitor.StartAsync();
    Check(await Send(monitor, MediaCommand.Pause), "command rejected");
    await WaitForSnapshotAsync(monitor, s => s?.PlaybackState == session.State);
    Check(monitor.CurrentSnapshot?.PlaybackState == session.State, "snapshot still playing after real pause");
}

static async Task RejectedPause()
{
    var session = new Session { Accept = false };
    await using var monitor = new MediaSessionMonitor(new Source(session));
    await monitor.StartAsync();
    Check(!await Send(monitor, MediaCommand.Pause), "false converted to successful ACK");
    Check(session.State == MediaPlaybackState.Playing && monitor.CurrentSnapshot?.PlaybackState == session.State,
        "rejection stopped progress while audio remained playing");
    session.Position = TimeSpan.FromSeconds(21);
    Check(await RefreshTimelineWhenIdleAsync(monitor, session.Position) && monitor.CurrentSnapshot?.Timeline.Position == session.Position,
        "progress froze after rejection while audio remained playing");
}

static async Task ChangedCurrentSession()
{
    var old = new Session();
    var active = new Session();
    var source = new Source(old) { Sessions = [old, active] };
    await using var monitor = new MediaSessionMonitor(source);
    await monitor.StartAsync();
    source.Current = active; // GSMTC current changes without adding/removing sessions.
    Check(await Send(monitor, MediaCommand.Pause), "command rejected");
    Check(active.State == MediaPlaybackState.Paused, "audible current session still playing");
    Check(old.State == MediaPlaybackState.Playing, "paused the stale session instead");
}

static async Task RepeatedClick()
{
    var session = new Session();
    await using var monitor = new MediaSessionMonitor(new Source(session));
    await monitor.StartAsync();
    Check(await Send(monitor, MediaCommand.PlayPause), "first click rejected");
    await WaitForSnapshotAsync(monitor, s => s?.PlaybackState == MediaPlaybackState.Paused);
    Check(monitor.CurrentSnapshot?.PlaybackState == MediaPlaybackState.Paused, "first click not observed");
    Check(await Send(monitor, MediaCommand.PlayPause), "second click rejected");
    await WaitForSnapshotAsync(monitor, s => s?.PlaybackState == session.State);
    Check(session.State == MediaPlaybackState.Playing && monitor.CurrentSnapshot?.PlaybackState == session.State,
        "second click did not resume observed playback");
}

static async Task AcceptedWithoutPlaybackChange()
{
    var session = new Session { Apply = false };
    await using var monitor = new MediaSessionMonitor(new Source(session));
    await monitor.StartAsync();
    Check(await Send(monitor, MediaCommand.Pause), "native acceptance lost");
    Check(monitor.CurrentSnapshot?.PlaybackState == MediaPlaybackState.Playing, "ACK fabricated playback confirmation");
    session.Position = TimeSpan.FromSeconds(22);
    Check(await RefreshTimelineWhenIdleAsync(monitor, session.Position) && monitor.CurrentSnapshot?.Timeline.Position == session.Position,
        "acceptance without native pause froze progress");
}

static async Task SwitchWhilePending()
{
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var old = new Session { BeforeCommand = async () => { entered.SetResult(); await release.Task; } };
    var active = new Session();
    var source = new Source(old) { Sessions = [old, active] };
    await using var monitor = new MediaSessionMonitor(source);
    await monitor.StartAsync();
    var command = Send(monitor, MediaCommand.Pause);
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
    source.Current = active;
    release.SetResult();
    Check(await command.WaitAsync(TimeSpan.FromSeconds(2)), "pending command rejected");
    Check(active.State == MediaPlaybackState.Playing, "command replayed on a different session");
    Check(monitor.CurrentSnapshot?.SourceAppUserModelId == active.SourceAppUserModelId &&
        monitor.CurrentSnapshot.PlaybackState == MediaPlaybackState.Playing, "old result replaced new session state");
}

static async Task NativeException()
{
    var session = new Session { BeforeCommand = () => throw new InvalidOperationException("fake native failure") };
    await using var monitor = new MediaSessionMonitor(new Source(session));
    await monitor.StartAsync();
    Check(!await Send(monitor, MediaCommand.Pause), "exception converted to success");
    Check(monitor.CurrentSnapshot?.PlaybackState == MediaPlaybackState.Playing, "exception fabricated pause");
}

static async Task MetadataFailure()
{
    var session = new Session();
    await using var monitor = new MediaSessionMonitor(new Source(session));
    await monitor.StartAsync();
    session.FailMetadata = true;
    Check(await Send(monitor, MediaCommand.Pause), "accepted command falsely rejected after metadata failure");
    Check(session.State == MediaPlaybackState.Paused, "command not executed");
    await Task.Delay(50);
    Check(monitor.CurrentSnapshot?.PlaybackState == MediaPlaybackState.Playing, "metadata failure fabricated confirmation");
}

static async Task BlockedMapperAck()
{
    var mapStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseMap = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var session = new Session();
    await using var monitor = new MediaSessionMonitor(new Source(session));
    await monitor.StartAsync();

    session.OnGetMediaProperties = async () =>
    {
        mapStarted.TrySetResult(true);
        await releaseMap.Task;
        return new NativeMediaProperties("Track", null, null, null);
    };

    var cmdTask = Send(monitor, MediaCommand.Previous);
    var completedInTime = await Task.WhenAny(cmdTask, Task.Delay(300)) == cmdTask;
    try
    {
        Check(completedInTime, "ACK stalled by blocked metadata mapping");
        Check(await cmdTask, "command was not accepted");
    }
    finally
    {
        releaseMap.TrySetResult(true);
    }
}

static async Task<bool> Send(MediaSessionMonitor monitor, MediaCommand command)
{
    var dispatcher = new DeviceCommandDispatcher(new MediaSessionMonitorController(monitor), new Unused(), new Unused(), new RejectingSystemCommandExecutor());
    var sink = new MemoryProtocolSink();
    var envelope = ProtocolSerializer.Parse(ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.MediaCommand,
        Guid.NewGuid().ToString("N"), new MediaCommandMessage(command))).Envelope!;
    await dispatcher.DispatchAsync(envelope, sink);
    using var ack = JsonDocument.Parse(sink.Messages.Single());
    Check(ack.RootElement.GetProperty("protocol").GetInt32() == 1, "protocol changed");
    return ack.RootElement.GetProperty("success").GetBoolean();
}

// TryRefreshTimeline returns false while a background refresh is active or when it already applied the position; wait for either.
static async Task<bool> RefreshTimelineWhenIdleAsync(MediaSessionMonitor monitor, TimeSpan expectedPosition)
{
    var deadline = DateTime.UtcNow.AddSeconds(2);
    while (DateTime.UtcNow < deadline)
    {
        if (monitor.TryRefreshTimeline() || monitor.CurrentSnapshot?.Timeline.Position == expectedPosition) return true;
        await Task.Delay(20);
    }
    return false;
}

sealed class Unused : IHostAudioController, IHostShortcutExecutor
{
    public Task<bool> SetVolumeAsync(int value, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> ToggleMuteAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> ExecuteAsync(string value, CancellationToken ct = default) => throw new NotSupportedException();
}

sealed class Source(Session session) : IWindowsMediaSessionSource
{
    public event EventHandler? SessionsChanged { add { } remove { } }
    public IWindowsMediaSession Current { get; set; } = session;
    public IReadOnlyList<IWindowsMediaSession> Sessions { get; set; } = [session];
    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public IReadOnlyList<IWindowsMediaSession> GetSessions() => Sessions;
    public IWindowsMediaSession GetCurrentSession() => Current;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

sealed class Session : IWindowsMediaSession
{
    public event EventHandler? MediaPropertiesChanged { add { } remove { } }
    public event EventHandler? PlaybackInfoChanged { add { } remove { } }
    public event EventHandler? TimelinePropertiesChanged { add { } remove { } }
    public string SourceAppUserModelId { get; } = Guid.NewGuid().ToString("N");
    public MediaPlaybackState State { get; private set; } = MediaPlaybackState.Playing;
    public bool Accept { get; init; } = true;
    public bool Apply { get; init; } = true;
    public Func<Task>? BeforeCommand { get; init; }
    public bool FailMetadata { get; set; }
    public TimeSpan Position { get; set; } = TimeSpan.FromSeconds(20);
    public Func<Task<NativeMediaProperties>>? OnGetMediaProperties { get; set; }
    public Task<NativeMediaProperties> GetMediaPropertiesAsync(CancellationToken ct = default) => OnGetMediaProperties is not null
        ? OnGetMediaProperties()
        : FailMetadata
            ? throw new InvalidOperationException("fake metadata failure")
            : Task.FromResult(new NativeMediaProperties("Track", null, null, null));
    public NativePlaybackInfo GetPlaybackInfo() => new(State);
    public NativeTimelineProperties GetTimelineProperties() => new(TimeSpan.Zero, TimeSpan.FromMinutes(3), Position, TimeSpan.Zero, TimeSpan.FromMinutes(3));
    public Task<bool> TryPlayAsync(CancellationToken ct = default) => Set(MediaPlaybackState.Playing);
    public Task<bool> TryPauseAsync(CancellationToken ct = default) => Set(MediaPlaybackState.Paused);
    public Task<bool> TryTogglePlayPauseAsync(CancellationToken ct = default) => Set(State == MediaPlaybackState.Playing ? MediaPlaybackState.Paused : MediaPlaybackState.Playing);
    public Task<bool> TrySkipNextAsync(CancellationToken ct = default) => Task.FromResult(Accept);
    public Task<bool> TrySkipPreviousAsync(CancellationToken ct = default) => Task.FromResult(Accept);
    private async Task<bool> Set(MediaPlaybackState state)
    {
        if (BeforeCommand is not null) await BeforeCommand();
        if (Accept && Apply) State = state;
        return Accept;
    }
    public void Dispose() { }
}
