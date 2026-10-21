using System.Text.Json;
using StreamDeck.Protocol;
using Xunit;

namespace StreamDeck.Host.Tests;

public sealed class DeviceCommandDispatcherTests
{
    [Fact]
    public async Task VolumeReplyContainsObservedSnapshotRevisionAndRequestId()
    {
        var audio = new FakeAudioController { Snapshot = new StreamDeck.Audio.AudioSnapshot(80, false) { Revision = 7 } };
        var sink = new MemoryProtocolSink();
        var envelope = ProtocolSerializer.Parse(ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.AudioSetVolume, "volume-7", new AudioSetVolumeMessage(80))).Envelope!;
        await CreateDispatcher(audioController: audio).DispatchAsync(envelope, sink, TestContext.Current.CancellationToken);
        Assert.Equal(2, sink.Messages.Count);
        using var update = JsonDocument.Parse(sink.Messages[1]);
        Assert.Equal("volume-7", update.RootElement.GetProperty("requestId").GetString());
        Assert.Equal(7, update.RootElement.GetProperty("data").GetProperty("revision").GetInt64());
        Assert.Equal(80, update.RootElement.GetProperty("data").GetProperty("volumePercent").GetInt32());
    }
    [Fact]
    public async Task MediaCommandIsAcknowledgedAfterControllerSucceeds()
    {
        var media = new FakeMediaController();
        var dispatcher = CreateDispatcher(mediaController: media);
        var sink = new MemoryProtocolSink();
        var envelope = ProtocolSerializer.Parse(ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.MediaCommand,
            "m-1",
            new MediaCommandMessage(MediaCommand.Next))).Envelope!;

        await dispatcher.DispatchAsync(envelope, sink, TestContext.Current.CancellationToken);

        Assert.Equal(MediaCommand.Next, media.LastCommand);
        using var document = JsonDocument.Parse(sink.Messages.Single());
        Assert.Equal("ack", document.RootElement.GetProperty("type").GetString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task FailedShortcutReturnsFailedAck()
    {
        var shortcuts = new FakeShortcutExecutor { Result = false };
        var dispatcher = CreateDispatcher(shortcutExecutor: shortcuts);
        var sink = new MemoryProtocolSink();
        var envelope = ProtocolSerializer.Parse(ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.ShortcutExecute,
            "s-1",
            new ShortcutExecuteMessage("missing"))).Envelope!;

        await dispatcher.DispatchAsync(envelope, sink, TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(sink.Messages.Single());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("commandFailed", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task PingReturnsPongWithReceivedTimestamp()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));
        var dispatcher = CreateDispatcher(timeProvider: time);
        var sink = new MemoryProtocolSink();
        var envelope = ProtocolSerializer.Parse(ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.Ping,
            requestId: null,
            new PingMessage(123))).Envelope!;

        await dispatcher.DispatchAsync(envelope, sink, TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(sink.Messages.Single());
        var root = document.RootElement;
        Assert.Equal("pong", root.GetProperty("type").GetString());
        Assert.Equal(123, root.GetProperty("data").GetProperty("sentAtUnixMs").GetInt64());
        Assert.Equal(time.GetUtcNow().ToUnixTimeMilliseconds(), root.GetProperty("data").GetProperty("receivedAtUnixMs").GetInt64());
    }

    private static DeviceCommandDispatcher CreateDispatcher(
        IHostMediaController? mediaController = null,
        IHostAudioController? audioController = null,
        IHostShortcutExecutor? shortcutExecutor = null,
        IHostSystemCommandExecutor? systemCommandExecutor = null,
        TimeProvider? timeProvider = null)
    {
        return new DeviceCommandDispatcher(
            mediaController ?? new FakeMediaController(),
            audioController ?? new FakeAudioController(),
            shortcutExecutor ?? new FakeShortcutExecutor(),
            systemCommandExecutor ?? new RejectingSystemCommandExecutor(),
            timeProvider);
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
        public StreamDeck.Audio.AudioSnapshot? Snapshot { get; init; }
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
        public bool Result { get; init; } = true;

        public Task<bool> ExecuteAsync(string shortcutId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
