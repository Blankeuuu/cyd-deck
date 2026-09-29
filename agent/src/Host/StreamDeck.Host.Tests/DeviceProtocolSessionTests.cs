using System.Text.Json;
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
            new DeviceShortcutDefinition("calc", "Calculator", "calculator", "launch"),
            new DeviceShortcutDefinition("notes", "Notes", "note", "url")
        ]);
        var session = new DeviceProtocolSession(CreateDispatcher(), new AuthorizingDeviceAuthorizer(), sink, shortcuts: shortcuts);

        await session.HandleTextMessageAsync(CreateHello(), TestContext.Current.CancellationToken);

        var shortcutsMessage = sink.Messages.Single(message => message.Contains("\"type\":\"shortcuts.update\"", StringComparison.Ordinal));
        using var shortcutsDocument = JsonDocument.Parse(shortcutsMessage);
        var definitions = shortcutsDocument.RootElement.GetProperty("data").GetProperty("shortcuts");
        Assert.Equal(2, definitions.GetArrayLength());
        Assert.Equal("calc", definitions[0].GetProperty("id").GetString());
        Assert.Equal("Calculator", definitions[0].GetProperty("label").GetString());
        Assert.Equal("launch", definitions[0].GetProperty("actionType").GetString());
        Assert.Equal("notes", definitions[1].GetProperty("id").GetString());
        Assert.Equal("Notes", definitions[1].GetProperty("label").GetString());
        Assert.Equal("url", definitions[1].GetProperty("actionType").GetString());
        Assert.False(shortcutsMessage.Contains("executablePath", StringComparison.OrdinalIgnoreCase));
        Assert.False(shortcutsMessage.Contains("command", StringComparison.OrdinalIgnoreCase));
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
            new DeviceShortcutDefinition("calc", "Calculator", "calculator", "launch")
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

    private static string CreateHello()
    {
        return ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.Hello,
            requestId: null,
            new HelloMessage("CYD-A81F", "CYD", "esp32", []));
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
