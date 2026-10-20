using Windows.Media.Control;

namespace StreamDeck.Media;

public sealed class WindowsGlobalMediaSessionSource : IWindowsMediaSessionSource
{
    private readonly Dictionary<GlobalSystemMediaTransportControlsSession, WindowsGlobalMediaSession> _sessions = [];
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private bool _disposed;
    private readonly object _gate = new();

    public event EventHandler? SessionsChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_manager is not null)
        {
            return;
        }

        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.SessionsChanged += HandleSessionsChanged;
        _manager.CurrentSessionChanged += HandleCurrentSessionChanged;
    }

    public IReadOnlyList<IWindowsMediaSession> GetSessions()
    {
        lock (_gate)
        {
            var manager = GetInitializedManager();
            var nativeSessions = manager.GetSessions();
            var wrappedSessions = new List<IWindowsMediaSession>(nativeSessions.Count);
            var activeSessions = new HashSet<GlobalSystemMediaTransportControlsSession>();

            foreach (var nativeSession in nativeSessions)
        {
                activeSessions.Add(nativeSession);
                wrappedSessions.Add(GetOrCreateSession(nativeSession));
            }

            foreach (var staleSession in _sessions.Keys.Where(session => !activeSessions.Contains(session)).ToArray())
        {
                _sessions[staleSession].Dispose();
                _sessions.Remove(staleSession);
            }

            return wrappedSessions;
        }
    }

    public IWindowsMediaSession? GetCurrentSession()
    {
        lock (_gate)
        {
            var nativeSession = GetInitializedManager().GetCurrentSession();
            return nativeSession is null ? null : GetOrCreateSession(nativeSession);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_manager is not null)
        {
                _manager.SessionsChanged -= HandleSessionsChanged;
                _manager.CurrentSessionChanged -= HandleCurrentSessionChanged;
            }

            foreach (var session in _sessions.Values)
        {
                session.Dispose();
            }

            _sessions.Clear();
            _manager = null;
            _disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private GlobalSystemMediaTransportControlsSessionManager GetInitializedManager()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _manager ?? throw new InvalidOperationException("The media session source has not been initialized.");
    }

    private WindowsGlobalMediaSession GetOrCreateSession(GlobalSystemMediaTransportControlsSession nativeSession)
    {
        if (_sessions.TryGetValue(nativeSession, out var wrappedSession))
        {
            return wrappedSession;
        }

        wrappedSession = new WindowsGlobalMediaSession(nativeSession);
        _sessions.Add(nativeSession, wrappedSession);
        return wrappedSession;
    }

    private void HandleSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }
}
