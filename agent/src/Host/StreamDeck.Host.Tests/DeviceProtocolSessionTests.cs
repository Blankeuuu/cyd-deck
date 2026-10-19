using System.Text.Json;
using System.Text.Json.Nodes;
using StreamDeck.Audio;
using StreamDeck.Configuration;
using StreamDeck.Media;
using StreamDeck.Protocol;
using StreamDeck.Shortcuts;
using Xunit;

namespace StreamDeck.Host.Tests;

public sealed class DeviceProtocolSessionTests
{
    [Fact]
    public async Task HydrationSendsShortcutsBeforeAppearanceAndAudioBeforeConnected()
    {
        var sink = new MemoryProtocolSink();
        var session = new DeviceProtocolSession(CreateDispatcher(), new AuthorizingDeviceAuthorizer(), sink,
            appearance: new AppearanceConfiguration(), shortcuts: new DeviceShortcutConfiguration([]),
            initialAudioProvider: () => new AudioSnapshot(57, false),
            initialMediaSender: (target, token) => target.SendTextAsync(ProtocolStateMapper.CreateMediaUpdate(null), token));
        await session.HandleTextMessageAsync(CreateHello(), TestContext.Current.CancellationToken);
        var types = sink.Messages.Select(message => JsonDocument.Parse(message).RootElement.GetProperty("type").GetString()).ToArray();
        Assert.True(Array.IndexOf(types, "shortcuts.update") < Array.IndexOf(types, "device.settings"));
        Assert.True(Array.IndexOf(types, "audio.update") < Array.IndexOf(types, "media.update"));
        Assert.True(Array.IndexOf(types, "media.update") < Array.IndexOf(types, "device.status.request"));
        Assert.True(Array.IndexOf(types, "device.status.request") < Array.IndexOf(types, "connection.state"));
    }

