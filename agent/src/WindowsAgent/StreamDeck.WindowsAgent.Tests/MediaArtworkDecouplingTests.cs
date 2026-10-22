using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;
using StreamDeck.Configuration;
using StreamDeck.Covers;
using StreamDeck.Host;
using StreamDeck.Media;
using StreamDeck.Protocol;
using StreamDeck.WindowsAgent;
using StreamDeck.WindowsAgent.Runtime;

internal static class MediaArtworkDecouplingTests
{
    public static async Task RunAsync()
    {
        Console.WriteLine("Running MediaArtworkDecouplingTests (Artwork Decoupling Regression)...");
        await TestBlockedThumbnailDoesNotDelayPlaybackOrTimelineUpdateAsync();
        await TestTrackChangeWithSlowArtworkRejectsStaleCoverAsync();
        await TestSameTrackNewThumbnailDecoupledAsync();
        await TestSameTrackNewThumbnailReferenceNeverPublishesNullCoverAsync();
        await TestSameTrackTransientNullThumbnailKeepsCoverIdAsync();
        await TestReturnToPreviousTrackUsesCoverCacheWithoutOpenReadAsync();
        Console.WriteLine("PASS: All MediaArtworkDecouplingTests passed successfully.");
    }

    public static async Task TestBlockedThumbnailDoesNotDelayPlaybackOrTimelineUpdateAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"streamdeck-decoupling-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var (runtime, sink) = CreateIsolatedRuntime(tempDir);
            await using var _ = runtime;

            var tcsA = new TaskCompletionSource<Stream?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thumbA = new BlockingThumbnailReference(() => tcsA.Task);

