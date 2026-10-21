using System.Text;
using StreamDeck.Audio;
using StreamDeck.Host;
using StreamDeck.Media;
using StreamDeck.Protocol;
using StreamDeck.Shortcuts;
using StreamDeck.WindowsAgent;
using StreamDeck.WindowsAgent.Runtime;

internal static class UsbTransportRecoveryTests
{
    public static async Task RunAsync()
    {
        await EofTerminatesAsync();
        await ReadFailurePropagatesAsync();
        await DiagnosticsTraceGatedAsync();
        await FrameOverflowDegradesConnectionAsync();
        await InvalidUtf8DegradesConnectionAsync();
        await IncomingProtocolParseErrorTracesErrorAsync();
        await AuthorizedSessionIsolatesInvalidReportAndPreservesCommandsAsync();
        await AuthorizedSessionDrainsOverflowAndPreservesNextFrameAsync();
        await AuthorizedSessionRecoversFromInvalidUtf8AndPreservesNextFrameAsync();
        Console.WriteLine("PASS USB EOF and read failure release the reconnect loop");
        Console.WriteLine("PASS USB authorized frame isolation and incoming ACK regression");
    }

    private static UsbLineProtocolTransport CreateTransport(IUsbDuplexStream stream, List<string>? outgoingMessages = null)
    {
        var sink = new UsbProtocolSink((msg, _) =>
        {
            outgoingMessages?.Add(msg);
            return ValueTask.CompletedTask;
        });
        return new UsbLineProtocolTransport(stream, new DeviceProtocolSession(null!, null!, sink), sink);
    }