    [Fact]
    public async Task CommandsAreRejectedBeforeAuthorization()
    {
        var sink = new MemoryProtocolSink();
        var session = new DeviceProtocolSession(CreateDispatcher(), new RejectingDeviceAuthorizer(), sink);

        await session.HandleTextMessageAsync(
            ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.AudioToggleMute, "a-1", payload: (object?)null),
            TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(sink.Messages.Single());
        Assert.Equal("ack", document.RootElement.GetProperty("type").GetString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("unauthorized", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AuthorizedHelloEnablesCommands()
    {
        var media = new FakeMediaController();
        var sink = new MemoryProtocolSink();
        var session = new DeviceProtocolSession(CreateDispatcher(media), new AuthorizingDeviceAuthorizer(), sink, bearerToken: "token");

        await session.HandleTextMessageAsync(CreateHello(), TestContext.Current.CancellationToken);
        await session.HandleTextMessageAsync(
            ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.MediaCommand, "m-1", new MediaCommandMessage(MediaCommand.PlayPause)),
            TestContext.Current.CancellationToken);

        Assert.Equal(MediaCommand.PlayPause, media.LastCommand);
        Assert.Contains(sink.Messages, message => message.Contains("\"type\":\"hello\"", StringComparison.Ordinal));
        Assert.Contains(sink.Messages, message => message.Contains("\"requestId\":\"m-1\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UsbHelloWithValidBearerTokenEchoesPairingTokenBeforeConnected()
    {
        var storagePath = Path.Combine(Path.GetTempPath(), $"streamdeck-session-reconnect-{Guid.NewGuid():N}.json");
        var service = new PairingApprovalService(storagePath);
        var firstSink = new MemoryProtocolSink();
        var firstSession = new DeviceProtocolSession(CreateDispatcher(), service, firstSink, remoteAddress: "USB");

        await firstSession.HandleTextMessageAsync(CreateHello(), TestContext.Current.CancellationToken);
        Assert.True(await service.ApproveAsync("CYD-A81F", TestContext.Current.CancellationToken));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        var tokenMessage = firstSink.Messages.Single(message => message.Contains("\"type\":\"pairing.token\"", StringComparison.Ordinal));
        using var tokenDocument = JsonDocument.Parse(tokenMessage);
        var token = tokenDocument.RootElement.GetProperty("data").GetProperty("token").GetString();

        var reconnectSink = new MemoryProtocolSink();
        var reconnectSession = new DeviceProtocolSession(CreateDispatcher(), new PairingApprovalService(storagePath), reconnectSink, remoteAddress: "USB");
        await reconnectSession.HandleTextMessageAsync(CreateHello(bearerToken: token), TestContext.Current.CancellationToken);

        var pairingTokenIndex = FindMessageIndex(reconnectSink.Messages, message => message.Contains("\"type\":\"pairing.token\"", StringComparison.Ordinal));
        var connectedIndex = FindMessageIndex(reconnectSink.Messages, message => message.Contains("\"type\":\"connection.state\"", StringComparison.Ordinal) && message.Contains("connected", StringComparison.Ordinal));
        Assert.True(pairingTokenIndex >= 0, "valid USB bearer reconnect echoes a pairing token");
        Assert.True(connectedIndex >= 0, "valid USB bearer reconnect sends connected state");
        Assert.True(pairingTokenIndex < connectedIndex, "USB pairing token echo must precede connected state");
        using var echoedTokenDocument = JsonDocument.Parse(reconnectSink.Messages[pairingTokenIndex]);
        Assert.Equal(token, echoedTokenDocument.RootElement.GetProperty("data").GetProperty("token").GetString());
    }

    [Fact]
    public async Task UsbHelloWithInvalidBearerTokenDoesNotEchoPairingTokenOrAuthorize()
    {
        var storagePath = Path.Combine(Path.GetTempPath(), $"streamdeck-session-invalid-reconnect-{Guid.NewGuid():N}.json");
        var service = new PairingApprovalService(storagePath);
        var firstSink = new MemoryProtocolSink();
        var firstSession = new DeviceProtocolSession(CreateDispatcher(), service, firstSink, remoteAddress: "USB");

        await firstSession.HandleTextMessageAsync(CreateHello(), TestContext.Current.CancellationToken);
        Assert.True(await service.ApproveAsync("CYD-A81F", TestContext.Current.CancellationToken));

        var reconnectSink = new MemoryProtocolSink();
        var reconnectSession = new DeviceProtocolSession(CreateDispatcher(), new PairingApprovalService(storagePath), reconnectSink, remoteAddress: "USB");
        await reconnectSession.HandleTextMessageAsync(CreateHello(bearerToken: "not-the-issued-token"), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(reconnectSink.Messages, message => message.Contains("\"type\":\"pairing.token\"", StringComparison.Ordinal));
        Assert.DoesNotContain(reconnectSink.Messages, message => message.Contains("\"type\":\"connection.state\"", StringComparison.Ordinal) && message.Contains("connected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WebSocketHelloWithValidBearerTokenDoesNotEchoPairingToken()
    {
        var storagePath = Path.Combine(Path.GetTempPath(), $"streamdeck-session-websocket-reconnect-{Guid.NewGuid():N}.json");
        var service = new PairingApprovalService(storagePath);
        var firstSink = new MemoryProtocolSink();
        var firstSession = new DeviceProtocolSession(CreateDispatcher(), service, firstSink, remoteAddress: "USB");

        await firstSession.HandleTextMessageAsync(CreateHello(), TestContext.Current.CancellationToken);
        Assert.True(await service.ApproveAsync("CYD-A81F", TestContext.Current.CancellationToken));
        var tokenMessage = firstSink.Messages.Single(message => message.Contains("\"type\":\"pairing.token\"", StringComparison.Ordinal));
        using var tokenDocument = JsonDocument.Parse(tokenMessage);
        var token = tokenDocument.RootElement.GetProperty("data").GetProperty("token").GetString();

        var reconnectSink = new MemoryProtocolSink();
        var reconnectSession = new DeviceProtocolSession(CreateDispatcher(), new PairingApprovalService(storagePath), reconnectSink, remoteAddress: "192.168.1.50");
        await reconnectSession.HandleTextMessageAsync(CreateHello(bearerToken: token), TestContext.Current.CancellationToken);

        Assert.Contains(reconnectSink.Messages, message => message.Contains("\"type\":\"connection.state\"", StringComparison.Ordinal) && message.Contains("connected", StringComparison.Ordinal));
        Assert.DoesNotContain(reconnectSink.Messages, message => message.Contains("\"type\":\"pairing.token\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthorizedHelloSendsInitialDeviceSettings()
    {
        var sink = new MemoryProtocolSink();
        var appearance = new AppearanceConfiguration { DimAfterSec = 86400, ScreenOffAfterSec = 86400 };
        var session = new DeviceProtocolSession(CreateDispatcher(), new AuthorizingDeviceAuthorizer(), sink, appearance: appearance);

        await session.HandleTextMessageAsync(CreateHello(), TestContext.Current.CancellationToken);

        var settingsMessage = sink.Messages.Single(message => message.Contains("\"type\":\"device.settings\"", StringComparison.Ordinal));
        using var settings = JsonDocument.Parse(settingsMessage);
        var data = settings.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("version").GetInt32());
        Assert.Equal("dark", data.GetProperty("theme").GetString());
        Assert.Equal("#7EAFE3", data.GetProperty("accent").GetString());
        Assert.Equal("#101216", data.GetProperty("background").GetString());
        Assert.Equal("rounded", data.GetProperty("buttonStyle").GetString());
        Assert.Equal(86400, data.GetProperty("dimAfterSec").GetInt32());
        Assert.Equal(86400, data.GetProperty("screenOffAfterSec").GetInt32());
        Assert.True(data.GetProperty("wakeOnTouch").GetBoolean());
    }

    [Fact]
    public async Task AuthorizedHelloSendsInitialAudioSnapshot()
    {
        var sink = new MemoryProtocolSink();
        var session = new DeviceProtocolSession(
            CreateDispatcher(),
            new AuthorizingDeviceAuthorizer(),
            sink,
            initialAudioProvider: () => new AudioSnapshot(73, false));

        await session.HandleTextMessageAsync(CreateHello(), TestContext.Current.CancellationToken);

        var audioMessage = sink.Messages.Single(message => message.Contains("\"type\":\"audio.update\"", StringComparison.Ordinal));
        using var audio = JsonDocument.Parse(audioMessage);
        Assert.Equal(73, audio.RootElement.GetProperty("data").GetProperty("volumePercent").GetInt32());
        Assert.False(audio.RootElement.GetProperty("data").GetProperty("muted").GetBoolean());
    }

    [Fact]
    public async Task AuthorizedHelloSendsInitialShortcutsUpdate()
    {
        var sink = new MemoryProtocolSink();
        var shortcuts = new DeviceShortcutConfiguration(
        [
            new DeviceShortcutDefinition("calc", "Calculator", "calculator", "both"),
            new DeviceShortcutDefinition("notes", "Notes", "note", "icon")
        ]);
        var session = new DeviceProtocolSession(CreateDispatcher(), new AuthorizingDeviceAuthorizer(), sink, shortcuts: shortcuts);

        await session.HandleTextMessageAsync(CreateHello(), TestContext.Current.CancellationToken);

        var shortcutsMessage = sink.Messages.Single(message => message.Contains("\"type\":\"shortcuts.update\"", StringComparison.Ordinal));
        using var shortcutsDocument = JsonDocument.Parse(shortcutsMessage);
        var definitions = shortcutsDocument.RootElement.GetProperty("data").GetProperty("shortcuts");
        Assert.Equal(2, definitions.GetArrayLength());
        Assert.Equal("calc", definitions[0].GetProperty("id").GetString());
        Assert.Equal("Calculator", definitions[0].GetProperty("label").GetString());
        Assert.Equal("calculator", definitions[0].GetProperty("icon").GetString());
        Assert.Equal("both", definitions[0].GetProperty("displayMode").GetString());
        Assert.Equal("notes", definitions[1].GetProperty("id").GetString());
        Assert.Equal("Notes", definitions[1].GetProperty("label").GetString());
        Assert.Equal("note", definitions[1].GetProperty("icon").GetString());
        Assert.Equal("icon", definitions[1].GetProperty("displayMode").GetString());
        Assert.False(shortcutsMessage.Contains("executablePath", StringComparison.OrdinalIgnoreCase));
        Assert.False(shortcutsMessage.Contains("command", StringComparison.OrdinalIgnoreCase));
        Assert.False(shortcutsMessage.Contains("actionType", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AuthorizedHelloSendsEmptyShortcutsUpdateAfterCatalogDeletion()
    {
        var sink = new MemoryProtocolSink();
        var session = new DeviceProtocolSession(
            CreateDispatcher(),
            new AuthorizingDeviceAuthorizer(),
            sink,
            shortcuts: new DeviceShortcutConfiguration([]));

        await session.HandleTextMessageAsync(CreateHello(), TestContext.Current.CancellationToken);

        var shortcutsMessage = sink.Messages.Single(message => message.Contains("\"type\":\"shortcuts.update\"", StringComparison.Ordinal));
        using var shortcutsDocument = JsonDocument.Parse(shortcutsMessage);
        var definitions = shortcutsDocument.RootElement.GetProperty("data").GetProperty("shortcuts");
        Assert.Equal(0, definitions.GetArrayLength());
    }

    [Fact]
    public async Task AuthorizedHelloSendsShortenedShortcutsUpdateAfterDeletion()
    {
        var sink = new MemoryProtocolSink();
        var shortcuts = new DeviceShortcutConfiguration(
        [
            new DeviceShortcutDefinition("calc", "Calculator", "calculator", "label")
        ]);
        var session = new DeviceProtocolSession(CreateDispatcher(), new AuthorizingDeviceAuthorizer(), sink, shortcuts: shortcuts);

        await session.HandleTextMessageAsync(CreateHello(), TestContext.Current.CancellationToken);

        var shortcutsMessage = sink.Messages.Single(message => message.Contains("\"type\":\"shortcuts.update\"", StringComparison.Ordinal));
        using var shortcutsDocument = JsonDocument.Parse(shortcutsMessage);
        var definitions = shortcutsDocument.RootElement.GetProperty("data").GetProperty("shortcuts");
        Assert.Equal(1, definitions.GetArrayLength());
        Assert.Equal("calc", definitions[0].GetProperty("id").GetString());
        Assert.False(shortcutsMessage.Contains("notes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShortcutsReportIsIgnoredBeforeAuthorization()
    {
        var handled = false;
        var sink = new MemoryProtocolSink();
        var session = new DeviceProtocolSession(
            CreateDispatcher(),
            new AuthorizingDeviceAuthorizer(),
            sink,
            shortcutsReportHandler: (_, _) =>
            {
                handled = true;
                return ValueTask.CompletedTask;
            });

        await session.HandleTextMessageAsync(CreateShortcutsReport("device-1", "Device shortcut", "launch"), TestContext.Current.CancellationToken);

        Assert.False(handled);
        using var document = JsonDocument.Parse(sink.Messages.Single());
        Assert.True(
            document.RootElement.GetProperty("type").GetString() is "ack" or "connection.state",
            "pre-authorization shortcuts.report must not be imported or dispatched");
    }

    [Fact]
    public async Task AuthorizedShortcutsReportInvokesPendingImportHandlerWithoutEchoingUpdate()
    {
        ShortcutsReportMessage? handled = null;
        var sink = new MemoryProtocolSink();
        var session = new DeviceProtocolSession(
            CreateDispatcher(),
            new AuthorizingDeviceAuthorizer(),
            sink,
            shortcutsReportHandler: (report, _) =>
            {
                handled = report;
                return ValueTask.CompletedTask;
            });

        await session.HandleTextMessageAsync(CreateHello(), TestContext.Current.CancellationToken);
        var messageCountAfterHello = sink.Messages.Count;

        await session.HandleTextMessageAsync(CreateShortcutsReport("device-1", "Device shortcut", "launch"), TestContext.Current.CancellationToken);

        Assert.NotNull(handled);
        Assert.Equal("device-1", handled.Shortcuts.Single().Id);
        Assert.Equal("Device shortcut", handled.Shortcuts.Single().Label);
        Assert.Equal("launch", handled.Shortcuts.Single().ActionType);
        Assert.Equal(messageCountAfterHello, sink.Messages.Count);
    }

    [Fact]
    public async Task InvalidJsonPublishesDegradedConnectionState()
    {
        var sink = new MemoryProtocolSink();
        var session = new DeviceProtocolSession(CreateDispatcher(), new AuthorizingDeviceAuthorizer(), sink);

        await session.HandleTextMessageAsync("{ nope", TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(sink.Messages.Single());
        Assert.Equal("connection.state", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("degraded", document.RootElement.GetProperty("data").GetProperty("state").GetString());
    }

    [Fact]
    public async Task UnsupportedProtocolVersionHelloOnlyPublishesDegradedConnectionState()
    {
        var sink = new MemoryProtocolSink();
        var session = new DeviceProtocolSession(CreateDispatcher(), new AuthorizingDeviceAuthorizer(), sink);
        var hello = JsonNode.Parse(CreateHello())!.AsObject();
        hello["protocol"] = 999;

        await session.HandleTextMessageAsync(hello.ToJsonString(), TestContext.Current.CancellationToken);

        Assert.Single(sink.Messages);
        using var document = JsonDocument.Parse(sink.Messages.Single());
        Assert.Equal("connection.state", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("degraded", document.RootElement.GetProperty("data").GetProperty("state").GetString());
        Assert.DoesNotContain(sink.Messages, message => message.Contains("\"type\":\"hello\"", StringComparison.Ordinal));
        Assert.DoesNotContain(sink.Messages, message => message.Contains("\"type\":\"pairing.token\"", StringComparison.Ordinal));
        Assert.DoesNotContain(sink.Messages, message => message.Contains("\"type\":\"connection.state\"", StringComparison.Ordinal) && message.Contains("connected", StringComparison.Ordinal));
        Assert.DoesNotContain(sink.Messages, message => message.Contains("\"type\":\"device.settings\"", StringComparison.Ordinal));
        Assert.DoesNotContain(sink.Messages, message => message.Contains("\"type\":\"shortcuts.update\"", StringComparison.Ordinal));
        Assert.DoesNotContain(sink.Messages, message => message.Contains("\"type\":\"audio.update\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeviceInfoIsIgnoredBeforeAuthorization()
    {
        DeviceInfoMessage? captured = null;
        var session = new DeviceProtocolSession(
            CreateDispatcher(),
            new RejectingDeviceAuthorizer(),
            new MemoryProtocolSink(),
            deviceInfoHandler: (info, _) =>
            {
                captured = info;
                return ValueTask.CompletedTask;
            });

        await session.HandleTextMessageAsync(CreateDeviceInfo(), TestContext.Current.CancellationToken);

        Assert.Null(captured);
    }

    [Fact]
    public async Task DeviceInfoUpdatesOnlyAfterAuthorization()
    {
        DeviceInfoMessage? captured = null;
        var session = new DeviceProtocolSession(
            CreateDispatcher(),
            new AuthorizingDeviceAuthorizer(),
            new MemoryProtocolSink(),
            deviceInfoHandler: (info, _) =>
            {
                captured = info;
                return ValueTask.CompletedTask;
            });

        await session.HandleTextMessageAsync(CreateHello(), TestContext.Current.CancellationToken);
        await session.HandleTextMessageAsync(CreateDeviceInfo(), TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("1.0.0", captured.FirmwareVersion);
        Assert.Equal("Lab", captured.Wifi.Ssid);
        Assert.Equal(64000, captured.Heap.LargestBlockBytes);
    }

    [Fact]
    public void HostCanCreateDeviceRecoveryCommands()
    {
        using var restart = JsonDocument.Parse(ProtocolStateMapper.CreateDeviceRestart("restart-1"));
        Assert.Equal("device.restart", restart.RootElement.GetProperty("type").GetString());
        Assert.Equal("restart-1", restart.RootElement.GetProperty("requestId").GetString());
        Assert.False(restart.RootElement.TryGetProperty("data", out _));

        using var reset = JsonDocument.Parse(ProtocolStateMapper.CreateDeviceFactoryReset("reset-1"));
        Assert.Equal("device.factoryReset", reset.RootElement.GetProperty("type").GetString());
        Assert.Equal("reset-1", reset.RootElement.GetProperty("requestId").GetString());
        Assert.False(reset.RootElement.TryGetProperty("data", out _));

        using var settings = JsonDocument.Parse(ProtocolStateMapper.CreateDeviceSettings(
            "settings-1",
            new DeviceSettingsMessage(
                1,
                "dark",
                "#D43B48",
                "#101216",
                "rounded",
                ["media", "pc", "shortcuts", "info"],
                ["media", "pc", "info"],
                70,
                300,
                20,
                1800,
                true)));
        Assert.Equal("device.settings", settings.RootElement.GetProperty("type").GetString());
        Assert.Equal("settings-1", settings.RootElement.GetProperty("requestId").GetString());
        Assert.Equal(1, settings.RootElement.GetProperty("data").GetProperty("version").GetInt32());
    }

    [Fact]
    public void MediaUpdateCarriesCoverIdAndAccentWithoutInlineImagePayload()
    {
        var json = ProtocolStateMapper.CreateMediaUpdate(
            new MediaSnapshot(
                "player",
                "Title",
                "Artist",
                "Album",
                MediaPlaybackState.Playing,
                new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(42), TimeSpan.Zero, TimeSpan.FromMinutes(3)),
                Thumbnail: null),
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "#112233");

        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("media.update", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", data.GetProperty("coverId").GetString());
        Assert.Equal("#112233", data.GetProperty("accent").GetString());
        Assert.False(data.TryGetProperty("cover", out _));
        Assert.False(data.TryGetProperty("thumbnail", out _));
        Assert.False(data.TryGetProperty("imageBase64", out _));
    }

    private static string CreateHello(string? bearerToken = null)
    {
        return ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.Hello,
            requestId: null,
            new HelloMessage("CYD-A81F", "CYD", "esp32", [], bearerToken));
    }

    private static int FindMessageIndex(IReadOnlyList<string> messages, Func<string, bool> predicate)
    {
        for (var index = 0; index < messages.Count; index++)
        {
            if (predicate(messages[index])) return index;
        }

        return -1;
    }

    private static string CreateDeviceInfo()
    {
        return ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.DeviceInfo,
            requestId: null,
            new DeviceInfoMessage(
                "1.0.0",
                12,
                new DeviceWifiInfo(true, "Lab", -55, "192.168.1.22"),
                "192.168.1.10",
                new DeviceStorageInfo(true, true, 1024, 512),
                new DeviceHeapInfo(100000, 80000, 64000)));
    }

    private static string CreateShortcutsReport(string id, string label, string actionType)
    {
        return $$"""
            {
              "protocol": 1,
              "type": "shortcuts.report",
              "data": {
                "shortcuts": [
                  { "id": "{{id}}", "label": "{{label}}", "actionType": "{{actionType}}" }
                ]
              }
            }
            """;
    }

    private static DeviceCommandDispatcher CreateDispatcher(IHostMediaController? mediaController = null)
    {
        return new DeviceCommandDispatcher(
            mediaController ?? new FakeMediaController(),
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

    private sealed class FakeMediaController : IHostMediaController
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
        public Task<bool> SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> ToggleMuteAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class FakeShortcutExecutor : IHostShortcutExecutor
    {
        public Task<bool> ExecuteAsync(string shortcutId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }
}
