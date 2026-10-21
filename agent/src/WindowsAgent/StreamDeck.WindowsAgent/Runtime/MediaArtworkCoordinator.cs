using System.Threading.Channels;
using StreamDeck.Covers;
using StreamDeck.Host;
using StreamDeck.Media;

namespace StreamDeck.WindowsAgent.Runtime;

internal sealed class MediaArtworkCoordinator : IAsyncDisposable
{
    private readonly Func<CoverProcessor?> _processorProvider;
    private readonly Func<long, MediaSnapshot?, string, string?, IProtocolSink?, Task> _onCoverReady;
    private readonly Action<string>? _log;
    private readonly Channel<ArtworkWorkItem> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _workerTask;
    private readonly object _lock = new();

    private long _currentGeneration;
    private string? _currentTrackKey;
    private IMediaThumbnailReference? _currentThumbnailRef;
    private string? _cachedCoverId;
    private string? _cachedAccent;
    private bool _isFailed;
    private bool _isQueuedOrProcessing;
    private MediaSnapshot? _latestSnapshot;
    private IProtocolSink? _lastSink;
    private CancellationTokenSource? _activeItemCts;
    private TaskCompletionSource<bool>? _idleTcs;

    public MediaArtworkCoordinator(
        Func<CoverProcessor?> processorProvider,
        Func<long, MediaSnapshot?, string, string?, IProtocolSink?, Task> onCoverReady,
        Action<string>? log = null)
    {
        _processorProvider = processorProvider;
        _onCoverReady = onCoverReady;
        _log = log;

        var channelOptions = new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<ArtworkWorkItem>(channelOptions);
        _workerTask = Task.Run(() => RunWorkerLoopAsync(_cts.Token), CancellationToken.None);
    }

    public string? CachedCoverId
    {
        get { lock (_lock) return _cachedCoverId; }
    }

    public string? CachedAccent
    {
        get { lock (_lock) return _cachedAccent; }
    }

    public MediaSnapshot? LatestSnapshot
    {
        get { lock (_lock) return _latestSnapshot; }
    }

    public IProtocolSink? LastSink
    {
        get { lock (_lock) return _lastSink; }
    }

    public bool IsCurrentGeneration(long generation)
    {
        lock (_lock) return generation == _currentGeneration;
    }

    public (string? CoverId, string? AccentHex, bool HasImmediateCover) Update(MediaSnapshot? snapshot, IProtocolSink? target)
    {
        lock (_lock)
        {
            if (target is not null)
            {
                _lastSink = target;
            }
            _latestSnapshot = snapshot;

            var trackKey = $"{snapshot?.SourceAppUserModelId}|{snapshot?.Title}|{snapshot?.Artist}|{snapshot?.AlbumTitle}";
            var thumbnailChanged = !ReferenceEquals(snapshot?.Thumbnail, _currentThumbnailRef);

            if (trackKey != _currentTrackKey || thumbnailChanged)
            {
                _currentGeneration++;
                _currentTrackKey = trackKey;
                _currentThumbnailRef = snapshot?.Thumbnail;
                _cachedCoverId = null;
                _cachedAccent = null;
                _isFailed = false;
                _isQueuedOrProcessing = false;

                try { _activeItemCts?.Cancel(); } catch { }
            }

            if (snapshot?.Thumbnail is null)
            {
                return (null, null, true);
            }

            if (_cachedCoverId is not null)
            {
                return (_cachedCoverId, _cachedAccent, true);
            }

            if (_isFailed)
            {
                return (null, null, true);
            }

            var processor = _processorProvider();
            if (processor is null)
            {
                return (null, null, true);
            }

            if (!_isQueuedOrProcessing)
            {
                _isQueuedOrProcessing = true;
                var item = new ArtworkWorkItem(_currentGeneration, snapshot.Thumbnail, processor);
                _channel.Writer.TryWrite(item);
            }

            return (null, null, false);
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _currentGeneration++;
            _currentTrackKey = null;
            _currentThumbnailRef = null;
            _cachedCoverId = null;
            _cachedAccent = null;
            _isFailed = false;
            _isQueuedOrProcessing = false;
            _latestSnapshot = null;
            _lastSink = null;
            try { _activeItemCts?.Cancel(); } catch { }
        }
    }

