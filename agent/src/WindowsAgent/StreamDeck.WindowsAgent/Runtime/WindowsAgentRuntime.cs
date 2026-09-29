using System.Net;
using StreamDeck.Audio;
using StreamDeck.Configuration;
using StreamDeck.Covers;
using StreamDeck.Host;
using StreamDeck.Media;
using StreamDeck.Protocol;
using StreamDeck.Shortcuts;
using StreamDeck.SystemMonitoring;

namespace StreamDeck.WindowsAgent.Runtime;

public sealed class WindowsAgentRuntime(
    AgentState state,
    PairingApprovalService pairingService,
    AgentOptions options,
    Func<AudioSnapshot?>? initialAudioProvider = null,
    int? listenPortOverride = null) : IAsyncDisposable
{
    private CancellationTokenSource? cancellation;
    private WindowsAgentWebSocketServer? webSocketServer;
    private MdnsAdvertiser? mdnsAdvertiser;
    private WindowsMasterVolumeService? volumeService;
    private CoreAudioEndpoint? audioEndpoint;
    private WindowsGlobalMediaSessionSource? mediaSource;
    private MediaSessionMonitor? mediaMonitor;
    private CoverProcessor? coverProcessor;
    private WindowsSystemMetricsProvider? systemMetricsProvider;
    private Task? systemMetricsTask;
    private readonly PendingShortcutImportStore pendingShortcutImports = new();
    private int port;
    private int lastHostConnected;

    public int Port => port;

    public async Task StartAsync()
    {
        if (cancellation is not null)
        {
            return;
        }

        var configuration = AgentConfigurationStoreFactory.CreateForCurrentUser().LoadOrDefault();
        port = listenPortOverride ?? configuration.Server?.Port ?? 8765;
        var monitoringInterval = TimeSpan.FromMilliseconds(configuration.Monitoring?.IntervalMs ?? 1000);
        var coverCacheDirectory = GetCoverCacheDirectory();
        coverProcessor = new CoverProcessor(new CoverProcessingOptions { CacheDirectory = coverCacheDirectory });
        cancellation = new CancellationTokenSource();
        state.SetMode(options.MockMode ? "mock" : "live");

        try
        {
            var deviceShortcuts = ShortcutCatalog.Create(new ShortcutConfiguration { Shortcuts = configuration.Shortcuts ?? [] }).DeviceConfiguration;
            var dispatcher = options.MockMode
                ? CreateMockDispatcher()
                : await CreateLiveDispatcherAsync(configuration, monitoringInterval, cancellation.Token).ConfigureAwait(false);

            webSocketServer = new WindowsAgentWebSocketServer(
                new TransportOptions
                {
                    BindAddress = IPAddress.Any,
                    Port = port,
                    WebSocketPath = "/ws",
                    AllowRemoteClients = true
                },
                dispatcher,
                pairingService,
                state,
                configuration.Appearance ?? new AppearanceConfiguration(),
                deviceShortcuts,
                coverCacheDirectory,
                initialAudioProvider ?? (() => volumeService?.CurrentSnapshot),
                HandleShortcutsReportAsync);

            Interlocked.Exchange(ref lastHostConnected, 0);
            state.Changed += HandleAgentStateChanged;
            pairingService.PairingChanged += HandlePairingChanged;
            await webSocketServer.StartAsync(cancellation.Token).ConfigureAwait(false);
            port = webSocketServer.Port;
            mdnsAdvertiser = new MdnsAdvertiser(port);
            await mdnsAdvertiser.StartAsync(cancellation.Token).ConfigureAwait(false);
            state.SetHostListening(port);
            if (TryMarkHostConnected(ref lastHostConnected, state.Snapshot().HostStatus == "connected"))
            {
                _ = PublishMediaUpdateAsync(mediaMonitor?.CurrentSnapshot, CancellationToken.None);
            }

            await UpdatePairingStateAsync().ConfigureAwait(false);
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<DeviceCommandDispatcher> CreateLiveDispatcherAsync(
        AgentConfiguration configuration,
        TimeSpan monitoringInterval,
        CancellationToken cancellationToken)
    {
        audioEndpoint = new CoreAudioEndpoint();
        volumeService = new WindowsMasterVolumeService(audioEndpoint);
        volumeService.Refresh();

        mediaSource = new WindowsGlobalMediaSessionSource();
        mediaMonitor = new MediaSessionMonitor(mediaSource);
        mediaMonitor.SnapshotChanged += HandleMediaSnapshotChanged;
        mediaMonitor.RefreshFailed += HandleMediaRefreshFailed;
        await mediaMonitor.StartAsync(cancellationToken).ConfigureAwait(false);

        systemMetricsProvider = new WindowsSystemMetricsProvider();
        systemMetricsTask = Task.Run(() => SampleSystemMetricsAsync(systemMetricsProvider, monitoringInterval, cancellationToken), CancellationToken.None);

        var shortcuts = ShortcutCatalog.Create(new ShortcutConfiguration { Shortcuts = configuration.Shortcuts ?? [] });
        var shortcutExecutor = new ShortcutExecutor(
            shortcuts,
            new WindowsProcessApplicationLauncher(),
            new WindowsShellUrlOpener(),
            new WindowsHotkeySender(),
            new WindowsProcessScriptRunner(),
            new WindowsPredefinedShortcutAdapter(ToggleDefaultMicrophoneMute));

        return new DeviceCommandDispatcher(
            new MediaSessionMonitorController(mediaMonitor),
            new MasterVolumeController(volumeService),
            new ShortcutExecutorController(shortcutExecutor),
            new RejectingSystemCommandExecutor());
    }

    private static bool ToggleDefaultMicrophoneMute()
    {
        using var microphoneEndpoint = CoreAudioEndpoint.CreateDefaultCaptureEndpoint();
        microphoneEndpoint.SetMute(!microphoneEndpoint.GetMute());
        return true;
    }

    private static DeviceCommandDispatcher CreateMockDispatcher()
    {
        return new DeviceCommandDispatcher(
            new MockMediaController(),
            new MockAudioController(),
            new MockShortcutExecutor(),
            new RejectingSystemCommandExecutor());
    }

    public async Task ReconnectAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await StartAsync().ConfigureAwait(false);
    }

    public IReadOnlyList<PendingShortcutImport> GetPendingShortcutImports(IReadOnlySet<string> savedShortcutIds)
    {
        return pendingShortcutImports.Snapshot(savedShortcutIds);
    }

    public Task<bool> SendDeviceRestartAsync(CancellationToken cancellationToken = default)
    {
        AgentDiagnosticLog.Write("Sending device restart command");
        return SendDeviceCommandAsync(ProtocolStateMapper.CreateDeviceRestart(CreateRequestId("restart")), cancellationToken);
    }

    public Task<bool> SendDeviceFactoryResetAsync(CancellationToken cancellationToken = default)
    {
        AgentDiagnosticLog.Write("Sending device factory reset command");
        return SendDeviceCommandAsync(ProtocolStateMapper.CreateDeviceFactoryReset(CreateRequestId("factory-reset")), cancellationToken);
    }

    public Task<bool> ApplyDeviceSettingsAsync(AppearanceConfiguration appearance, CancellationToken cancellationToken = default)
    {
        AgentDiagnosticLog.Write("Sending device settings command");
        return SendDeviceCommandAsync(ProtocolStateMapper.CreateDeviceSettings(CreateRequestId("settings"), appearance), cancellationToken);
    }

    public async Task<bool> ForgetConnectedDeviceAsync(CancellationToken cancellationToken = default)
    {
        var deviceId = state.Snapshot().DeviceId;
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        AgentDiagnosticLog.Write("Forgetting connected device pairing record");
        var forgotten = await pairingService.ForgetAsync(deviceId, cancellationToken).ConfigureAwait(false);
        if (!forgotten)
        {
            return false;
        }

        if (webSocketServer is not null)
        {
            await webSocketServer.DisconnectActiveDeviceAsync(cancellationToken).ConfigureAwait(false);
        }

        await ReconnectAsync().ConfigureAwait(false);
        state.MarkDeviceForgotten(deviceId);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task StopAsync()
    {
        var source = cancellation;
        cancellation = null;
        if (source is not null)
        {
            await source.CancelAsync().ConfigureAwait(false);
        }

        if (mdnsAdvertiser is not null)
        {
            await mdnsAdvertiser.DisposeAsync().ConfigureAwait(false);
            mdnsAdvertiser = null;
        }

        if (webSocketServer is not null)
        {
            await webSocketServer.DisposeAsync().ConfigureAwait(false);
            webSocketServer = null;
        }

        if (systemMetricsTask is not null)
        {
            try
            {
                await systemMetricsTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            systemMetricsTask = null;
        }

        if (mediaMonitor is not null)
        {
            mediaMonitor.SnapshotChanged -= HandleMediaSnapshotChanged;
            mediaMonitor.RefreshFailed -= HandleMediaRefreshFailed;
            await mediaMonitor.DisposeAsync().ConfigureAwait(false);
            mediaMonitor = null;
        }

        coverProcessor = null;

        mediaSource = null;
        volumeService?.Dispose();
        volumeService = null;
        audioEndpoint?.Dispose();
        audioEndpoint = null;
        systemMetricsProvider = null;
        state.Changed -= HandleAgentStateChanged;
        pairingService.PairingChanged -= HandlePairingChanged;
        source?.Dispose();
    }

    private Task<bool> SendDeviceCommandAsync(string message, CancellationToken cancellationToken)
    {
        return webSocketServer is null
            ? Task.FromResult(false)
            : webSocketServer.SendToActiveDeviceAsync(message, cancellationToken);
    }

    private static string CreateRequestId(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    private void HandleMediaSnapshotChanged(object? sender, MediaSnapshot? snapshot)
    {
        _ = PublishMediaUpdateAsync(snapshot, CancellationToken.None);
    }

    private void HandleMediaRefreshFailed(object? sender, MediaSessionRefreshFailedEventArgs args)
    {
        var paramName = args.ParamName is null ? string.Empty : $" param={args.ParamName}";
        AgentDiagnosticLog.Write($"Media session refresh failed operation={args.Operation}: {args.ExceptionType}{paramName}");
    }

    internal static bool TryMarkHostConnected(ref int lastConnected, bool isConnected)
    {
        var previous = Interlocked.Exchange(ref lastConnected, isConnected ? 1 : 0);
        return isConnected && previous == 0;
    }

    private void HandleAgentStateChanged(object? sender, EventArgs args)
    {
        var isConnected = state.Snapshot().HostStatus == "connected";
        if (TryMarkHostConnected(ref lastHostConnected, isConnected))
        {
            _ = PublishMediaUpdateAsync(mediaMonitor?.CurrentSnapshot, CancellationToken.None);
        }
    }

    private ValueTask HandleShortcutsReportAsync(ShortcutsReportMessage report, CancellationToken cancellationToken)
    {
        var configuration = AgentConfigurationStoreFactory.CreateForCurrentUser().LoadOrDefault();
        var savedIds = (configuration.Shortcuts ?? [])
            .Select(shortcut => shortcut.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        pendingShortcutImports.UpdateFromDeviceReport(report, savedIds);
        return ValueTask.CompletedTask;
    }

    private async Task PublishMediaUpdateAsync(MediaSnapshot? snapshot, CancellationToken cancellationToken)
    {
        try
        {
            string? coverId = null;
            string? accent = null;

            if (snapshot?.Thumbnail is not null && coverProcessor is not null)
            {
                await using var thumbnailStream = await snapshot.Thumbnail.OpenReadAsync(cancellationToken).ConfigureAwait(false);
                var cover = await coverProcessor.ProcessAsync(thumbnailStream, cancellationToken).ConfigureAwait(false);
                coverId = cover?.CoverId;
                accent = cover?.AccentHex;
            }

            await SendDeviceCommandAsync(ProtocolStateMapper.CreateMediaUpdate(snapshot, coverId, accent), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AgentDiagnosticLog.Write($"Media update publish failed: {ex.GetType().Name}");
        }
    }

    private static string GetCoverCacheDirectory()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(basePath, "StreamDeckAgent", "cache");
    }

    private void HandlePairingChanged(object? sender, EventArgs args)
    {
        _ = UpdatePairingStateAsync();
    }

    private async Task UpdatePairingStateAsync()
    {
        var pending = await pairingService.ListPendingAsync().ConfigureAwait(false);
        var next = pending.FirstOrDefault();
        if (next is not null)
        {
            state.MarkPairingPending(next.DeviceId, next.Name, next.RemoteAddress);
            return;
        }

        if (state.Snapshot().HostStatus == "pairing pending")
        {
            state.SetHostListening(port);
        }
    }

    private static async Task SampleSystemMetricsAsync(
        ISystemMetricsProvider provider,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        provider.GetSnapshot();

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            provider.GetSnapshot();
        }
    }
}

internal sealed class MockMediaController : IHostMediaController
{
    public Task<bool> ExecuteAsync(MediaCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }
}

internal sealed class MockAudioController : IHostAudioController
{
    public Task<bool> SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    public Task<bool> ToggleMuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }
}

internal sealed class MockShortcutExecutor : IHostShortcutExecutor
{
    public Task<bool> ExecuteAsync(string shortcutId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }
}