    private static async Task EofTerminatesAsync()
    {
        await using var transport = CreateTransport(new ClosedStream());
        using var cancellation = new CancellationTokenSource();
        var run = transport.RunAsync(cancellation.Token);
        try
        {
            await run.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch (TimeoutException)
        {
            cancellation.Cancel();
            try { await run; } catch (OperationCanceledException) { }
            throw new InvalidOperationException("USB EOF must terminate RunAsync so activeSink is cleared and discovery resumes.");
        }
    }

    private static async Task ReadFailurePropagatesAsync()
    {
        await using var transport = CreateTransport(new ClosedStream(fail: true));
        try { await transport.RunAsync(CancellationToken.None); }
        catch (IOException error) when (error.Message == "simulated detach") { return; }
        throw new InvalidOperationException("USB read failure must propagate to the reconnect loop.");
    }

    private static async Task DiagnosticsTraceGatedAsync()
    {
        var originalEnv = Environment.GetEnvironmentVariable("CYD_DECK_DIAGNOSTICS");
        try
        {
            Environment.SetEnvironmentVariable("CYD_DECK_DIAGNOSTICS", "1");
            var written = new List<string>();
            var stream = new MemoryTestStream(written);
            await using (var transport = CreateTransport(stream))
            {
                var secretPayload = "{\"protocol\":1,\"type\":\"pairing.token\",\"requestId\":\"req-123\",\"data\":{\"token\":\"SUPER_SECRET_TOKEN_VALUE\"}}";
                await transport.SendFrameAsync(secretPayload, CancellationToken.None);
            }

            var recentLines = AgentDiagnosticLog.ReadRecentLines();
            var matchingLine = recentLines.LastOrDefault(l => l.Contains("usb_tx type=pairing.token"));
            if (matchingLine is null)
            {
                throw new InvalidOperationException("Expected usb_tx diagnostic trace when CYD_DECK_DIAGNOSTICS=1.");
            }
            if (matchingLine.Contains("SUPER_SECRET_TOKEN_VALUE"))
            {
                throw new InvalidOperationException("Diagnostic trace must never contain secrets or payload.");
            }
            if (!matchingLine.Contains("req=req-123"))
            {
                throw new InvalidOperationException("Diagnostic trace must contain sanitized requestId.");
            }

            // Gated test: disable diagnostics and verify no new trace emitted
            Environment.SetEnvironmentVariable("CYD_DECK_DIAGNOSTICS", "0");
            var countBefore = AgentDiagnosticLog.ReadRecentLines().Count;
            await using (var transportDisabled = CreateTransport(stream))
            {
                await transportDisabled.SendFrameAsync("{\"protocol\":1,\"type\":\"ping\",\"requestId\":\"req-456\"}", CancellationToken.None);
            }
            var countAfter = AgentDiagnosticLog.ReadRecentLines().Count;
            if (countAfter > countBefore)
            {
                var lastLine = AgentDiagnosticLog.ReadRecentLines().Last();
                if (lastLine.Contains("req-456"))
                {
                    throw new InvalidOperationException("Diagnostic trace must be suppressed when CYD_DECK_DIAGNOSTICS=0.");
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("CYD_DECK_DIAGNOSTICS", originalEnv);
        }
    }

    private static async Task FrameOverflowDegradesConnectionAsync()
    {
        var outgoing = new List<string>();
        var overflowBytes = new byte[17 * 1024];
        Array.Fill(overflowBytes, (byte)'X');
        overflowBytes[^1] = (byte)'\n';

        var stream = new SequenceStream([overflowBytes]);
        await using var transport = CreateTransport(stream, outgoing);

        await transport.RunAsync(CancellationToken.None);

        if (!outgoing.Any(m => m.Contains("\"state\":\"degraded\"") && m.Contains("USB frame is too large.")))
        {
            throw new InvalidOperationException("Frame overflow must emit degraded connection state.");
        }
    }

    private static async Task InvalidUtf8DegradesConnectionAsync()
    {
        var outgoing = new List<string>();
        // Invalid UTF-8 sequence (0xC0 0xAF is overlong encoding of slash)
        var invalidUtf8 = new byte[] { 0xC0, 0xAF, (byte)'\n' };

        var stream = new SequenceStream([invalidUtf8]);
        await using var transport = CreateTransport(stream, outgoing);

        await transport.RunAsync(CancellationToken.None);

        if (!outgoing.Any(m => m.Contains("\"state\":\"degraded\"") && m.Contains("USB frame is not valid UTF-8.")))
        {
            throw new InvalidOperationException("Invalid UTF-8 must emit degraded connection state.");
        }
    }

    private static async Task IncomingProtocolParseErrorTracesErrorAsync()
    {
        var outgoing = new List<string>();
        // Diagnostic event with invalid correlation (contains space/special characters rejected by schema)
        var invalidProtoFrame = Encoding.UTF8.GetBytes("{\"protocol\":1,\"type\":\"diagnostic.event\",\"data\":{\"stage\":\"bad stage\",\"correlation\":\"\",\"deviceMs\":10,\"transport\":\"USB\"}}\n");

        var stream = new SequenceStream([invalidProtoFrame]);
        await using var transport = CreateTransport(stream, outgoing);

        await transport.RunAsync(CancellationToken.None);

        if (!outgoing.Any(m => m.Contains("\"state\":\"degraded\"")))
        {
            throw new InvalidOperationException("Protocol schema parse error must emit degraded connection state.");
        }
    }

    private static async Task AuthorizedSessionIsolatesInvalidReportAndPreservesCommandsAsync()
    {
        var outgoing = new List<string>();
        var mediaController = new TestMediaController();
        var dispatcher = new DeviceCommandDispatcher(
            mediaController,
            new TestAudioController(),
            new TestShortcutExecutor(),
            new RejectingSystemCommandExecutor());

        var sink = new UsbProtocolSink((msg, _) =>
        {
            outgoing.Add(msg);
            return ValueTask.CompletedTask;
        });

        var session = new DeviceProtocolSession(
            dispatcher,
            new TestAuthorizer(),
            sink,
            bearerToken: "test-auth-token");

        // Sequence of frames:
        // 1. Authorized hello
        var helloFrame = Encoding.UTF8.GetBytes(ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.Hello,
            requestId: null,
            new HelloMessage("CYD-A81F", "CYD", "esp32", ["display", "shortcuts", "media"], "test-auth-token")) + "\n");

        // 2. Malformed shortcuts.report with empty slot placeholder invalid token (physical CYD repro)
        var invalidReportFrame = Encoding.UTF8.GetBytes("""
            {"protocol":1,"type":"shortcuts.report","data":{"shortcuts":[{"id":"__empty_slot_0","label":"Slot 0","actionType":"none"}]}}
            """ + "\n");

        // 3. Ping
        var pingFrame = Encoding.UTF8.GetBytes(ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.Ping,
            requestId: null,
            new PingMessage(99887766L)) + "\n");

        // 4. Media command
        var mediaCmdFrame = Encoding.UTF8.GetBytes(ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.MediaCommand,
            requestId: "cmd-rec-1",
            new MediaCommandMessage(MediaCommand.PlayPause)) + "\n");

        // 5. Incoming ACK from device (e.g. device.settings ack)
        var incomingAckFrame = Encoding.UTF8.GetBytes(ProtocolSerializer.SerializeAck("settings-init-1", true) + "\n");

        var stream = new SequenceStream([helloFrame, invalidReportFrame, pingFrame, mediaCmdFrame, incomingAckFrame]);
        await using var transport = new UsbLineProtocolTransport(stream, session, sink);

        await transport.RunAsync(CancellationToken.None);

        if (!session.IsAuthorized)
        {
            throw new InvalidOperationException("Session must remain authorized after malformed report.");
        }

        if (outgoing.Any(m => m.Contains("\"state\":\"degraded\"")))
        {
            throw new InvalidOperationException("Authorized session must not emit connection.state degraded on malformed report.");
        }

        if (!outgoing.Any(m => m.Contains("\"type\":\"pong\"") && m.Contains("99887766")))
        {
            throw new InvalidOperationException("Subsequent ping must be answered with pong.");
        }

        if (mediaController.ExecutedCommand != MediaCommand.PlayPause)
        {
            throw new InvalidOperationException("Subsequent media command must be executed.");
        }

        if (!outgoing.Any(m => m.Contains("\"type\":\"ack\"") && m.Contains("\"requestId\":\"cmd-rec-1\"") && m.Contains("\"success\":true")))
        {
            throw new InvalidOperationException("Subsequent media command must receive success ACK.");
        }

        // Verify incoming ACK did not trigger a reply ACK (no ACK-of-ACK)
        if (outgoing.Any(m => m.Contains("\"requestId\":\"settings-init-1\"")))
        {
            throw new InvalidOperationException("Incoming device ACK must not generate a response ACK.");
        }
    }