    public async Task WaitForArtworkIdleAsync(TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);
        TaskCompletionSource<bool> tcs;
        lock (_lock)
        {
            if (!_isQueuedOrProcessing && _activeItemCts is null)
            {
                return;
            }
            tcs = _idleTcs ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        var timeoutTask = Task.Delay(effectiveTimeout);
        var completed = await Task.WhenAny(tcs.Task, timeoutTask).ConfigureAwait(false);
        if (completed == timeoutTask)
        {
            throw new TimeoutException($"Timed out waiting for artwork worker to become idle after {effectiveTimeout}.");
        }
    }

    private async Task RunWorkerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ArtworkWorkItem? item;
            try
            {
                if (!await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    break;
                }

                if (!_channel.Reader.TryRead(out item) || item is null)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            lock (_lock)
            {
                if (item.Generation != _currentGeneration)
                {
                    _isQueuedOrProcessing = false;
                    SignalIdleIfDone();
                    continue;
                }
            }

            CoverProcessResult? coverResult = null;
            CancellationTokenSource? itemCts = null;
            try
            {
                itemCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                lock (_lock)
                {
                    _activeItemCts = itemCts;
                }

                coverResult = await TryProcessCoverWithRetryAsync(item.Thumbnail, item.Processor, _log, itemCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Artwork worker error: {ex.GetType().Name}");
            }
            finally
            {
                lock (_lock)
                {
                    if (ReferenceEquals(_activeItemCts, itemCts))
                    {
                        _activeItemCts = null;
                    }
                }
                itemCts?.Dispose();
            }

            bool isCurrent;
            MediaSnapshot? latest;
            IProtocolSink? sink;
            lock (_lock)
            {
                isCurrent = item.Generation == _currentGeneration;
                latest = _latestSnapshot;
                sink = _lastSink;

                if (isCurrent)
                {
                    if (coverResult is not null)
                    {
                        _cachedCoverId = coverResult.CoverId;
                        _cachedAccent = coverResult.AccentHex;
                    }
                    else
                    {
                        _isFailed = true;
                    }
                }
                else
                {
                    _log?.Invoke($"Artwork result for generation {item.Generation} rejected (stale; active is {_currentGeneration}).");
                }
            }

            if (isCurrent && coverResult is not null)
            {
                try
                {
                    await _onCoverReady(item.Generation, latest, coverResult.CoverId, coverResult.AccentHex, sink).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log?.Invoke($"Cover ready callback failed: {ex.GetType().Name}");
                }
            }

            lock (_lock)
            {
                _isQueuedOrProcessing = false;
                SignalIdleIfDone();
            }
        }

        lock (_lock)
        {
            _isQueuedOrProcessing = false;
            SignalIdleIfDone();
        }
    }

    private void SignalIdleIfDone()
    {
        if (!_isQueuedOrProcessing && _activeItemCts is null && _idleTcs is not null)
        {
            var tcs = _idleTcs;
            _idleTcs = null;
            tcs.TrySetResult(true);
        }
    }

    private static async Task<CoverProcessResult?> TryProcessCoverWithRetryAsync(
        IMediaThumbnailReference thumbnail,
        CoverProcessor processor,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await using var thumbnailStream = await thumbnail.OpenReadAsync(cancellationToken).ConfigureAwait(false);
                if (thumbnailStream is null || (thumbnailStream.CanSeek && thumbnailStream.Length == 0))
                {
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(100 * attempt, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    return null;
                }

                var cover = await processor.ProcessAsync(thumbnailStream, cancellationToken).ConfigureAwait(false);
                if (cover is not null)
                {
                    return cover;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (attempt < maxAttempts)
                {
                    log?.Invoke($"Artwork attempt {attempt} failed ({exception.GetType().Name}), retrying in {100 * attempt}ms...");
                    await Task.Delay(100 * attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                log?.Invoke($"Artwork processing failed after {maxAttempts} attempts: {exception.GetType().Name}; publishing track metadata.");
            }
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        try { _activeItemCts?.Cancel(); } catch { }
        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            var completed = await Task.WhenAny(_workerTask, Task.Delay(1000)).ConfigureAwait(false);
            if (completed == _workerTask)
            {
                await _workerTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log?.Invoke($"Artwork coordinator dispose: {ex.GetType().Name}");
        }
        _cts.Dispose();
    }

    private sealed record ArtworkWorkItem(
        long Generation,
        IMediaThumbnailReference Thumbnail,
        CoverProcessor Processor);
}
