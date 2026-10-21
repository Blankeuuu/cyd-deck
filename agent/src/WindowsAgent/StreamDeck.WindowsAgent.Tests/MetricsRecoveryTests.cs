using System.Diagnostics;
using System.Reflection;
using StreamDeck.Configuration;
using StreamDeck.Host;
using StreamDeck.Protocol;
using StreamDeck.SystemMonitoring;
using StreamDeck.WindowsAgent;
using StreamDeck.WindowsAgent.Runtime;

namespace StreamDeck.WindowsAgent.Tests;

internal static class MetricsRecoveryTests
{
    public static async Task RunAsync()
    {
        await TestSensorFaultRecoveryContinuesSamplingAsync();
        await TestPersistentSensorFaultDoesNotCrashPublisherAsync();
        await TestPublisherCancellationTerminatesCleanlyAsync();
        await TestPublishFailureDoesNotTerminateLoopAsync();
        await TestInitialHydrationSendsStatsWhenMetricsAvailableAsync();
        await TestInitialHydrationGracefulWhenNoMetricsAsync();
        Console.WriteLine("PASS metrics recovery and truthful stats logging regression");
    }

    private static SystemMetrics CreateValidMetrics(double cpu = 42.0)
    {
        return new SystemMetrics
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            CpuUsagePercent = cpu,
            RamTotalBytes = 16_000_000_000,
            RamUsedBytes = 8_000_000_000,
            Uptime = TimeSpan.FromHours(5),
            GpuUsagePercent = 25.0,
            CpuTemperatureCelsius = 55.0
        };
    }

    private sealed class FlakyMetricsProvider(int failAttempts, SystemMetrics healthyMetrics) : ISystemMetricsProvider
    {
        private int callCount;

        public int CallCount => Volatile.Read(ref callCount);

        public SystemMetrics GetSnapshot()
        {
            var attempt = Interlocked.Increment(ref callCount);
            if (attempt <= failAttempts)
            {
                throw new InvalidOperationException($"Simulated sensor failure attempt {attempt}");
            }

            return healthyMetrics;
        }
    }

    private sealed class AlwaysFailingMetricsProvider : ISystemMetricsProvider
    {
        public SystemMetrics GetSnapshot() => throw new InvalidOperationException("Permanent sensor failure");
    }

    private sealed class TrackingSink : IProtocolSink
    {
        public List<string> SentMessages { get; } = [];

        public ValueTask SendTextAsync(string message, CancellationToken cancellationToken = default)
        {
            lock (SentMessages)
            {
                SentMessages.Add(message);
            }
            return ValueTask.CompletedTask;
        }
    }

    private static async Task TestSensorFaultRecoveryContinuesSamplingAsync()
    {
        var healthy = CreateValidMetrics(33.0);
        var provider = new FlakyMetricsProvider(failAttempts: 2, healthy);
        var sampled = new List<SystemMetrics>();
        var published = new List<SystemMetrics>();
        var errors = new List<Exception>();
        var onTickCount = 0;

        using var cts = new CancellationTokenSource();
        var task = SystemMetricsPublisher.RunAsync(
            provider,
            interval: TimeSpan.FromMilliseconds(20),
            sampled: m => { lock (sampled) sampled.Add(m); },
            publish: (m, _) => { lock (published) published.Add(m); return Task.CompletedTask; },
            failed: e => { lock (errors) errors.Add(e); },
            cts.Token,
            onTick: () => Interlocked.Increment(ref onTickCount));

        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(3))
        {
            lock (published)
            {
                if (published.Count >= 2 && errors.Count >= 2) break;
            }
            await Task.Delay(20);
        }

        await cts.CancelAsync();
        try { await task; } catch (OperationCanceledException) { }

        if (errors.Count < 2)
            throw new InvalidOperationException($"Expected at least 2 errors from initial failed samples, got {errors.Count}");
        if (published.Count < 2)
            throw new InvalidOperationException($"Expected successful recovery samples after sensor recovered, got {published.Count}");
        if (onTickCount < 4)
            throw new InvalidOperationException($"Expected onTick to continue firing across failures, got {onTickCount}");
    }

    private static async Task TestPersistentSensorFaultDoesNotCrashPublisherAsync()
    {
        var provider = new AlwaysFailingMetricsProvider();
        var errors = new List<Exception>();
        var onTickCount = 0;

        using var cts = new CancellationTokenSource();
        var task = SystemMetricsPublisher.RunAsync(
            provider,
            interval: TimeSpan.FromMilliseconds(20),
            sampled: _ => { },
            publish: (_, _) => Task.CompletedTask,
            failed: e => { lock (errors) errors.Add(e); },
            cts.Token,
            onTick: () => Interlocked.Increment(ref onTickCount));

        await Task.Delay(150);
        if (task.IsFaulted)
            throw new InvalidOperationException("Publisher task must not fault when provider throws continuously");

        await cts.CancelAsync();
        try { await task; } catch (OperationCanceledException) { }

        if (errors.Count < 3)
            throw new InvalidOperationException($"Expected multiple recorded errors without task death, got {errors.Count}");
        if (onTickCount < 3)
            throw new InvalidOperationException($"Expected onTick to keep ticking despite sensor fault, got {onTickCount}");
    }

    private static async Task TestPublisherCancellationTerminatesCleanlyAsync()
    {
        var provider = new FlakyMetricsProvider(0, CreateValidMetrics());
        using var cts = new CancellationTokenSource();
        var task = SystemMetricsPublisher.RunAsync(
            provider,
            interval: TimeSpan.FromMilliseconds(50),
            sampled: _ => { },
            publish: (_, _) => Task.CompletedTask,
            failed: _ => { },
            cts.Token);

        await Task.Delay(50);
        await cts.CancelAsync();

        var completed = await Task.WhenAny(task, Task.Delay(1000));
        if (completed != task)
            throw new InvalidOperationException("SystemMetricsPublisher did not terminate promptly upon cancellation");
    }

    private static async Task TestPublishFailureDoesNotTerminateLoopAsync()
    {
        var provider = new FlakyMetricsProvider(0, CreateValidMetrics());
        var publishAttempts = 0;
        var errors = new List<Exception>();

        using var cts = new CancellationTokenSource();
        var task = SystemMetricsPublisher.RunAsync(
            provider,
            interval: TimeSpan.FromMilliseconds(20),
            sampled: _ => { },
            publish: (_, _) =>
            {
                var count = Interlocked.Increment(ref publishAttempts);
                if (count == 1) throw new System.IO.IOException("Simulated USB transport disconnect");
                return Task.CompletedTask;
            },
            failed: e => { lock (errors) errors.Add(e); },
            cts.Token);

        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(2))
        {
            if (Volatile.Read(ref publishAttempts) >= 3) break;
            await Task.Delay(20);
        }

        await cts.CancelAsync();
        try { await task; } catch (OperationCanceledException) { }

        if (publishAttempts < 3)
            throw new InvalidOperationException($"Expected publish loop to continue after transport failure, attempts={publishAttempts}");
        if (errors.Count < 1)
            throw new InvalidOperationException("Expected transport error to be recorded");
    }

    private static async Task TestInitialHydrationSendsStatsWhenMetricsAvailableAsync()
    {
        var messages = await InvokeRuntimeHydrationAsync(CreateValidMetrics(50.0));
        var frame = messages.Single(m => ProtocolSerializer.Parse(m).Envelope?.Type == ProtocolConstants.MessageTypes.SystemStats);
        var parse = ProtocolSerializer.Parse(frame);
        var stats = ProtocolSerializer.DeserializeData<SystemStatsMessage>(parse.Envelope!);
        if (stats.HostLocalMinutes is null || stats.HostDate is null)
            throw new InvalidOperationException("Hydrated system.stats must contain host time");
        if (stats.CpuPercent != 50)
            throw new InvalidOperationException("Hydration must use the actual sampled CPU value");
        if (messages.IndexOf(frame) >= messages.FindIndex(m => ProtocolSerializer.Parse(m).Envelope?.Type == ProtocolConstants.MessageTypes.MediaUpdate))
        {
            throw new InvalidOperationException("Clock/PC hydration must precede media/artwork");
        }
    }

    private static async Task TestInitialHydrationGracefulWhenNoMetricsAsync()
    {
        var messages = await InvokeRuntimeHydrationAsync(null);
        if (messages.Any(m => ProtocolSerializer.Parse(m).Envelope?.Type == ProtocolConstants.MessageTypes.SystemStats))
        {
            throw new InvalidOperationException("Must not emit synthetic metrics when real metrics are unavailable");
        }
    }

    private static async Task<List<string>> InvokeRuntimeHydrationAsync(SystemMetrics? metrics)
    {
        var fixtureDirectory = Path.Combine(Path.GetTempPath(), "cyd-metrics-hydration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureDirectory);
        try
        {
            await using var runtime = new WindowsAgentRuntime(new AgentState(),
                new PairingApprovalService(Path.Combine(fixtureDirectory, "pairing.json")),
                new AgentOptions(MockMode: true),
                configurationStore: new AgentConfigurationStore(Path.Combine(fixtureDirectory, "config.json")));
            typeof(WindowsAgentRuntime).GetField("lastSampledMetrics", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(runtime, metrics);
            var sink = new TrackingSink();
            var operation = (ValueTask)typeof(WindowsAgentRuntime).GetMethod("HydrateInitialDeviceStateAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(runtime, [sink, CancellationToken.None])!;
            await operation;
            return sink.SentMessages;
        }
        finally { Directory.Delete(fixtureDirectory, recursive: true); }
    }
}
