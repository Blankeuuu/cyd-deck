namespace StreamDeck.Media;

public sealed class MediaSessionRefreshFailedEventArgs(string operation, string exceptionType, string? paramName = null) : EventArgs
{
    public MediaSessionRefreshFailedEventArgs(string exceptionType)
        : this("Refresh", exceptionType)
    {
    }

    public string Operation { get; } = operation;

    public string ExceptionType { get; } = exceptionType;

    public string? ParamName { get; } = SanitizeToken(paramName);

    public static MediaSessionRefreshFailedEventArgs FromException(string operation, Exception exception)
    {
        return new MediaSessionRefreshFailedEventArgs(
            operation,
            exception.GetType().Name,
            exception is ArgumentException argumentException ? argumentException.ParamName : null);
    }

    private static string? SanitizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        const int maxLength = 64;
        var buffer = new char[Math.Min(value.Length, maxLength)];
        var count = 0;
        foreach (var character in value)
        {
            if (count >= buffer.Length)
            {
                break;
            }

            buffer[count++] = char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.'
                ? character
                : '_';
        }

        return count == 0 ? null : new string(buffer, 0, count);
    }

}

public sealed class MediaSessionMonitor : IAsyncDisposable
{
    private readonly IWindowsMediaSessionSource _source;
    private readonly IMediaSessionSelector _selector;
    private readonly IMediaSessionMapper _mapper;
    private readonly object _gate = new();
    private readonly object _publishGate = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<IWindowsMediaSession> _subscribedSessions = [];
    private IWindowsMediaSession? _selectedSession;
    private IWindowsMediaSession? _currentSnapshotSession;
    private bool _started;
    private bool _disposed;
    private int _activeRefreshCount;
    private long _refreshSequence;
    private long _lastAppliedSequence;
    private long _snapshotVersion;
    private long _lastEmittedVersion;

    public MediaSessionMonitor(
        IWindowsMediaSessionSource source,
        IMediaSessionSelector? selector = null,
        IMediaSessionMapper? mapper = null)
    {
        _source = source;
        _selector = selector ?? new DefaultMediaSessionSelector();
        _mapper = mapper ?? new WindowsMediaSessionMapper();
    }

    public event EventHandler<MediaSnapshot?>? SnapshotChanged;

    public event EventHandler<MediaSessionRefreshFailedEventArgs>? RefreshFailed;

    public MediaSnapshot? CurrentSnapshot { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        await _source.InitializeAsync(cancellationToken).ConfigureAwait(false);
        _source.SessionsChanged += HandleSessionsChanged;
        _started = true;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> PreviousAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync(session => session.TrySkipPreviousAsync(cancellationToken), cancellationToken);
    }

