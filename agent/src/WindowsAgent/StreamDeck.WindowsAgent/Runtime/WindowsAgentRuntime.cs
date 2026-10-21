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
    int? listenPortOverride = null,
    AgentConfigurationStore? configurationStore = null) : IAsyncDisposable
{
    private readonly AgentConfigurationStore configurationStore = configurationStore ?? AgentConfigurationStoreFactory.CreateForCurrentUser();
    private int reconnectCount;
    private CancellationTokenSource? cancellation;
    private WindowsAgentWebSocketServer? webSocketServer;
    private MdnsAdvertiser? mdnsAdvertiser;
    private WindowsMasterVolumeService? volumeService;
    private CoreAudioEndpoint? audioEndpoint;
    private WindowsGlobalMediaSessionSource? mediaSource;
    private MediaSessionMonitor? mediaMonitor;
    private CoverProcessor? coverProcessor;
    private ShortcutCatalog? shortcutCatalog;
    private IReadOnlyList<string?>? deviceSlots;
    private string? lastPublishedMedia;
    private string? lastTrackKey;
    private string? lastTrackAndPlayState;
    private string? cachedCoverId;
    private string? cachedAccent;
    private IMediaThumbnailReference? lastThumbnailRef;
    private readonly SemaphoreSlim mediaPublishGate = new(1, 1);
    private MediaArtworkCoordinator? artworkCoordinator;
    private MediaArtworkCoordinator ArtworkCoordinator => artworkCoordinator ??= new MediaArtworkCoordinator(
        () => coverProcessor,
        OnCoverReadyAsync,
        AgentDiagnosticLog.Write);
    private UsbDeviceRuntime? usbRuntime;
    private WindowsSystemMetricsProvider? systemMetricsProvider;
    private volatile SystemMetrics? lastSampledMetrics;
    private Task? systemMetricsTask;
    private readonly PendingShortcutImportStore pendingShortcutImports = new();
    private int port;

    public int Port => port;
    internal int ReconnectCount => reconnectCount;
    internal string? EffectiveUsbPort => options.UsbPort ?? configurationStore.LoadOrDefault().Server?.UsbPort;
    internal Task WaitForArtworkIdleAsync(TimeSpan? timeout = null) => ArtworkCoordinator.WaitForArtworkIdleAsync(timeout);

    public async Task StartAsync()
    {
        if (cancellation is not null)
        {
            return;
        }

        var configuration = configurationStore.LoadOrDefault();
        state.MarkConfigurationSaved(configuration.Server?.UsbPort, options.UsbPort);
        port = listenPortOverride ?? configuration.Server?.Port ?? 8765;
        var monitoringInterval = TimeSpan.FromMilliseconds(configuration.Monitoring?.IntervalMs ?? 1000);
        var coverCacheDirectory = GetCoverCacheDirectory();
        coverProcessor = new CoverProcessor(new CoverProcessingOptions { CacheDirectory = coverCacheDirectory, CoverSizePixels = 128, JpegQuality = 80 });
        cancellation = new CancellationTokenSource();
        state.SetMode(options.MockMode ? "mock" : "live");

        try
        {
            shortcutCatalog = ShortcutCatalog.Create(new ShortcutConfiguration { Shortcuts = configuration.Shortcuts ?? [] });
            deviceSlots = configuration.Profiles.FirstOrDefault(p => p.Name == configuration.ActiveProfile)?.AssignedShortcutIds;
            var deviceShortcuts = BuildDeviceShortcuts(shortcutCatalog.DeviceConfiguration, deviceSlots);
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
                ResolveAssignedLayout(configuration.Appearance ?? new AppearanceConfiguration(), deviceSlots),
                deviceShortcuts,
                coverCacheDirectory,
                initialAudioProvider ?? ReadCurrentAudioSnapshot,
                HandleShortcutsReportAsync,
                shortcutsProvider: () => BuildDeviceShortcuts(shortcutCatalog!.DeviceConfiguration, deviceSlots),
                appearanceProvider: () => ResolveAssignedLayout(configurationStore.LoadOrDefault().Appearance ?? new AppearanceConfiguration(), deviceSlots),
                initialMediaSender: (sink, token) => HydrateInitialDeviceStateAsync(sink, token));

            var effectiveUsbPort = options.UsbPort ?? configuration.Server?.UsbPort;
            if (!options.MockMode && (configuration.Server?.UsbAutoConnect != false || effectiveUsbPort is not null))
            {
                usbRuntime = new UsbDeviceRuntime(
                    () => ResolveUsbPortCandidates(options with { UsbPort = effectiveUsbPort }),
                    usbPort =>
                    {
                        AgentDiagnosticLog.Write($"Opening USB serial transport on {usbPort}");
                        return new SerialPortUsbDuplexStream(usbPort);
                    },
                    dispatcher,
                    pairingService,
                    state,
                    ResolveAssignedLayout(configuration.Appearance ?? new AppearanceConfiguration(), deviceSlots),
                    deviceShortcuts,
                    coverCacheDirectory,
                    initialAudioProvider ?? ReadCurrentAudioSnapshot,
                    HandleShortcutsReportAsync,
                shortcutsProvider: () => BuildDeviceShortcuts(shortcutCatalog!.DeviceConfiguration, deviceSlots),
                appearanceProvider: () => ResolveAssignedLayout(configurationStore.LoadOrDefault().Appearance ?? new AppearanceConfiguration(), deviceSlots),
                initialMediaSender: (sink, token) => HydrateInitialDeviceStateAsync(sink, token));
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
            : UsbSerialPortDiscovery.FindConnectedSupportedPorts();
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
        systemMetricsTask = Task.Run(() => RunSystemMetricsPublisherAsync(systemMetricsProvider, monitoringInterval, cancellationToken), CancellationToken.None);

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
            new RejectingSystemCommandExecutor(), diagnostics: AgentDiagnosticLog.Trace);
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

    public async Task<bool> UpdateShortcutsAsync(IReadOnlyList<StreamDeck.Shortcuts.ShortcutDefinition> shortcuts, CancellationToken cancellationToken = default, IReadOnlyList<string?>? assignedSlots = null)
    {
        var configuration = new ShortcutConfiguration { Shortcuts = shortcuts };
        var current = shortcutCatalog;
        if (current is null) return false;
        current.Replace(configuration);
        deviceSlots = assignedSlots?.ToArray();
        return await SendDeviceCommandAsync(ProtocolStateMapper.CreateShortcutsUpdate(BuildDeviceShortcuts(current.DeviceConfiguration, deviceSlots)), cancellationToken).ConfigureAwait(false);
    }

    internal static DeviceShortcutConfiguration BuildDeviceShortcuts(DeviceShortcutConfiguration catalog, IReadOnlyList<string?>? slots)
    {
        if (slots is null) return new(catalog.Shortcuts.Take(6).ToArray());
        return new(Enumerable.Range(0, 6).Select(index => {
            var id = index < slots.Count ? slots[index] : null;
            return catalog.Shortcuts.FirstOrDefault(s => s.Id == id)
                ?? new DeviceShortcutDefinition($"__empty_slot_{index}", "No shortcut", "empty", "label");
        }).ToArray());
    }

    internal static AppearanceConfiguration ResolveAssignedLayout(AppearanceConfiguration appearance, IReadOnlyList<string?>? slots)
    {
        if (appearance.ShortcutsLayout != "auto" || slots is null) return appearance;
        var last = Enumerable.Range(0, Math.Min(6, slots.Count)).Where(i => slots[i] is not null).DefaultIfEmpty(0).Max();
        return appearance with { ShortcutsLayout = last < 1 ? "hero-1" : last < 2 ? "split-2" : last < 4 ? "quad-4" : "grid-6" };
    }

    public Task<bool> SyncSavedConfigurationAsync(CancellationToken cancellationToken = default) => RetryConfigurationSyncAsync(cancellationToken);

    public Task<bool> RetryConfigurationSyncAsync(CancellationToken cancellationToken = default)
    {
        var saved = configurationStore.LoadOrDefault();
        var savedSlots = saved.Profiles.FirstOrDefault(p => p.Name == saved.ActiveProfile)?.AssignedShortcutIds;
        var savedCatalog = ShortcutCatalog.Create(new ShortcutConfiguration { Shortcuts = saved.Shortcuts ?? [] });
        shortcutCatalog = savedCatalog;
        deviceSlots = savedSlots;
        return new ConfigurationSynchronizer(state).SendAsync(async token =>
        {
            var shortcuts = ProtocolStateMapper.CreateShortcutsUpdate(BuildDeviceShortcuts(savedCatalog.DeviceConfiguration, savedSlots));
            var settings = ProtocolStateMapper.CreateDeviceSettings(CreateRequestId("settings"),
                ResolveAssignedLayout(saved.Appearance ?? new AppearanceConfiguration(), savedSlots), applyBrightness: true);
            // Capture one transport for the whole attempt; never split a configuration across devices.
            var usb = usbRuntime;
            if (usb?.IsReady == true)
                return await usb.SendConfigurationAsync(shortcuts, settings, token).ConfigureAwait(false);
            var socket = webSocketServer;
            return socket is not null && await socket.SendConfigurationAsync(shortcuts, settings, token).ConfigureAwait(false);
        }, cancellationToken);
    }
    public async Task ReconnectAsync()
    {
        reconnectCount++;
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
        return SendDeviceCommandAsync(ProtocolStateMapper.CreateDeviceSettings(CreateRequestId("settings"), ResolveAssignedLayout(appearance, deviceSlots), applyBrightness: true), cancellationToken);
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
        if (artworkCoordinator is not null)
        {
            await artworkCoordinator.DisposeAsync().ConfigureAwait(false);
        }
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

        systemMetricsProvider?.Dispose(); systemMetricsProvider = null;
        lastSampledMetrics = null;
        if (mediaMonitor is not null)
        {
            mediaMonitor.SnapshotChanged -= HandleMediaSnapshotChanged;
            mediaMonitor.RefreshFailed -= HandleMediaRefreshFailed;
            await mediaMonitor.DisposeAsync().ConfigureAwait(false);
            mediaMonitor = null;
        }

        artworkCoordinator?.Reset();
        coverProcessor = null;
        cachedCoverId = null;
        cachedAccent = null;
        lastThumbnailRef = null;
        lastTrackKey = null;
        lastTrackAndPlayState = null;

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
        var configuration = configurationStore.LoadOrDefault();
        var savedIds = (configuration.Shortcuts ?? [])
            .Select(shortcut => shortcut.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        pendingShortcutImports.UpdateFromDeviceReport(report, savedIds);
        return ValueTask.CompletedTask;
    }

    private async Task PublishMediaUpdateAsync(MediaSnapshot? snapshot, CancellationToken cancellationToken, IProtocolSink? target = null)
    {
        if (target is not null) {
            var enabled = Environment.GetEnvironmentVariable("CYD_DECK_DIAGNOSTICS") == "1" ? "true" : "false";
            await target.SendTextAsync("{\"protocol\":1,\"type\":\"diagnostics.configure\",\"data\":{\"enabled\":" + enabled + "}}", cancellationToken).ConfigureAwait(false);
        }

        var (coverId, accent, _) = ArtworkCoordinator.Update(snapshot, target);
        cachedCoverId = coverId;
        cachedAccent = accent;

        await PublishMediaUpdateCoreAsync(snapshot, coverId, accent, cancellationToken, target).ConfigureAwait(false);
    }

    private async Task OnCoverReadyAsync(long generation, MediaSnapshot? snapshot, string coverId, string? accent, IProtocolSink? target)
    {
        if (!ArtworkCoordinator.IsCurrentGeneration(generation))
        {
            return;
        }

        cachedCoverId = coverId;
        cachedAccent = accent;

        var currentSnapshot = mediaMonitor?.CurrentSnapshot ?? snapshot;
        await PublishMediaUpdateCoreAsync(currentSnapshot, coverId, accent, CancellationToken.None, target).ConfigureAwait(false);
    }

    private async Task PublishMediaUpdateCoreAsync(
        MediaSnapshot? snapshot,
        string? coverId,
        string? accent,
        CancellationToken cancellationToken,
        IProtocolSink? target = null)
    {
        await mediaPublishGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var trackKey = $"{snapshot?.SourceAppUserModelId}|{snapshot?.Title}|{snapshot?.Artist}|{snapshot?.AlbumTitle}";
            AgentDiagnosticLog.Trace("stage=media_state_updated");
            var thumbnailChanged = !ReferenceEquals(snapshot?.Thumbnail, lastThumbnailRef);
            if (trackKey != lastTrackKey || thumbnailChanged)
            {
                lastTrackKey = trackKey;
                lastThumbnailRef = snapshot?.Thumbnail;
                lastTrackAndPlayState = null;
            }

            if (volumeService?.CurrentSnapshot is { } audioSnapshot)
            {
                var audioMessage = ProtocolStateMapper.CreateAudioUpdate(audioSnapshot);
                if (target is not null) await target.SendTextAsync(audioMessage, cancellationToken).ConfigureAwait(false);
                else await SendDeviceCommandAsync(audioMessage, cancellationToken).ConfigureAwait(false);
            }

            var trackAndPlayState = $"{trackKey}|{snapshot?.PlaybackState}|{coverId}";
            var isTimelineOnly = target is null &&
                                 trackAndPlayState == lastTrackAndPlayState &&
                                 snapshot is not null;

            string message;
            if (isTimelineOnly)
            {
                message = ProtocolSerializer.Serialize(
                    ProtocolConstants.MessageTypes.MediaTimeline,
                    requestId: null,
                    new MediaTimelineMessage(
                        (long)snapshot!.Timeline.Position.TotalMilliseconds,
                        snapshot.Timeline.EndTime > TimeSpan.Zero ? (long)snapshot.Timeline.EndTime.TotalMilliseconds : null,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            }
            else
            {
                message = ProtocolStateMapper.CreateMediaUpdate(snapshot, coverId, accent);
            }

            var sent = true;
            if (target is UsbDeviceRuntime.ActiveUsbSink usbSink) await usbSink.SendUpdateAsync(message, cancellationToken).ConfigureAwait(false);
            else if (target is not null) await target.SendTextAsync(message, cancellationToken).ConfigureAwait(false);
            else sent = await SendDeviceCommandAsync(message, cancellationToken).ConfigureAwait(false);

            if (sent && !isTimelineOnly)
            {
                lastTrackAndPlayState = trackAndPlayState;
                var signature = $"{snapshot?.SourceAppUserModelId}|{snapshot?.Title}|{snapshot?.PlaybackState}|{coverId}";
                if (signature != lastPublishedMedia)
                {
                    lastPublishedMedia = signature;
                    AgentDiagnosticLog.Write($"Media sent playing={snapshot?.PlaybackState == MediaPlaybackState.Playing} metadata={!string.IsNullOrWhiteSpace(snapshot?.Title)} artwork={coverId is not null}");
                }
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

    private async ValueTask HydrateInitialDeviceStateAsync(IProtocolSink sink, CancellationToken cancellationToken)
    {
        var metrics = lastSampledMetrics;
        if (metrics is null && systemMetricsProvider is not null)
        {
            try
            {
                metrics = systemMetricsProvider.GetSnapshot();
                if (metrics is not null)
                {
                    lastSampledMetrics = metrics;
                    state.UpdateSystemMetrics(metrics);
                }
            }
            catch (Exception ex)
            {
                AgentDiagnosticLog.Write($"Initial hydration metrics sample failed: {ex.GetType().Name}");
            }
        }

        if (metrics is not null)
        {
            try
            {
                var statsMessage = ProtocolStateMapper.CreateSystemStats(metrics);
                await sink.SendTextAsync(statsMessage, cancellationToken).ConfigureAwait(false);
                AgentDiagnosticLog.Trace("stage=stats_sent");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AgentDiagnosticLog.Write($"Initial hydration system stats send failed: {ex.GetType().Name}");
            }
        }
        // Clock/PC hydration must not wait for artwork processing.
        await PublishMediaUpdateAsync(mediaMonitor?.CurrentSnapshot, cancellationToken, sink).ConfigureAwait(false);
    }

    private Task SampleSystemMetricsAsync(
        ISystemMetricsProvider provider,
        TimeSpan interval,
        CancellationToken cancellationToken) =>
        RunSystemMetricsPublisherAsync(provider, interval, cancellationToken);

    private Task RunSystemMetricsPublisherAsync(
        ISystemMetricsProvider provider,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        return SystemMetricsPublisher.RunAsync(
            provider,
            interval,
            metrics =>
            {
                lastSampledMetrics = metrics;
                state.UpdateSystemMetrics(metrics);
            },
            async (metrics, token) =>
            {
                AgentDiagnosticLog.Trace("stage=snapshot_sampled source=WindowsSystemMetricsProvider");
                if (usbRuntime?.IsReady == true || state.Snapshot().HostStatus == "connected")
                {
                    try
                    {
                        var sent = await SendDeviceCommandAsync(ProtocolStateMapper.CreateSystemStats(metrics), token).ConfigureAwait(false);
                        if (sent)
                        {
                            AgentDiagnosticLog.Trace("stage=stats_sent");
                        }
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        AgentDiagnosticLog.Write($"System stats publish failed: {exception.GetType().Name}");
                    }
                }
            },
            error =>
            {
                AgentDiagnosticLog.Write($"System metrics sampling failed: {error.GetType().Name}");
            },
            cancellationToken,
            onTick: () =>
            {
                if (mediaMonitor is not null)
                {
                    try
                    {
                        mediaMonitor.TryRefreshTimeline();
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        AgentDiagnosticLog.Write($"Media timeline poll failed: {exception.GetType().Name}");
                    }
                }
            });
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
