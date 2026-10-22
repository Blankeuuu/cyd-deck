using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using StreamDeck.Configuration;
using StreamDeck.Covers;
using StreamDeck.Host;
using StreamDeck.Media;
using StreamDeck.WindowsAgent;
using StreamDeck.WindowsAgent.Runtime;

internal static class RuntimeMediaRegressionTests
{
    public static async Task RunAsync()
    {
        Console.WriteLine("Running RuntimeMediaRegressionTests...");
        await TestSameTrackNewThumbnailReferenceKeepsCoverIdAsync();
        await TestTimelinePollsDoNotTriggerAdditionalImageReadsAsync();
        await TestTransientOpenReadFailureRetriesAndSucceedsAsync();
        Console.WriteLine("PASS: All RuntimeMediaRegressionTests passed successfully.");
    }

    private static async Task TestSameTrackNewThumbnailReferenceKeepsCoverIdAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"streamdeck-runtime-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var (runtime, sink) = CreateIsolatedRuntime(tempDir);
            await using var _ = runtime;

            var bytesA = CreateSampleImageBytes(Color.Red);
            var bytesB = CreateSampleImageBytes(Color.Blue);

            var thumbA = new FakeThumbnailReference(() => new MemoryStream(bytesA));
            var thumbB = new FakeThumbnailReference(() => new MemoryStream(bytesB));

            var snapshotA = new MediaSnapshot(
                "Spotify.exe", "Song Title", "Artist Name", "Album Name",
                MediaPlaybackState.Playing,
                new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromMinutes(3)),
                thumbA);

            await InvokePublishMediaUpdateAsync(runtime, snapshotA, sink);
            await runtime.WaitForArtworkIdleAsync();
            var coverIdA = GetCachedCoverId(runtime);
            if (string.IsNullOrWhiteSpace(coverIdA))
            {
                throw new InvalidOperationException("Expected non-empty cachedCoverId for thumbnail A.");
            }
            if (thumbA.OpenReadCount != 1)
            {
                throw new InvalidOperationException($"Expected 1 OpenReadAsync call on thumbA, got {thumbA.OpenReadCount}.");
            }

            // Same track metadata, but different thumbnail instance
            var snapshotB = new MediaSnapshot(
                "Spotify.exe", "Song Title", "Artist Name", "Album Name",
                MediaPlaybackState.Playing,
                new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(11), TimeSpan.Zero, TimeSpan.FromMinutes(3)),
                thumbB);

            await InvokePublishMediaUpdateAsync(runtime, snapshotB, sink);
            await runtime.WaitForArtworkIdleAsync();
            var coverIdB = GetCachedCoverId(runtime);
            if (string.IsNullOrWhiteSpace(coverIdB))
            {
                throw new InvalidOperationException("Expected non-empty cachedCoverId for thumbnail B.");
            }
            if (thumbB.OpenReadCount != 1)
            {
                throw new InvalidOperationException($"Expected 1 OpenReadAsync call on thumbB, got {thumbB.OpenReadCount}.");
            }

            if (coverIdA == coverIdB)
            {
                throw new InvalidOperationException($"Expected different cover IDs for red vs blue thumbnails, but both were {coverIdA}.");
            }

            Console.WriteLine("  PASS: TestSameTrackNewThumbnailReferenceKeepsCoverId");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static async Task TestTimelinePollsDoNotTriggerAdditionalImageReadsAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"streamdeck-runtime-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var (runtime, sink) = CreateIsolatedRuntime(tempDir);
            await using var _ = runtime;

            var bytes = CreateSampleImageBytes(Color.Green);
            var thumb = new FakeThumbnailReference(() => new MemoryStream(bytes));

            for (var second = 10; second <= 15; second++)
            {
                var snapshot = new MediaSnapshot(
                    "Spotify.exe", "Song Title", "Artist Name", "Album Name",
                    MediaPlaybackState.Playing,
                    new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(second), TimeSpan.Zero, TimeSpan.FromMinutes(3)),
                    thumb);

                await InvokePublishMediaUpdateAsync(runtime, snapshot, sink);
            }
            await runtime.WaitForArtworkIdleAsync();

            if (thumb.OpenReadCount != 1)
            {
                throw new InvalidOperationException($"Expected exactly 1 OpenReadAsync call across timeline ticks, got {thumb.OpenReadCount}.");
            }

            Console.WriteLine("  PASS: TestTimelinePollsDoNotTriggerAdditionalImageReads");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static async Task TestTransientOpenReadFailureRetriesAndSucceedsAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"streamdeck-runtime-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var (runtime, sink) = CreateIsolatedRuntime(tempDir);
            await using var _ = runtime;

            var bytes = CreateSampleImageBytes(Color.Orange);
            var attempts = 0;
            var thumb = new FakeThumbnailReference(() =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new IOException("Simulated transient file lock (sharing violation).");
                }
                return new MemoryStream(bytes);
            });

            var snapshot = new MediaSnapshot(
                "Spotify.exe", "Locked Song", "Artist", "Album",
                MediaPlaybackState.Playing,
                new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(5), TimeSpan.Zero, TimeSpan.FromMinutes(3)),
                thumb);

            await InvokePublishMediaUpdateAsync(runtime, snapshot, sink);
            await runtime.WaitForArtworkIdleAsync();

            if (thumb.OpenReadCount != 2)
            {
                throw new InvalidOperationException($"Expected exactly 2 attempts before success, got {thumb.OpenReadCount}.");
            }

            var coverId = GetCachedCoverId(runtime);
            if (string.IsNullOrWhiteSpace(coverId))
            {
                throw new InvalidOperationException("Expected cachedCoverId to be populated after successful retry.");
            }

            Console.WriteLine("  PASS: TestTransientOpenReadFailureRetriesAndSucceeds");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
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

    private static async Task InvokePublishMediaUpdateAsync(WindowsAgentRuntime runtime, MediaSnapshot? snapshot, IProtocolSink sink)
    {
        var method = typeof(WindowsAgentRuntime).GetMethod(
            "PublishMediaUpdateAsync",
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(MediaSnapshot), typeof(CancellationToken), typeof(IProtocolSink)])!;

        var task = (Task)method.Invoke(runtime, [snapshot, CancellationToken.None, sink])!;
        await task;
    }

    private static string? GetCachedCoverId(WindowsAgentRuntime runtime)
    {
        var field = typeof(WindowsAgentRuntime).GetField("cachedCoverId", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (string?)field.GetValue(runtime);
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

    private sealed class FakeThumbnailReference(Func<Stream?> streamProvider) : IMediaThumbnailReference
    {
        public int OpenReadCount;

        public ValueTask<Stream?> OpenReadAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref OpenReadCount);
            return ValueTask.FromResult(streamProvider());
        }
    }
}
