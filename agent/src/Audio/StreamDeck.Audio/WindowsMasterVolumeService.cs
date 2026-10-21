namespace StreamDeck.Audio;

public sealed class WindowsMasterVolumeService : IDisposable
{
    private readonly INativeAudioEndpoint _endpoint;
    private bool _disposed;
    private long revision;
    private readonly object publishGate = new();

    public WindowsMasterVolumeService(INativeAudioEndpoint endpoint)
    {
        _endpoint = endpoint;
        _endpoint.Changed += HandleEndpointChanged;
    }

    public event EventHandler<AudioSnapshot>? AudioChanged;

    public AudioSnapshot? CurrentSnapshot { get; private set; }

    public bool Refresh()
    {
        ThrowIfDisposed();

        try
        {
            var snapshot = ReadSnapshot();
            Publish(snapshot);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool SetVolume(int volumePercent)
    {
        ThrowIfDisposed();

        try
        {
            _endpoint.SetVolumeScalar(AudioScalarMapper.ToScalar(volumePercent));
            Refresh();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool SetMute(bool isMuted)
    {
        ThrowIfDisposed();

        try
        {
            _endpoint.SetMute(isMuted);
            Refresh();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool ToggleMute()
    {
        ThrowIfDisposed();

        try
        {
            _endpoint.SetMute(!_endpoint.GetMute());
            Refresh();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _endpoint.Changed -= HandleEndpointChanged;
        _endpoint.Dispose();
        _disposed = true;
    }

    private AudioSnapshot ReadSnapshot()
    {
        return new AudioSnapshot(
            AudioScalarMapper.ToPercent(_endpoint.GetVolumeScalar()),
            _endpoint.GetMute());
    }

    private void HandleEndpointChanged(object? sender, NativeAudioEndpointChangedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        Publish(new AudioSnapshot(AudioScalarMapper.ToPercent(args.VolumeScalar), args.IsMuted));
    }

    private void Publish(AudioSnapshot snapshot)
    {
        lock (publishGate) {
            snapshot = snapshot with { Revision = ++revision };
            CurrentSnapshot = snapshot;
            AudioChanged?.Invoke(this, snapshot);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
