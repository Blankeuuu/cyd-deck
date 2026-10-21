using System.Text.Json;
using StreamDeck.Audio;
using StreamDeck.Configuration;
using StreamDeck.Media;
using StreamDeck.Protocol;
using StreamDeck.Shortcuts;
using Xunit;

namespace StreamDeck.Host.Tests;

public sealed class DeviceProtocolSessionFrameIsolationTests
{
    [Fact]
    public async Task AuthorizedSession_MalformedShortcutsReport_DoesNotDegradeOrDropAuth_AndSubsequentCommandsSucceed()
    {
        var media = new RecordingMediaController();
        var sink = new RecordingProtocolSink();
        var loggedErrors = new List<string>();

        var session = new DeviceProtocolSession(
            CreateDispatcher(media),
            new AuthorizingDeviceAuthorizer(),
            sink,
            bearerToken: "token-123",
            sessionErrorHandler: err => loggedErrors.Add(err));

        var cancellationToken = TestContext.Current.CancellationToken;

        // 1. Authorized Hello
        await session.HandleTextMessageAsync(CreateHello("token-123"), cancellationToken);
        Assert.True(session.IsAuthorized);
        var messageCountAfterHello = sink.Messages.Count;

        // 2. Malformed shortcuts.report (e.g. empty slot placeholder invalid token as observed in physical CYD live repro)
        var malformedReport = """
            {
              "protocol": 1,
              "type": "shortcuts.report",
              "data": {
                "shortcuts": [
                  { "id": "__empty_slot_0", "label": "Empty", "actionType": "none" }
                ]
              }
            }
            """;
        await session.HandleTextMessageAsync(malformedReport, cancellationToken);

        // Verify: Not degraded, error callback invoked, auth retained
        Assert.True(session.IsAuthorized);
        Assert.NotEmpty(loggedErrors);
        var messagesAfterReport = sink.Messages.Skip(messageCountAfterHello).ToList();
        Assert.DoesNotContain(messagesAfterReport, m => m.Contains("\"state\":\"degraded\"", StringComparison.Ordinal));

        // 3. Subsequent Ping succeeds and receives Pong
        var pingUnixMs = 1710000000000L;
        var pingMessage = ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.Ping,
            requestId: null,
            new PingMessage(pingUnixMs));
        await session.HandleTextMessageAsync(pingMessage, cancellationToken);

        Assert.Contains(sink.Messages, m => m.Contains("\"type\":\"pong\"", StringComparison.Ordinal) && m.Contains(pingUnixMs.ToString(), StringComparison.Ordinal));

        // 4. Subsequent MediaCommand succeeds and receives ACK
        var commandMessage = """
            {
              "protocol": 1,
              "type": "media.command",
              "requestId": "media-req-42",
              "data": {
                "command": "playPause"
              }
            }
            """;
        await session.HandleTextMessageAsync(commandMessage, cancellationToken);

        Assert.Equal(MediaCommand.PlayPause, media.LastCommand);
        Assert.Contains(sink.Messages, m => m.Contains("\"type\":\"ack\"", StringComparison.Ordinal)
            && m.Contains("\"requestId\":\"media-req-42\"", StringComparison.Ordinal)
            && m.Contains("\"success\":true", StringComparison.Ordinal));