            var snapshotPlaying = new MediaSnapshot(
                "Spotify.exe", "Track 1", "Artist 1", "Album 1",
                MediaPlaybackState.Playing,
                new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromMinutes(3)),
                thumbA);

            // 1. Initial play update must return immediately even when thumbnail read is blocked
            var initialPublishTask = InvokePublishMediaUpdateAsync(runtime, snapshotPlaying, sink);
            var completedTask = await Task.WhenAny(initialPublishTask, Task.Delay(500));
            if (completedTask != initialPublishTask)
            {
                throw new TimeoutException("PublishMediaUpdateAsync blocked waiting for thumbnail OpenReadAsync! (RED: gate held during artwork)");
            }
            await initialPublishTask;

            // Verify metadata was published immediately with coverId == null
            var initialMessage = sink.Messages.LastOrDefault();
            if (initialMessage is null)
            {
                throw new InvalidOperationException("Expected immediate media update frame in sink, but got none.");
            }
            var initialJson = JsonDocument.Parse(initialMessage);
            var initialType = initialJson.RootElement.GetProperty("type").GetString();
            if (initialType != ProtocolConstants.MessageTypes.MediaUpdate)
            {
                throw new InvalidOperationException($"Expected media.update message, got: {initialType}");
            }
            var initialCoverId = initialJson.RootElement.GetProperty("data").TryGetProperty("coverId", out var cid) ? cid.GetString() : null;
            if (initialCoverId is not null)
            {
                throw new InvalidOperationException($"Expected coverId null on immediate publish, got {initialCoverId}");
            }
            var initialPlaying = initialJson.RootElement.GetProperty("data").GetProperty("playing").GetBoolean();
            if (!initialPlaying)
            {
                throw new InvalidOperationException("Expected playing=true on immediate publish.");
            }

            // 2. Pause update while thumbnail is STILL blocked must also return immediately
            var snapshotPaused = new MediaSnapshot(
                "Spotify.exe", "Track 1", "Artist 1", "Album 1",
                MediaPlaybackState.Paused,
                new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(15), TimeSpan.Zero, TimeSpan.FromMinutes(3)),
                thumbA);

            var pausePublishTask = InvokePublishMediaUpdateAsync(runtime, snapshotPaused, sink);
            var pauseCompleted = await Task.WhenAny(pausePublishTask, Task.Delay(500));
            if (pauseCompleted != pausePublishTask)
            {
                throw new TimeoutException("Pause update blocked waiting for previous thumbnail or publish gate! (RED)");
            }
            await pausePublishTask;

            var pauseMessage = sink.Messages.LastOrDefault();
            var pauseJson = JsonDocument.Parse(pauseMessage!);
            var pausePlaying = pauseJson.RootElement.GetProperty("data").GetProperty("playing").GetBoolean();
            if (pausePlaying)
            {
                throw new InvalidOperationException("Expected playing=false in pause media update.");
            }

            // 3. Unblock the thumbnail stream
            var bytes = CreateSampleImageBytes(Color.Magenta);
            tcsA.SetResult(new MemoryStream(bytes));

            // Wait for artwork processing to complete and republish latest state
            var coverPublished = await WaitForMessageAsync(sink, msg =>
            {
                using var doc = JsonDocument.Parse(msg);
                if (doc.RootElement.TryGetProperty("type", out var type) &&
                    type.GetString() == ProtocolConstants.MessageTypes.MediaUpdate &&
                    doc.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("coverId", out var c) &&
                    !string.IsNullOrWhiteSpace(c.GetString()))
                {
                    // Ensure the republished cover message carries the latest playback state (Paused, not Playing)
                    var isPlaying = data.GetProperty("playing").GetBoolean();
                    return !isPlaying;
                }
                return false;
            }, timeoutMs: 3000);

            if (!coverPublished)
            {
                throw new TimeoutException("Expected eventual media.update with populated coverId and playing=false.");
            }

            Console.WriteLine("  PASS: TestBlockedThumbnailDoesNotDelayPlaybackOrTimelineUpdate");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    public static async Task TestTrackChangeWithSlowArtworkRejectsStaleCoverAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"streamdeck-decoupling-stale-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var (runtime, sink) = CreateIsolatedRuntime(tempDir);
            await using var _ = runtime;

            var tcsA = new TaskCompletionSource<Stream?>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Ignore cancellation token to simulate WinRT OpenReadAsync ignoring token
            var thumbA = new BlockingThumbnailReference(() => tcsA.Task, ignoreCancellation: true);

            var snapshotA = new MediaSnapshot(
                "Spotify.exe", "Song A", "Artist A", "Album A",
                MediaPlaybackState.Playing,
                new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMinutes(3)),
                thumbA);

            // Publish Track A (thumbnail blocked)
            var pubATask = InvokePublishMediaUpdateAsync(runtime, snapshotA, sink);
            await Task.WhenAny(pubATask, Task.Delay(500));
            if (!pubATask.IsCompleted)
            {
                throw new TimeoutException("Publish Track A blocked! (RED)");
            }

            // Switch to Track B immediately
            var bytesB = CreateSampleImageBytes(Color.Cyan);
            var thumbB = new BlockingThumbnailReference(() => Task.FromResult<Stream?>(new MemoryStream(bytesB)));

            var snapshotB = new MediaSnapshot(
                "Spotify.exe", "Song B", "Artist B", "Album B",
                MediaPlaybackState.Playing,
                new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMinutes(3)),
                thumbB);

            var pubBTask = InvokePublishMediaUpdateAsync(runtime, snapshotB, sink);
            await Task.WhenAny(pubBTask, Task.Delay(500));
            if (!pubBTask.IsCompleted)
            {
                throw new TimeoutException("Publish Track B blocked! (RED)");
            }

            // Now release stale thumbnail A
            var bytesA = CreateSampleImageBytes(Color.Yellow);
            tcsA.SetResult(new MemoryStream(bytesA));

            // Wait for Track B artwork to land
            var trackBCoverPublished = await WaitForMessageAsync(sink, msg =>
            {
                using var doc = JsonDocument.Parse(msg);
                if (doc.RootElement.TryGetProperty("type", out var type) &&
                    type.GetString() == ProtocolConstants.MessageTypes.MediaUpdate &&
                    doc.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("title", out var title) &&
                    title.GetString() == "Song B" &&
                    data.TryGetProperty("coverId", out var c) &&
                    !string.IsNullOrWhiteSpace(c.GetString()))
                {
                    return true;
                }
                return false;
            }, timeoutMs: 3000);

            if (!trackBCoverPublished)
            {
                throw new TimeoutException("Track B artwork was not published.");
            }

            // Verify Song A cover was NEVER published after Song B was active
            foreach (var message in sink.Messages)
            {
                using var doc = JsonDocument.Parse(message);
                if (doc.RootElement.TryGetProperty("type", out var type) &&
                    type.GetString() == ProtocolConstants.MessageTypes.MediaUpdate &&
                    doc.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("title", out var title) &&
                    title.GetString() == "Song B" &&
                    data.TryGetProperty("coverId", out var cid) &&
                    cid.GetString() is not null)
                {
                    // Calculate sha256 of bytesA to verify it is NOT bytesA
                    using var sha = System.Security.Cryptography.SHA256.Create();
                    var shaA = BitConverter.ToString(sha.ComputeHash(bytesA)).Replace("-", "").ToLowerInvariant();
                    if (cid.GetString() == shaA)
                    {
                        throw new InvalidOperationException("Stale cover from Song A was published for Song B! (RED)");
                    }
                }
            }

            Console.WriteLine("  PASS: TestTrackChangeWithSlowArtworkRejectsStaleCover");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    public static async Task TestSameTrackNewThumbnailDecoupledAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"streamdeck-decoupling-same-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var (runtime, sink) = CreateIsolatedRuntime(tempDir);
            await using var _ = runtime;

            var bytesA = CreateSampleImageBytes(Color.Red);
            var bytesB = CreateSampleImageBytes(Color.Blue);

            var thumbA = new BlockingThumbnailReference(() => Task.FromResult<Stream?>(new MemoryStream(bytesA)));
            var thumbB = new BlockingThumbnailReference(() => Task.FromResult<Stream?>(new MemoryStream(bytesB)));

            var snapshotA = new MediaSnapshot(
                "Spotify.exe", "Same Track", "Artist", "Album",
                MediaPlaybackState.Playing,
                new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.FromMinutes(3)),
                thumbA);

            var pubTask = InvokePublishMediaUpdateAsync(runtime, snapshotA, sink);
            await pubTask;

            // Wait for cover A
            var hasCoverA = await WaitForMessageAsync(sink, msg =>
            {
                using var doc = JsonDocument.Parse(msg);
                return doc.RootElement.TryGetProperty("data", out var data) &&
                       data.TryGetProperty("coverId", out var cid) &&
                       !string.IsNullOrWhiteSpace(cid.GetString());
            }, timeoutMs: 3000);

            if (!hasCoverA) throw new TimeoutException("Cover A was not published.");

            // Now same track but new thumbnail instance B
            var snapshotB = new MediaSnapshot(
                "Spotify.exe", "Same Track", "Artist", "Album",
                MediaPlaybackState.Playing,
                new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(2), TimeSpan.Zero, TimeSpan.FromMinutes(3)),
                thumbB);

            await InvokePublishMediaUpdateAsync(runtime, snapshotB, sink);

            string? lastCoverId = null;
            var hasCoverB = await WaitForMessageAsync(sink, msg =>
            {
                using var doc = JsonDocument.Parse(msg);
                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("coverId", out var cid) &&
                    !string.IsNullOrWhiteSpace(cid.GetString()))
                {
                    lastCoverId = cid.GetString();
                    return true;
                }
                return false;
            }, timeoutMs: 3000);

            if (!hasCoverB || string.IsNullOrWhiteSpace(lastCoverId))
            {
                throw new TimeoutException("Cover B was not published for same track new thumbnail.");
            }

            Console.WriteLine("  PASS: TestSameTrackNewThumbnailDecoupled");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static MediaSnapshot MakeSnapshot(string title, MediaPlaybackState state, IMediaThumbnailReference? thumb, int seconds = 1) =>
        new("Spotify.exe", title, "Artist", "Album", state,
            new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(seconds), TimeSpan.Zero, TimeSpan.FromMinutes(3)),
            thumb);

    private static string? LastCoverId(MemoryProtocolSink sink)
    {
        var last = sink.Messages.ToArray().LastOrDefault();
        if (last is null) throw new InvalidOperationException("No message in sink.");
        using var doc = JsonDocument.Parse(last);
        return doc.RootElement.GetProperty("data").TryGetProperty("coverId", out var c) ? c.GetString() : null;
    }

    private static async Task<string> WaitForCoverIdAsync(MemoryProtocolSink sink)
    {
        string? id = null;
        var ok = await WaitForMessageAsync(sink, msg =>
        {
            using var doc = JsonDocument.Parse(msg);
            if (doc.RootElement.TryGetProperty("data", out var d) && d.TryGetProperty("coverId", out var c) && !string.IsNullOrWhiteSpace(c.GetString()))
            {
                id = c.GetString();
                return true;
            }
            return false;
        }, timeoutMs: 3000);
        if (!ok || id is null) throw new TimeoutException("Cover was not published.");
        return id;
    }

    public static async Task TestSameTrackNewThumbnailReferenceNeverPublishesNullCoverAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"streamdeck-decoupling-ref-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var (runtime, sink) = CreateIsolatedRuntime(tempDir);
            await using var _ = runtime;
            var bytes = CreateSampleImageBytes(Color.Red);
            IMediaThumbnailReference NewRef() => new BlockingThumbnailReference(() => Task.FromResult<Stream?>(new MemoryStream(bytes)));

            await InvokePublishMediaUpdateAsync(runtime, MakeSnapshot("Same", MediaPlaybackState.Playing, NewRef()), sink);
            var coverId = await WaitForCoverIdAsync(sink);
            var before = sink.Messages.Count();

            for (var i = 0; i < 5; i++)
            {
                await InvokePublishMediaUpdateAsync(runtime, MakeSnapshot("Same", i % 2 == 0 ? MediaPlaybackState.Paused : MediaPlaybackState.Playing, NewRef(), 2 + i), sink);
                if (LastCoverId(sink) != coverId)
                    throw new InvalidOperationException("Nowa referencja miniatury dla tego samego utworu wyczyściła coverId (RED).");
            }
            await Task.Delay(400);
            foreach (var msg in sink.Messages.ToArray().Skip(before))
            {
                using var doc = JsonDocument.Parse(msg);
                if (doc.RootElement.GetProperty("type").GetString() == ProtocolConstants.MessageTypes.MediaUpdate &&
                    (!doc.RootElement.GetProperty("data").TryGetProperty("coverId", out var c) || c.GetString() != coverId))
                    throw new InvalidOperationException("media.update z coverId != znany dla tego samego utworu.");
            }
            Console.WriteLine("  PASS: TestSameTrackNewThumbnailReferenceNeverPublishesNullCover");
        }
        finally { try { Directory.Delete(tempDir, recursive: true); } catch { } }
    }

    public static async Task TestSameTrackTransientNullThumbnailKeepsCoverIdAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"streamdeck-decoupling-null-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var (runtime, sink) = CreateIsolatedRuntime(tempDir);
            await using var _ = runtime;
            var bytes = CreateSampleImageBytes(Color.Green);
            await InvokePublishMediaUpdateAsync(runtime, MakeSnapshot("Same", MediaPlaybackState.Playing,
                new BlockingThumbnailReference(() => Task.FromResult<Stream?>(new MemoryStream(bytes)))), sink);
            var coverId = await WaitForCoverIdAsync(sink);

            await InvokePublishMediaUpdateAsync(runtime, MakeSnapshot("Same", MediaPlaybackState.Paused, null, 3), sink);
            if (LastCoverId(sink) != coverId)
                throw new InvalidOperationException("Chwilowo null Thumbnail wyczyścił coverId (RED).");

            var failing = new BlockingThumbnailReference(() => throw new IOException("OpenRead failed"));
            await InvokePublishMediaUpdateAsync(runtime, MakeSnapshot("Same", MediaPlaybackState.Playing, failing, 4), sink);
            await Task.Delay(800);
            await InvokePublishMediaUpdateAsync(runtime, MakeSnapshot("Same", MediaPlaybackState.Playing, null, 5), sink);
            if (LastCoverId(sink) != coverId)
                throw new InvalidOperationException("Porażka OpenRead wyczyściła coverId.");

            // Realna zmiana utworu bez okładki => null.
            await InvokePublishMediaUpdateAsync(runtime, MakeSnapshot("Other", MediaPlaybackState.Playing, null), sink);
            if (LastCoverId(sink) is not null)
                throw new InvalidOperationException("Stary coverId przeżył zmianę utworu.");
            Console.WriteLine("  PASS: TestSameTrackTransientNullThumbnailKeepsCoverId");
        }
        finally { try { Directory.Delete(tempDir, recursive: true); } catch { } }
    }

    public static async Task TestReturnToPreviousTrackUsesCoverCacheWithoutOpenReadAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"streamdeck-decoupling-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var (runtime, sink) = CreateIsolatedRuntime(tempDir);
            await using var _ = runtime;
            var bytesA = CreateSampleImageBytes(Color.Red);
            var bytesB = CreateSampleImageBytes(Color.Blue);
            await InvokePublishMediaUpdateAsync(runtime, MakeSnapshot("A", MediaPlaybackState.Playing,
                new BlockingThumbnailReference(() => Task.FromResult<Stream?>(new MemoryStream(bytesA)))), sink);
            var idA = await WaitForCoverIdAsync(sink);
            await runtime.WaitForArtworkIdleAsync();

            await InvokePublishMediaUpdateAsync(runtime, MakeSnapshot("B", MediaPlaybackState.Playing,
                new BlockingThumbnailReference(() => Task.FromResult<Stream?>(new MemoryStream(bytesB)))), sink);
            await Task.Delay(100);
            await runtime.WaitForArtworkIdleAsync();
            var idB = LastCoverId(sink);
            if (idB == idA) throw new InvalidOperationException("Cover B == cover A.");

            var opens = 0;
            await InvokePublishMediaUpdateAsync(runtime, MakeSnapshot("A", MediaPlaybackState.Playing,
                new BlockingThumbnailReference(() => { Interlocked.Increment(ref opens); return Task.FromResult<Stream?>(new MemoryStream(bytesA)); })), sink);
            if (LastCoverId(sink) != idA)
                throw new InvalidOperationException("Powrót do utworu A nie użył cache coverId (RED).");
            if (Volatile.Read(ref opens) != 0)
                throw new InvalidOperationException("Powrót do utworu A wywołał OpenRead mimo cache.");
            Console.WriteLine("  PASS: TestReturnToPreviousTrackUsesCoverCacheWithoutOpenRead");
        }
        finally { try { Directory.Delete(tempDir, recursive: true); } catch { } }
    }
    private static (WindowsAgentRuntime Runtime, MemoryProtocolSink Sink) CreateIsolatedRuntime(string tempDir)
    {
        var configPath = Path.Combine(tempDir, "config.json");
        var pairingPath = Path.Combine(tempDir, "pairing.json");
        var coversDir = Path.Combine(tempDir, "covers");

        var store = new AgentConfigurationStore(configPath);
        store.Save(AgentConfiguration.CreateDefault());

        var state = new AgentState();
        var pairing = new PairingApprovalService(pairingPath);
        var options = new AgentOptions(MockMode: true);

        var runtime = new WindowsAgentRuntime(
            state,
            pairing,
            options,
            initialAudioProvider: null,
            listenPortOverride: 0,
            configurationStore: store);

        var processor = new CoverProcessor(new CoverProcessingOptions
        {
            CacheDirectory = coversDir,
            CoverSizePixels = 128,
            JpegQuality = 80
        });

        var processorField = typeof(WindowsAgentRuntime).GetField("coverProcessor", BindingFlags.Instance | BindingFlags.NonPublic)!;
        processorField.SetValue(runtime, processor);

        var sink = new MemoryProtocolSink();
        return (runtime, sink);
    }

    private static Task InvokePublishMediaUpdateAsync(WindowsAgentRuntime runtime, MediaSnapshot? snapshot, IProtocolSink sink)
    {
        var method = typeof(WindowsAgentRuntime).GetMethod(
            "PublishMediaUpdateAsync",
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(MediaSnapshot), typeof(CancellationToken), typeof(IProtocolSink)])!;

        return (Task)method.Invoke(runtime, [snapshot, CancellationToken.None, sink])!;
    }

    private static async Task<bool> WaitForMessageAsync(MemoryProtocolSink sink, Func<string, bool> predicate, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            var messages = sink.Messages.ToArray();
            for (var i = messages.Length - 1; i >= 0; i--)
            {
                if (predicate(messages[i])) return true;
            }
            await Task.Delay(25);
        }
        return false;
    }

    private static byte[] CreateSampleImageBytes(Color color)
    {
        using var bmp = new Bitmap(128, 128);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(color);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private sealed class BlockingThumbnailReference(Func<Task<Stream?>> streamProvider, bool ignoreCancellation = false) : IMediaThumbnailReference
    {
        public async ValueTask<Stream?> OpenReadAsync(CancellationToken cancellationToken = default)
        {
            if (ignoreCancellation)
            {
                return await streamProvider().ConfigureAwait(false);
            }
            using var reg = cancellationToken.Register(() => { });
            return await streamProvider().ConfigureAwait(false);
        }
    }
}
