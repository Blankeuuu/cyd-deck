using StreamDeck.Audio;
using StreamDeck.Configuration;
using StreamDeck.Host;
using StreamDeck.Protocol;
using StreamDeck.Shortcuts;
using System.Text;

namespace StreamDeck.WindowsAgent.Runtime;

public sealed class UsbDeviceRuntime(
    Func<IUsbDuplexStream> streamFactory,
    DeviceCommandDispatcher dispatcher,
    PairingApprovalService pairingService,
    AgentState state,
    AppearanceConfiguration appearance,
    DeviceShortcutConfiguration shortcuts,
    string coverCacheDirectory,
    Func<AudioSnapshot?>? initialAudioProvider = null,
    Func<ShortcutsReportMessage, CancellationToken, ValueTask>? shortcutsReportHandler = null,
    Func<IReadOnlyList<string>>? portCandidatesProvider = null,
    Func<string, IUsbDuplexStream>? portStreamFactory = null,
    TimeSpan? helloProbeTimeout = null,
    Func<DeviceShortcutConfiguration>? shortcutsProvider = null,
    Func<AppearanceConfiguration>? appearanceProvider = null,
    Func<IProtocolSink, CancellationToken, ValueTask>? initialMediaSender = null) : IAsyncDisposable
{
    private const string DefaultUsbPortName = "USB";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly object activeGate = new();
    private readonly Func<IReadOnlyList<string>> portCandidatesProvider = portCandidatesProvider ?? (() => [DefaultUsbPortName]);
    private readonly Func<string, IUsbDuplexStream> portStreamFactory = portStreamFactory ?? (_ => streamFactory());
    private readonly TimeSpan helloProbeTimeout = helloProbeTimeout ?? TimeSpan.FromSeconds(6.0);
    private CancellationTokenSource? cancellation;
    private Task? runTask;
    private ActiveUsbSink? activeSink;
    private bool waitingForUsbPortLogged;

    public UsbDeviceRuntime(
        Func<IReadOnlyList<string>> portCandidatesProvider,
        Func<string, IUsbDuplexStream> portStreamFactory,
        DeviceCommandDispatcher dispatcher,
        PairingApprovalService pairingService,
        AgentState state,
        AppearanceConfiguration appearance,
        DeviceShortcutConfiguration shortcuts,
        string coverCacheDirectory,
        Func<AudioSnapshot?>? initialAudioProvider = null,
        Func<ShortcutsReportMessage, CancellationToken, ValueTask>? shortcutsReportHandler = null,
        TimeSpan? helloProbeTimeout = null,
    Func<DeviceShortcutConfiguration>? shortcutsProvider = null,
    Func<AppearanceConfiguration>? appearanceProvider = null,
    Func<IProtocolSink, CancellationToken, ValueTask>? initialMediaSender = null)
        : this(
            () => throw new UsbPortUnavailableException(),
            dispatcher,
            pairingService,
            state,
            appearance,
            shortcuts,
            coverCacheDirectory,
            initialAudioProvider,
            shortcutsReportHandler,
            portCandidatesProvider,
            portStreamFactory,
            helloProbeTimeout,
            shortcutsProvider,
            appearanceProvider,
            initialMediaSender)
    {
    }

    public bool IsReady
    {
        get
        {
            lock (activeGate) return activeSink is not null;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (runTask is not null) return Task.CompletedTask;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        runTask = Task.Run(() => RunReconnectLoopAsync(cancellation.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task<bool> SendToActiveDeviceAsync(string message, CancellationToken cancellationToken = default)
    {
        ActiveUsbSink? sink;
        lock (activeGate) sink = activeSink;
        if (sink is null) return false;
        await sink.SendUpdateAsync(message, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task RunReconnectLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ActiveUsbSink? sink = null;
            try
            {
                var candidates = portCandidatesProvider();
                if (candidates.Count == 0) throw new UsbPortUnavailableException();
                foreach (var port in candidates)
                {
                    await using var stream = portStreamFactory(port);
                    var probeResult = await ProbeHelloFrameAsync(stream, cancellationToken).ConfigureAwait(false);
                    if (probeResult is null) continue;

                    var effectiveStream = probeResult.Leftover.Length > 0
                        ? new PrefixedUsbDuplexStream(stream, probeResult.Leftover)
                        : stream;

                    await RunConnectedStreamAsync(effectiveStream, probeResult.Hello, connectedSink => sink = connectedSink, cancellationToken).ConfigureAwait(false);
                    break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (UsbPortUnavailableException)
            {
                if (!waitingForUsbPortLogged)
                {
                    AgentDiagnosticLog.Write("USB discovery has no usable CH340 serial port");
                    waitingForUsbPortLogged = true;
                }
            }
            catch (Exception ex)
            {
                AgentDiagnosticLog.Write($"USB transport error: {ex.Message} ({ex.GetType().FullName})");
            }
            finally
            {
                if (sink is not null)
                {
                    ClearActiveSink(sink);
                    if (sink.DeviceId is not null && string.Equals(state.Snapshot().RemoteAddress, "USB", StringComparison.Ordinal))
                    {
                        state.MarkDeviceDisconnected(sink.DeviceId);
                    }
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }

        async Task RunConnectedStreamAsync(IUsbDuplexStream stream, string helloFrame, Action<ActiveUsbSink> setCurrentSink, CancellationToken cancellationToken)
        {
                if (waitingForUsbPortLogged)
                {
                    AgentDiagnosticLog.Write("USB serial adapter detected; transport opened");
                    waitingForUsbPortLogged = false;
                }
                UsbLineProtocolTransport? transport = null;
                var connectedSink = new ActiveUsbSink(state, coverCacheDirectory, message => transport!.SendFrameAsync(message, cancellationToken), SetActiveSink);
                setCurrentSink(connectedSink);
                var session = new DeviceProtocolSession(dispatcher, pairingService, connectedSink, null, "USB", appearance, shortcuts, HandleDeviceInfoAsync, initialAudioProvider, shortcutsReportHandler, shortcutsProvider, appearanceProvider, initialMediaSender, connectedSink.SendRequestedCoverAsync);
                transport = new UsbLineProtocolTransport(stream, session, connectedSink);
                connectedSink.TrackIncoming(helloFrame);
                await session.HandleTextMessageAsync(helloFrame, cancellationToken).ConfigureAwait(false);
                await transport.RunAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record ProbeResult(string Hello, byte[] Leftover);

    private async Task<ProbeResult?> ProbeHelloFrameAsync(IUsbDuplexStream stream, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(helloProbeTimeout);
        var buffer = new byte[512];
        using var frame = new MemoryStream();
        var nonHelloLinesSeen = 0;
        try
        {
            var probeFrame = StrictUtf8.GetBytes("{\"protocol\":1,\"type\":\"hello\",\"data\":{}}\n");
            try
            {
                await stream.WriteAsync(probeFrame, timeout.Token).ConfigureAwait(false);
            }
            catch
            {
                // Ignore write failures on read-only probe streams
            }

            while (!timeout.Token.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    await Task.Delay(20, timeout.Token).ConfigureAwait(false);
                    continue;
                }
                for (var index = 0; index < read; index++)
                {
                    var value = buffer[index];
                    if (value == '\n')
                    {
                        var candidate = TryDecodeFrame(frame);
                        frame.SetLength(0);
                        if (string.IsNullOrWhiteSpace(candidate))
                        {
                            continue;
                        }

                        AgentDiagnosticLog.Write($"USB probe line: {candidate}");

                        if (IsValidProbeHello(candidate))
                        {
                            var leftover = index + 1 < read
                                ? buffer.AsSpan(index + 1, read - (index + 1)).ToArray()
                                : Array.Empty<byte>();
                            return new ProbeResult(candidate, leftover);
                        }

                        if (IsExplicitWrongRoleHello(candidate))
                        {
                            return null;
                        }

                        nonHelloLinesSeen++;
                        if ((nonHelloLinesSeen % 2) == 0)
                        {
                            try
                            {
                                await stream.WriteAsync(probeFrame, timeout.Token).ConfigureAwait(false);
                            }
                            catch
                            {
                                // Ignore write failures
                            }
                        }
                        if (nonHelloLinesSeen >= 25)
                        {
                            return null;
                        }

                        continue;
                    }

                    if (value == '\r') continue;
                    if (frame.Length >= 16 * 1024) return null;
                    frame.WriteByte(value);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        return null;
    }

    private sealed class PrefixedUsbDuplexStream(IUsbDuplexStream inner, byte[] prefix) : IUsbDuplexStream
    {
        private int offset;

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (offset < prefix.Length)
            {
                var count = Math.Min(prefix.Length - offset, buffer.Length);
                prefix.AsMemory(offset, count).CopyTo(buffer);
                offset += count;
                return count;
            }

            return await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
            => inner.WriteAsync(buffer, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private static bool IsExplicitWrongRoleHello(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        var parseResult = ProtocolSerializer.Parse(message);
        if (!parseResult.IsSuccess || parseResult.Envelope?.Type != ProtocolConstants.MessageTypes.Hello) return false;
        var hello = ProtocolSerializer.DeserializeData<HelloMessage>(parseResult.Envelope);
        return !string.Equals(hello.Role, "esp32", StringComparison.Ordinal);
    }

    private static string? TryDecodeFrame(MemoryStream frame)
    {
        if (frame.Length == 0) return null;
        try
        {
            return StrictUtf8.GetString(frame.GetBuffer(), 0, checked((int)frame.Length));
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool IsValidProbeHello(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        var parseResult = ProtocolSerializer.Parse(message);
        if (!parseResult.IsSuccess || parseResult.Envelope?.Type != ProtocolConstants.MessageTypes.Hello) return false;
        var hello = ProtocolSerializer.DeserializeData<HelloMessage>(parseResult.Envelope);
        return string.Equals(hello.Role, "esp32", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(hello.DeviceId);
    }

    private ValueTask HandleDeviceInfoAsync(DeviceInfoMessage info, CancellationToken cancellationToken)
    {
        state.UpdateDeviceInfo(info);
        return ValueTask.CompletedTask;
    }

    private void SetActiveSink(ActiveUsbSink sink)
    {
        lock (activeGate) activeSink = sink;
    }

    private void ClearActiveSink(ActiveUsbSink sink)
    {
        lock (activeGate)
        {
            if (ReferenceEquals(activeSink, sink)) activeSink = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var source = cancellation;
        cancellation = null;
        if (source is not null) await source.CancelAsync().ConfigureAwait(false);
        if (runTask is not null)
        {
            try { await runTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            runTask = null;
        }
        source?.Dispose();
    }

    public sealed class ActiveUsbSink(
        AgentState state,
        string coverCacheDirectory,
        Func<string, ValueTask> send,
        Action<ActiveUsbSink> setActiveSink) : IProtocolSink, IUsbIncomingObserver
    {
        private readonly UsbCoverSender coverSender = new(coverCacheDirectory);
        private readonly object gate = new();
        private HelloMessage? hello;
        private bool connected;
        private readonly SemaphoreSlim coverTransferGate = new(1, 1);
        private readonly SemaphoreSlim mediaFrameGate = new(1, 1);
        private string? sentCoverId;
        private string? currentCoverId;
        private bool requestsCachedCovers;

        public string? DeviceId => hello?.DeviceId;

        public async ValueTask SendTextAsync(string message, CancellationToken cancellationToken = default)
        {
            await send(message).ConfigureAwait(false);
            TrackOutgoing(message);
        }

        public async ValueTask SendUpdateAsync(string message, CancellationToken cancellationToken = default)
        {
            if (ProtocolSerializer.Parse(message).Envelope?.Type != ProtocolConstants.MessageTypes.MediaUpdate)
            {
                await SendTextAsync(message, cancellationToken).ConfigureAwait(false);
                return;
            }
            await mediaFrameGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                currentCoverId = TryReadMediaCoverId(message);
                await SendTextAsync(message, cancellationToken).ConfigureAwait(false);
                if (!requestsCachedCovers)
                    await TrySendCoverForMediaUpdateAsync(message, cancellationToken).ConfigureAwait(false);
            }
            finally { mediaFrameGate.Release(); }
        }

        public ValueTask SendRequestedCoverAsync(CoverRequestMessage request, CancellationToken cancellationToken = default)
        {
            if (!requestsCachedCovers || request.CoverId != currentCoverId) return ValueTask.CompletedTask;
            return TrySendCoverForMediaUpdateAsync(ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.MediaUpdate, null,
                new MediaUpdateMessage(null, null, null, false, 0, 0, request.CoverId, null)), cancellationToken);
        }

        public async ValueTask TrySendCoverForMediaUpdateAsync(string message, CancellationToken cancellationToken = default)
        {
            var coverId = TryReadMediaCoverId(message);
            if (string.IsNullOrWhiteSpace(coverId)) return;
            await coverTransferGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (coverId == sentCoverId) return;
                if (await coverSender.SendCoverAsync(coverId, this, cancellationToken).ConfigureAwait(false))
                {
                    sentCoverId = coverId;
                    AgentDiagnosticLog.Write("USB artwork transfer completed");
                }
            }
            finally { coverTransferGate.Release(); }
        }

        public void TrackIncoming(string message)
        {
            var parseResult = Protocol.ProtocolSerializer.Parse(message);
            if (!parseResult.IsSuccess) return;

            if (parseResult.Envelope?.Type == Protocol.ProtocolConstants.MessageTypes.Hello)
            {
                var incomingHello = Protocol.ProtocolSerializer.DeserializeData<HelloMessage>(parseResult.Envelope);
                lock (gate)
                {
                    hello = incomingHello;
                    requestsCachedCovers = incomingHello.Capabilities.Contains("cover-cache-request", StringComparer.Ordinal);
                    connected = false;
                    sentCoverId = null;
                }
            }
            else if (parseResult.Envelope?.Type == Protocol.ProtocolConstants.MessageTypes.DeviceInfo)
            {
                var info = Protocol.ProtocolSerializer.DeserializeData<DeviceInfoMessage>(parseResult.Envelope);
                if (info.Cover is not null && info.Cover.Status == 0)
                {
                    sentCoverId = null;
                }
            }
        }

        private void TrackOutgoing(string message)
        {
            HelloMessage? currentHello;
            lock (gate) currentHello = hello;
            if (currentHello is null) return;
            var parseResult = Protocol.ProtocolSerializer.Parse(message);
            if (parseResult.IsSuccess && parseResult.Envelope?.Type == Protocol.ProtocolConstants.MessageTypes.ConnectionState)
            {
                var connection = Protocol.ProtocolSerializer.DeserializeData<ConnectionStateMessage>(parseResult.Envelope);
                if (string.Equals(connection.State, "connected", StringComparison.Ordinal))
                {
                    var becameConnected = false;
                    lock (gate)
                    {
                        if (!connected)
                        {
                            connected = true;
                            becameConnected = true;
                        }
                    }

                    if (becameConnected)
                    {
                        setActiveSink(this);
                        state.MarkDeviceConnected(currentHello.DeviceId, currentHello.Name, "USB");
                        if (currentHello.DeviceInfo is not null) state.UpdateDeviceInfo(currentHello.DeviceInfo);
                    }
                }
            }
        }

        private static string? TryReadMediaCoverId(string message)
        {
            var parseResult = Protocol.ProtocolSerializer.Parse(message);
            if (!parseResult.IsSuccess || parseResult.Envelope?.Type != Protocol.ProtocolConstants.MessageTypes.MediaUpdate) return null;
            return Protocol.ProtocolSerializer.DeserializeData<MediaUpdateMessage>(parseResult.Envelope).CoverId;
        }
    }
}