    public Task<bool> PlayAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync(session => session.TryPlayAsync(cancellationToken), cancellationToken);
    }

    public Task<bool> PauseAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync(session => session.TryPauseAsync(cancellationToken), cancellationToken);
    }

    public Task<bool> TogglePlayPauseAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync(session => session.TryTogglePlayPauseAsync(cancellationToken), cancellationToken);
    }

    public Task<bool> NextAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync(session => session.TrySkipNextAsync(cancellationToken), cancellationToken);
    }

    public bool TryRefreshTimeline()
    {
        IWindowsMediaSession? selectedSession;
        MediaSnapshot? currentSnapshot;
        long capturedRefreshSequence;
        long capturedSnapshotVersion;

        lock (_gate)
        {
            if (_disposed || _selectedSession is null || CurrentSnapshot is null)
            {
                return false;
            }

            if (!ReferenceEquals(_selectedSession, _currentSnapshotSession))
            {
                return false;
            }

            if (_activeRefreshCount > 0)
            {
                return false;
            }

            selectedSession = _selectedSession;
            currentSnapshot = CurrentSnapshot;
            capturedRefreshSequence = _refreshSequence;
            capturedSnapshotVersion = _snapshotVersion;
        }

        if (currentSnapshot.PlaybackState != MediaPlaybackState.Playing)
        {
            return false;
        }

        var timeline = selectedSession.GetTimelineProperties();
        var playbackInfo = selectedSession.GetPlaybackInfo();
        var position = WindowsMediaSessionMapper.CalculateLivePosition(timeline, playbackInfo, DateTimeOffset.UtcNow);

        if (position == currentSnapshot.Timeline.Position &&
            (timeline.EndTime == TimeSpan.Zero || timeline.EndTime == currentSnapshot.Timeline.EndTime))
        {
            return false;
        }

        var updatedTimeline = currentSnapshot.Timeline with
        {
            Position = position,
            EndTime = timeline.EndTime > TimeSpan.Zero ? timeline.EndTime : currentSnapshot.Timeline.EndTime
        };

        var updatedSnapshot = currentSnapshot with { Timeline = updatedTimeline };
        long version;

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            if (!ReferenceEquals(_selectedSession, selectedSession) ||
                !ReferenceEquals(_currentSnapshotSession, selectedSession) ||
                !ReferenceEquals(CurrentSnapshot, currentSnapshot) ||
                _activeRefreshCount > 0 ||
                _refreshSequence != capturedRefreshSequence ||
                _snapshotVersion != capturedSnapshotVersion)
            {
                return false;
            }

            CurrentSnapshot = updatedSnapshot;
            version = ++_snapshotVersion;
        }

        PublishSnapshotChanged(updatedSnapshot, version);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cts.Cancel();
        }

        lock (_publishGate)
        {
            SnapshotChanged = null;
            RefreshFailed = null;
        }

        _source.SessionsChanged -= HandleSessionsChanged;

        lock (_gate)
        {
            foreach (var session in _subscribedSessions)
            {
                UnsubscribeSession(session);
            }

            _subscribedSessions.Clear();
            _selectedSession = null;
            _currentSnapshotSession = null;
        }

        await _source.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }

    private async Task<bool> ExecuteCommandAsync(Func<IWindowsMediaSession, Task<bool>> command, CancellationToken cancellationToken)
    {
        try
        {
            // CurrentSession can change without a sessions-list notification.
            // Resolve it at the command boundary rather than controlling a cached session.
            lock (_gate)
            {
                if (_disposed) return false;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var sessions = _source.GetSessions();
            var selectedSession = _selector.Select(_source.GetCurrentSession(), sessions);
            if (selectedSession is null) return false;

            var accepted = await command(selectedSession).ConfigureAwait(false);
            // Native acceptance is not playback confirmation. Publish only a new read,
            // also after rejection, because SMTC events can be delayed or omitted.
            await RefreshSafelyAsync(cancellationToken).ConfigureAwait(false);
            return accepted;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private void HandleSessionsChanged(object? sender, EventArgs args)
    {
        _ = RefreshSafelyAsync(CancellationToken.None);
    }

    private void HandleSessionChanged(object? sender, EventArgs args)
    {
        _ = RefreshSafelyAsync(CancellationToken.None);
    }

    private async Task RefreshSafelyAsync(CancellationToken cancellationToken)
    {
        var context = new MediaSessionRefreshContext();
        try
        {
            await RefreshAsync(cancellationToken, context).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            RefreshFailed?.Invoke(this, MediaSessionRefreshFailedEventArgs.FromException(context.Operation, exception));
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken, MediaSessionRefreshContext? context = null)
    {
        CancellationToken effectiveCt;
        long sequence;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _activeRefreshCount++;
            sequence = ++_refreshSequence;
            effectiveCt = _cts.Token;
        }

        try
        {
            using var linkedCts = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, effectiveCt)
                : null;
            var token = linkedCts?.Token ?? effectiveCt;

            context ??= new MediaSessionRefreshContext();
            var sessions = RunRefreshOperation(context, "GetSessions", _source.GetSessions);
            ReconcileSubscriptions(sessions);

            var currentSession = RunRefreshOperation(context, "GetCurrentSession", _source.GetCurrentSession);
            var selectedSession = RunRefreshOperation(context, "SelectSession", () => _selector.Select(currentSession, sessions));
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                if (sequence >= _lastAppliedSequence)
                {
                    _selectedSession = selectedSession;
                }
            }

            if (selectedSession is null)
            {
                SetCurrentSnapshot(null, sequence, null);
                return;
            }

            var snapshot = await RunRefreshOperationAsync(
                context,
                "MapSession",
                () => _mapper.MapAsync(selectedSession, token)).ConfigureAwait(false);

            lock (_gate)
            {
                if (_disposed || sequence < _lastAppliedSequence || !ReferenceEquals(_selectedSession, selectedSession))
                {
                    return;
                }
            }

            SetCurrentSnapshot(snapshot, sequence, selectedSession);
        }
        finally
        {
            lock (_gate)
            {
                _activeRefreshCount--;
            }
        }
    }

    private static T RunRefreshOperation<T>(MediaSessionRefreshContext context, string operation, Func<T> action)
    {
        context.Operation = operation;
        return action();
    }

    private static async Task<T> RunRefreshOperationAsync<T>(MediaSessionRefreshContext context, string operation, Func<Task<T>> action)
    {
        context.Operation = operation;
        return await action().ConfigureAwait(false);
    }

    private sealed class MediaSessionRefreshContext
    {
        public string Operation { get; set; } = "Refresh";
    }

    private void SetCurrentSnapshot(MediaSnapshot? snapshot, long sequence, IWindowsMediaSession? session)
    {
        long version;
        lock (_gate)
        {
            if (_disposed || sequence < _lastAppliedSequence)
            {
                return;
            }

            if (session is not null && !ReferenceEquals(_selectedSession, session))
            {
                return;
            }

            _lastAppliedSequence = sequence;
            _currentSnapshotSession = session;

            if (Equals(CurrentSnapshot, snapshot))
            {
                return;
            }

            CurrentSnapshot = snapshot;
            version = ++_snapshotVersion;
        }

        PublishSnapshotChanged(snapshot, version);
    }

    private void PublishSnapshotChanged(MediaSnapshot? snapshot, long version)
    {
        lock (_publishGate)
        {
            lock (_gate)
            {
                if (_disposed || version <= _lastEmittedVersion)
                {
                    return;
                }

                _lastEmittedVersion = version;
            }

            SnapshotChanged?.Invoke(this, snapshot);
        }
    }

    private void ReconcileSubscriptions(IReadOnlyList<IWindowsMediaSession> sessions)
    {
        lock (_gate)
        {
            var activeSessions = sessions.ToHashSet();

            foreach (var session in _subscribedSessions.ToArray())
            {
                if (!activeSessions.Contains(session))
                {
                    UnsubscribeSession(session);
                    _subscribedSessions.Remove(session);
                }
            }

            foreach (var session in sessions)
            {
                if (_subscribedSessions.Add(session))
                {
                    SubscribeSession(session);
                }
            }
        }
    }

    private void SubscribeSession(IWindowsMediaSession session)
    {
        session.MediaPropertiesChanged += HandleSessionChanged;
        session.PlaybackInfoChanged += HandleSessionChanged;
        session.TimelinePropertiesChanged += HandleSessionChanged;
    }

    private void UnsubscribeSession(IWindowsMediaSession session)
    {
        session.MediaPropertiesChanged -= HandleSessionChanged;
        session.PlaybackInfoChanged -= HandleSessionChanged;
        session.TimelinePropertiesChanged -= HandleSessionChanged;
    }
}