        // 5. Incoming ACK from device (e.g. device.settings ack) generates no reply (no ACK-of-ACK)
        var countBeforeAck = sink.Messages.Count;
        var incomingAck = ProtocolSerializer.SerializeAck("settings-abc-123", true);
        await session.HandleTextMessageAsync(incomingAck, cancellationToken);
        Assert.Equal(countBeforeAck, sink.Messages.Count);
    }

    [Fact]
    public async Task AuthorizedSession_MalformedPayloadWithSafeRequestId_SendsFailedAckWithoutDegraded()
    {
        var sink = new RecordingProtocolSink();
        var loggedErrors = new List<string>();

        var session = new DeviceProtocolSession(
            CreateDispatcher(),
            new AuthorizingDeviceAuthorizer(),
            sink,
            bearerToken: "token-123",
            sessionErrorHandler: err => loggedErrors.Add(err));

        var cancellationToken = TestContext.Current.CancellationToken;

        await session.HandleTextMessageAsync(CreateHello("token-123"), cancellationToken);
        var countAfterHello = sink.Messages.Count;

        // Frame with invalid payload shape (missing command) but with valid, safe requestId
        var invalidCommandWithReqId = """
            {
              "protocol": 1,
              "type": "media.command",
              "requestId": "safe-cmd-req-99",
              "data": {}
            }
            """;
        await session.HandleTextMessageAsync(invalidCommandWithReqId, cancellationToken);

        Assert.True(session.IsAuthorized);
        Assert.NotEmpty(loggedErrors);

        var newMessages = sink.Messages.Skip(countAfterHello).ToList();
        Assert.DoesNotContain(newMessages, m => m.Contains("\"state\":\"degraded\"", StringComparison.Ordinal));

        var ack = newMessages.FirstOrDefault(m => m.Contains("\"requestId\":\"safe-cmd-req-99\"", StringComparison.Ordinal));
        Assert.NotNull(ack);
        Assert.Contains("\"type\":\"ack\"", ack, StringComparison.Ordinal);
        Assert.Contains("\"success\":false", ack, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"invalidPayload\"", ack, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnauthorizedSession_MalformedFrame_SendsDegradedState()
    {
        var sink = new RecordingProtocolSink();
        var session = new DeviceProtocolSession(CreateDispatcher(), new RejectingDeviceAuthorizer(), sink);

        var cancellationToken = TestContext.Current.CancellationToken;
        await session.HandleTextMessageAsync("{\"protocol\":1,\"type\":\"invalid.junk\"}", cancellationToken);

        Assert.False(session.IsAuthorized);
        Assert.Contains(sink.Messages, m => m.Contains("\"state\":\"degraded\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthorizedSession_IncomingAckDoesNotTriggerUnsupportedMessageAck()
    {
        var sink = new RecordingProtocolSink();
        var session = new DeviceProtocolSession(CreateDispatcher(), new AuthorizingDeviceAuthorizer(), sink, bearerToken: "token-123");

        var cancellationToken = TestContext.Current.CancellationToken;
        await session.HandleTextMessageAsync(CreateHello("token-123"), cancellationToken);

        var countBeforeAck = sink.Messages.Count;
        var incomingAck = ProtocolSerializer.SerializeAck("settings-xyz", true);
        await session.HandleTextMessageAsync(incomingAck, cancellationToken);

        Assert.Equal(countBeforeAck, sink.Messages.Count);
        Assert.DoesNotContain(sink.Messages, m => m.Contains("unsupportedMessage", StringComparison.Ordinal));

        // Even if incoming ACK has non-standard shape with requestId, never reply with ACK-of-ACK
        var nonStandardAck = """{"protocol":1,"type":"ack","requestId":"settings-legacy","data":{"success":true}}""";
        await session.HandleTextMessageAsync(nonStandardAck, cancellationToken);
        Assert.Equal(countBeforeAck, sink.Messages.Count);
    }

    private static string CreateHello(string? bearerToken = null)
    {
        return ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.Hello,
            requestId: null,
            new HelloMessage("CYD-TEST", "CYD", "esp32", ["display", "shortcuts", "media"], bearerToken));
    }

    private static DeviceCommandDispatcher CreateDispatcher(IHostMediaController? mediaController = null)
    {
        return new DeviceCommandDispatcher(
            mediaController ?? new RecordingMediaController(),
            new FakeAudioController(),
            new FakeShortcutExecutor(),
            new RejectingSystemCommandExecutor());
    }

    private sealed class AuthorizingDeviceAuthorizer : IDeviceAuthorizer
    {
        public ValueTask<DeviceAuthorizationResult> AuthorizeAsync(
            HelloMessage hello,
            string? bearerToken,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(DeviceAuthorizationResult.Authorized);
        }
    }

    private sealed class RecordingMediaController : IHostMediaController
    {
        public MediaCommand? LastCommand { get; private set; }

        public Task<bool> ExecuteAsync(MediaCommand command, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeAudioController : IHostAudioController
    {
        public Task<bool> SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> ToggleMuteAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class FakeShortcutExecutor : IHostShortcutExecutor
    {
        public Task<bool> ExecuteAsync(string shortcutId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class RecordingProtocolSink : IProtocolSink
    {
        public List<string> Messages { get; } = [];

        public ValueTask SendTextAsync(string message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return ValueTask.CompletedTask;
        }
    }
}
