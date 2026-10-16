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
    private ShortcutCatalog? shortcutCatalog;
    private string? lastPublishedMedia;
    private readonly SemaphoreSlim mediaPublishGate = new(1, 1);
    private UsbDeviceRuntime? usbRuntime;
    private WindowsSystemMetricsProvider? systemMetricsProvider;
    private Task? systemMetricsTask;
    private readonly PendingShortcutImportStore pendingShortcutImports = new();
    private int port;

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
        coverProcessor = new CoverProcessor(new CoverProcessingOptions { CacheDirectory = coverCacheDirectory, CoverSizePixels = 128, JpegQuality = 80 });
        cancellation = new CancellationTokenSource();
        state.SetMode(options.MockMode ? "mock" : "live");

        try
        {
            shortcutCatalog = ShortcutCatalog.Create(new ShortcutConfiguration { Shortcuts = configuration.Shortcuts ?? [] });
            var deviceShortcuts = shortcutCatalog.DeviceConfiguration;
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
                initialAudioProvider ?? ReadCurrentAudioSnapshot,
                HandleShortcutsReportAsync,
                shortcutsProvider: () => shortcutCatalog!.DeviceConfiguration,
                appearanceProvider: () => AgentConfigurationStoreFactory.CreateForCurrentUser().LoadOrDefault().Appearance ?? new AppearanceConfiguration(),
                initialMediaSender: (sink, token) => new ValueTask(PublishMediaUpdateAsync(mediaMonitor?.CurrentSnapshot, token, sink)));

            if (!options.MockMode && (configuration.Server?.UsbAutoConnect != false || options.UsbPort is not null))
            {
                usbRuntime = new UsbDeviceRuntime(
                    () => ResolveUsbPortCandidates(options with { UsbPort = options.UsbPort ?? configuration.Server?.UsbPort }),
                    usbPort =>
                    {
                        AgentDiagnosticLog.Write($"Opening USB serial transport on {usbPort}");
                        return new SerialPortUsbDuplexStream(usbPort);
                    },
                    dispatcher,
                    pairingService,
                    state,
                    configuration.Appearance ?? new AppearanceConfiguration(),
                    deviceShortcuts,
                    coverCacheDirectory,
                    initialAudioProvider ?? ReadCurrentAudioSnapshot,
                    HandleShortcutsReportAsync,
                shortcutsProvider: () => shortcutCatalog!.DeviceConfiguration,
                appearanceProvider: () => AgentConfigurationStoreFactory.CreateForCurrentUser().LoadOrDefault().Appearance ?? new AppearanceConfiguration(),
                initialMediaSender: (sink, token) => new ValueTask(PublishMediaUpdateAsync(mediaMonitor?.CurrentSnapshot, token, sink)));
                await usbRuntime.StartAsync(cancellation.Token).ConfigureAwait(false);
            }


            pairingService.PairingChanged += HandlePairingChanged;
            await webSocketServer.StartAsync(cancellation.Token).ConfigureAwait(false);
            port = webSocketServer.Port;
            mdnsAdvertiser = new MdnsAdvertiser(port);
            await mdnsAdvertiser.StartAsync(cancellation.Token).ConfigureAwait(false);
            state.SetHostListening(port);
            await UpdatePairingStateAsync().ConfigureAwait(false);
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static IReadOnlyList<string> ResolveUsbPortCandidates(AgentOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.UsbPort)
            ? [options.UsbPort]
            : UsbSerialPortDiscovery.FindConnectedCh340Ports();
    }

    private async Task<DeviceCommandDispatcher> CreateLiveDispatcherAsync(
        AgentConfiguration configuration,
        TimeSpan monitoringInterval,
        CancellationToken cancellationToken)
    {
        audioEndpoint = new CoreAudioEndpoint();
        volumeService = new WindowsMasterVolumeService(audioEndpoint);
        volumeService.AudioChanged += HandleAudioChanged;
        volumeService.Refresh();
        state.UpdateHostAudio(volumeService.CurrentSnapshot);

        mediaSource = new WindowsGlobalMediaSessionSource();
        mediaMonitor = new MediaSessionMonitor(mediaSource);
        mediaMonitor.SnapshotChanged += HandleMediaSnapshotChanged;
        mediaMonitor.RefreshFailed += HandleMediaRefreshFailed;
        await mediaMonitor.StartAsync(cancellationToken).ConfigureAwait(false);
        state.UpdateHostMedia(mediaMonitor.CurrentSnapshot);

        systemMetricsProvider = new WindowsSystemMetricsProvider();
        systemMetricsTask = Task.Run(() => SampleSystemMetricsAsync(systemMetricsProvider, monitoringInterval, cancellationToken), CancellationToken.None);

        var shortcuts = shortcutCatalog ?? throw new InvalidOperationException("Shortcut catalog is not initialized.");
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

    public async Task<bool> ExecuteHostMediaCommandAsync(MediaCommand command, CancellationToken cancellationToken = default)
    {
        if (mediaMonitor is not null)
        {
            var controller = new MediaSessionMonitorController(mediaMonitor);
            return await controller.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    public Task<bool> SetHostVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
    {
        if (volumeService is not null)
        {
            var controller = new MasterVolumeController(volumeService);
            return controller.SetVolumeAsync(Math.Clamp(volumePercent, 0, 100), cancellationToken);
        }

        state.UpdateHostAudio(new AudioSnapshot(Math.Clamp(volumePercent, 0, 100), state.Snapshot().CurrentAudio?.IsMuted ?? false));
        return Task.FromResult(true);
    }

    public Task<bool> ToggleHostMuteAsync(CancellationToken cancellationToken = default)
    {
        if (volumeService is not null)
        {
            var controller = new MasterVolumeController(volumeService);
            return controller.ToggleMuteAsync(cancellationToken);
        }

        var current = state.Snapshot().CurrentAudio ?? new AudioSnapshot(65, false);
        state.UpdateHostAudio(new AudioSnapshot(current.VolumePercent, !current.IsMuted));
        return Task.FromResult(true);
    }

    public async Task<bool> UpdateShortcutsAsync(IReadOnlyList<StreamDeck.Shortcuts.ShortcutDefinition> shortcuts, CancellationToken cancellationToken = default)
    {
        var configuration = new ShortcutConfiguration { Shortcuts = shortcuts };
        var current = shortcutCatalog;
        if (current is null) return false;
        current.Replace(configuration);
        return await SendDeviceCommandAsync(ProtocolStateMapper.CreateShortcutsUpdate(current.DeviceConfiguration), cancellationToken).ConfigureAwait(false);
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
        return SendDeviceCommandAsync(ProtocolStateMapper.CreateDeviceSettings(CreateRequestId("settings"), appearance, applyBrightness: true), cancellationToken);
    }

    public async Task<bool> ForgetDeviceAsync(string? specificDeviceId = null, CancellationToken cancellationToken = default)
    {
        var deviceId = specificDeviceId ?? state.Snapshot().DeviceId;
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        AgentDiagnosticLog.Write($"Forgetting device pairing record: {deviceId}");
        var forgotten = await pairingService.ForgetAsync(deviceId, cancellationToken).ConfigureAwait(false);
        if (!forgotten)
        {
            return false;
        }

        if (webSocketServer is not null && string.Equals(state.Snapshot().DeviceId, deviceId, StringComparison.Ordinal))
        {
            await webSocketServer.DisconnectActiveDeviceAsync(cancellationToken).ConfigureAwait(false);
        }

        await ReconnectAsync().ConfigureAwait(false);
        state.MarkDeviceForgotten(deviceId);
        return true;
    }

    public Task<bool> ForgetConnectedDeviceAsync(CancellationToken cancellationToken = default) =>
        ForgetDeviceAsync(null, cancellationToken);

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

        if (usbRuntime is not null)
        {
            await usbRuntime.DisposeAsync().ConfigureAwait(false);
            usbRuntime = null;
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
        if (volumeService is not null) volumeService.AudioChanged -= HandleAudioChanged;
        volumeService?.Dispose();
        volumeService = null;
        audioEndpoint?.Dispose();
        audioEndpoint = null;
        systemMetricsProvider = null;
        pairingService.PairingChanged -= HandlePairingChanged;
        source?.Dispose();
    }

    private Task<bool> SendDeviceCommandAsync(string message, CancellationToken cancellationToken)
    {
        return SendDeviceCommandCoreAsync(message, cancellationToken);
    }

    private async Task<bool> SendDeviceCommandCoreAsync(string message, CancellationToken cancellationToken)
    {
        var activeUsbRuntime = usbRuntime;
        var activeWebSocketServer = webSocketServer;
        return await SendWithUsbPriorityAsync(
            activeUsbRuntime?.IsReady == true,
            () => activeUsbRuntime is not null
                ? activeUsbRuntime.SendToActiveDeviceAsync(message, cancellationToken)
                : Task.FromResult(false),
            () => activeWebSocketServer is not null
                ? activeWebSocketServer.SendToActiveDeviceAsync(message, cancellationToken)
                : Task.FromResult(false)).ConfigureAwait(false);
    }

    internal static async Task<bool> SendWithUsbPriorityAsync(
        bool usbReady,
        Func<Task<bool>> sendUsb,
        Func<Task<bool>> sendWebSocket)
    {
        if (usbReady && await TrySendUsbAsync().ConfigureAwait(false))
        {
            return true;
        }

        if (await sendWebSocket().ConfigureAwait(false))
        {
            return true;
        }

        return !usbReady && await TrySendUsbAsync().ConfigureAwait(false);

        async Task<bool> TrySendUsbAsync()
        {
            try
            {
                return await sendUsb().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
            {
                AgentDiagnosticLog.Write($"USB command send failed: {exception.GetType().Name}");
                return false;
            }
        }
    }

    private static string CreateRequestId(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    private AudioSnapshot? ReadCurrentAudioSnapshot()
    {
        volumeService?.Refresh();
        return volumeService?.CurrentSnapshot;
    }

    private void HandleAudioChanged(object? sender, AudioSnapshot snapshot)
    {
        state.UpdateHostAudio(snapshot);
        _ = PublishAudioUpdateAsync(snapshot);
    }

    private async Task PublishAudioUpdateAsync(AudioSnapshot snapshot)
    {
        try { await SendDeviceCommandAsync(ProtocolStateMapper.CreateAudioUpdate(snapshot), CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) { AgentDiagnosticLog.Write($"Audio publish failed: {exception.GetType().Name}"); }
    }

    private void HandleMediaSnapshotChanged(object? sender, MediaSnapshot? snapshot)
    {
        state.UpdateHostMedia(snapshot);
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

    private ValueTask HandleShortcutsReportAsync(ShortcutsReportMessage report, CancellationToken cancellationToken)
    {
        var configuration = AgentConfigurationStoreFactory.CreateForCurrentUser().LoadOrDefault();
        var savedIds = (configuration.Shortcuts ?? [])
            .Select(shortcut => shortcut.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        pendingShortcutImports.UpdateFromDeviceReport(report, savedIds);
        return ValueTask.CompletedTask;
    }

    private async Task PublishMediaUpdateAsync(MediaSnapshot? snapshot, CancellationToken cancellationToken, IProtocolSink? target = null)
    {
        await mediaPublishGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? coverId = null;
            string? accent = null;

            if (snapshot?.Thumbnail is not null && coverProcessor is not null)
            {
                try
                {
                    await using var thumbnailStream = await snapshot.Thumbnail.OpenReadAsync(cancellationToken).ConfigureAwait(false);
                    var cover = await coverProcessor.ProcessAsync(thumbnailStream, cancellationToken).ConfigureAwait(false);
                    coverId = cover?.CoverId;
                    accent = cover?.AccentHex;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    AgentDiagnosticLog.Write($"Artwork processing failed: {exception.GetType().Name}; publishing track metadata.");
                }
            }

            if (volumeService?.CurrentSnapshot is { } audioSnapshot)
            {
                var audioMessage = ProtocolStateMapper.CreateAudioUpdate(audioSnapshot);
                if (target is not null) await target.SendTextAsync(audioMessage, cancellationToken).ConfigureAwait(false);
                else await SendDeviceCommandAsync(audioMessage, cancellationToken).ConfigureAwait(false);
            }
            var message = ProtocolStateMapper.CreateMediaUpdate(snapshot, coverId, accent);
            var sent = true;
            if (target is UsbDeviceRuntime.ActiveUsbSink usbSink) await usbSink.SendUpdateAsync(message, cancellationToken).ConfigureAwait(false);
            else if (target is not null) await target.SendTextAsync(message, cancellationToken).ConfigureAwait(false);
            else sent = await SendDeviceCommandAsync(message, cancellationToken).ConfigureAwait(false);
            var signature = $"{snapshot?.SourceAppUserModelId}|{snapshot?.Title}|{snapshot?.PlaybackState}|{coverId}";
            if (sent && signature != lastPublishedMedia)
            {
                lastPublishedMedia = signature;
                AgentDiagnosticLog.Write($"Media sent playing={snapshot?.PlaybackState == MediaPlaybackState.Playing} metadata={!string.IsNullOrWhiteSpace(snapshot?.Title)} artwork={coverId is not null}");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AgentDiagnosticLog.Write($"Media update publish failed: {ex.GetType().Name}");
        }
        finally { mediaPublishGate.Release(); }
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

    private async Task SampleSystemMetricsAsync(
        ISystemMetricsProvider provider,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        var initial = provider.GetSnapshot();
        state.UpdateSystemMetrics(initial);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var metrics = provider.GetSnapshot();
            state.UpdateSystemMetrics(metrics);
            if (state.Snapshot().HostStatus == "connected")
            {
                try
                {
                    await SendDeviceCommandAsync(ProtocolStateMapper.CreateSystemStats(metrics), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    AgentDiagnosticLog.Write($"System stats publish failed: {exception.GetType().Name}");
                }
            }
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