    private static async Task AuthorizedSessionDrainsOverflowAndPreservesNextFrameAsync()
    {
        var outgoing = new List<string>();
        var sink = new UsbProtocolSink((msg, _) =>
        {
            outgoing.Add(msg);
            return ValueTask.CompletedTask;
        });

        var session = new DeviceProtocolSession(
            new DeviceCommandDispatcher(new TestMediaController(), new TestAudioController(), new TestShortcutExecutor(), new RejectingSystemCommandExecutor()),
            new TestAuthorizer(),
            sink,
            bearerToken: "test-auth-token");

        var helloFrame = Encoding.UTF8.GetBytes(ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.Hello,
            requestId: null,
            new HelloMessage("CYD-A81F", "CYD", "esp32", ["display"], "test-auth-token")) + "\n");

        var overflowBytes = new byte[17 * 1024];
        Array.Fill(overflowBytes, (byte)'Y');
        overflowBytes[^1] = (byte)'\n';

        var pingFrame = Encoding.UTF8.GetBytes(ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.Ping,
            requestId: null,
            new PingMessage(12345678L)) + "\n");

        var stream = new SequenceStream([helloFrame, overflowBytes, pingFrame]);
        await using var transport = new UsbLineProtocolTransport(stream, session, sink);

        await transport.RunAsync(CancellationToken.None);

        if (!session.IsAuthorized)
        {
            throw new InvalidOperationException("Authorized session must remain authorized across overflow drain.");
        }

        if (outgoing.Any(m => m.Contains("\"state\":\"degraded\"")))
        {
            throw new InvalidOperationException("Authorized session must not emit degraded connection state on overflow.");
        }

        if (!outgoing.Any(m => m.Contains("\"type\":\"pong\"") && m.Contains("12345678")))
        {
            throw new InvalidOperationException("Frame immediately following overflow drain must be processed.");
        }
    }

    private static async Task AuthorizedSessionRecoversFromInvalidUtf8AndPreservesNextFrameAsync()
    {
        var outgoing = new List<string>();
        var sink = new UsbProtocolSink((msg, _) =>
        {
            outgoing.Add(msg);
            return ValueTask.CompletedTask;
        });

        var session = new DeviceProtocolSession(
            new DeviceCommandDispatcher(new TestMediaController(), new TestAudioController(), new TestShortcutExecutor(), new RejectingSystemCommandExecutor()),
            new TestAuthorizer(),
            sink,
            bearerToken: "test-auth-token");

        var helloFrame = Encoding.UTF8.GetBytes(ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.Hello,
            requestId: null,
            new HelloMessage("CYD-A81F", "CYD", "esp32", ["display"], "test-auth-token")) + "\n");

        var invalidUtf8 = new byte[] { 0xC0, 0xAF, (byte)'\n' };

        var pingFrame = Encoding.UTF8.GetBytes(ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.Ping,
            requestId: null,
            new PingMessage(87654321L)) + "\n");

        var stream = new SequenceStream([helloFrame, invalidUtf8, pingFrame]);
        await using var transport = new UsbLineProtocolTransport(stream, session, sink);

        await transport.RunAsync(CancellationToken.None);

        if (!session.IsAuthorized)
        {
            throw new InvalidOperationException("Authorized session must remain authorized across invalid UTF-8 frame.");
        }

        if (outgoing.Any(m => m.Contains("\"state\":\"degraded\"")))
        {
            throw new InvalidOperationException("Authorized session must not emit degraded connection state on invalid UTF-8.");
        }

        if (!outgoing.Any(m => m.Contains("\"type\":\"pong\"") && m.Contains("87654321")))
        {
            throw new InvalidOperationException("Frame immediately following invalid UTF-8 must be processed.");
        }
    }

    private sealed class TestAuthorizer : IDeviceAuthorizer
    {
        public ValueTask<DeviceAuthorizationResult> AuthorizeAsync(HelloMessage hello, string? bearerToken, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(DeviceAuthorizationResult.Authorized);
    }

    private sealed class TestMediaController : IHostMediaController
    {
        public MediaCommand? ExecutedCommand { get; private set; }
        public Task<bool> ExecuteAsync(MediaCommand command, CancellationToken cancellationToken = default)
        {
            ExecutedCommand = command;
            return Task.FromResult(true);
        }
    }

    private sealed class TestAudioController : IHostAudioController
    {
        public Task<bool> SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> ToggleMuteAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class TestShortcutExecutor : IHostShortcutExecutor
    {
        public Task<bool> ExecuteAsync(string shortcutId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class ClosedStream(bool fail = false) : IUsbDuplexStream
    {
        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token)
            => fail ? ValueTask.FromException<int>(new IOException("simulated detach")) : ValueTask.FromResult(0);
        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SequenceStream : IUsbDuplexStream
    {
        private readonly MemoryStream source = new();

        public SequenceStream(IEnumerable<byte[]> chunks)
        {
            foreach (var chunk in chunks) source.Write(chunk);
            source.Position = 0;
        }

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token)
        {
            var read = source.Read(buffer.Span);
            return ValueTask.FromResult(read);
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync()
        {
            source.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MemoryTestStream(List<string> written) : IUsbDuplexStream
    {
        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token) => ValueTask.FromResult(0);

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token)
        {
            written.Add(Encoding.UTF8.GetString(buffer.Span));
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
