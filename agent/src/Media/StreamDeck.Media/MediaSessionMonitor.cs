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
    private readonly HashSet<IWindowsMediaSession> _subscribedSessions = [];
    private IWindowsMediaSession? _selectedSession;
    private bool _started;

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
        return ExecuteCommandAsync(session => session.TrySkipPreviousAsync(cancellationToken));
    }

    public Task<bool> PlayAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync(session => session.TryPlayAsync(cancellationToken));
    }

    public Task<bool> PauseAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync(session => session.TryPauseAsync(cancellationToken));
    }

    public Task<bool> TogglePlayPauseAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync(session => session.TryTogglePlayPauseAsync(cancellationToken));
    }

    public Task<bool> NextAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync(session => session.TrySkipNextAsync(cancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        _source.SessionsChanged -= HandleSessionsChanged;

        lock (_gate)
        {
            foreach (var session in _subscribedSessions)
            {
                UnsubscribeSession(session);
            }

            _subscribedSessions.Clear();
            _selectedSession = null;
        }

        await _source.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<bool> ExecuteCommandAsync(Func<IWindowsMediaSession, Task<bool>> command)
    {
        var selectedSession = GetSelectedSession();
        if (selectedSession is null)
        {
            return false;
        }

        try
        {
            return await command(selectedSession).ConfigureAwait(false);
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

    private IWindowsMediaSession? GetSelectedSession()
    {
        lock (_gate)
        {
            return _selectedSession;
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
        context ??= new MediaSessionRefreshContext();
        var sessions = RunRefreshOperation(context, "GetSessions", _source.GetSessions);
        ReconcileSubscriptions(sessions);

        var currentSession = RunRefreshOperation(context, "GetCurrentSession", _source.GetCurrentSession);
        var selectedSession = RunRefreshOperation(context, "SelectSession", () => _selector.Select(currentSession, sessions));
        lock (_gate)
        {
            _selectedSession = selectedSession;
        }

        if (selectedSession is null)
        {
            SetCurrentSnapshot(null);
            return;
        }

        var snapshot = await RunRefreshOperationAsync(
            context,
            "MapSession",
            () => _mapper.MapAsync(selectedSession, cancellationToken)).ConfigureAwait(false);
        SetCurrentSnapshot(snapshot);
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

    private void SetCurrentSnapshot(MediaSnapshot? snapshot)
    {
        if (Equals(CurrentSnapshot, snapshot))
        {
            return;
        }

        CurrentSnapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
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
