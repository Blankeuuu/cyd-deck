using System.Text.Json;
using StreamDeck.Protocol;
using Xunit;

namespace StreamDeck.Host.Tests;

public sealed class PairingApprovalServiceTests
{
    [Fact]
    public async Task UnknownHelloCreatesPendingDevice()
    {
        var service = CreateService(out _);

        var result = await service.AuthorizeAsync(CreateHello(), bearerToken: null, remoteAddress: "192.168.1.20:41000", TestContext.Current.CancellationToken);
        var pending = await service.ListPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DeviceAuthorizationStatus.PendingApproval, result.Status);
        var device = Assert.Single(pending);
        Assert.Equal("CYD-A81F", device.DeviceId);
        Assert.Equal("CYD", device.Name);
        Assert.Equal("192.168.1.20:41000", device.RemoteAddress);
    }

    [Fact]
    public async Task NonEsp32HelloDoesNotCreatePendingDevice()
    {
        var service = CreateService(out _);

        var result = await service.AuthorizeAsync(
            CreateHello(role: "agent"),
            bearerToken: null,
            remoteAddress: "192.168.1.20:41000",
            TestContext.Current.CancellationToken);
        var pending = await service.ListPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DeviceAuthorizationStatus.Rejected, result.Status);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task ApproveDeliversTokenOnceAndTokenAuthorizesNextHandshake()
    {
        var service = CreateService(out _);
        var media = new FakeMediaController();
        var sink = new MemoryProtocolSink();
        var session = new DeviceProtocolSession(CreateDispatcher(media), service, sink);

        await session.HandleTextMessageAsync(CreateHelloJson(), TestContext.Current.CancellationToken);
        Assert.True(await service.ApproveAsync("CYD-A81F", TestContext.Current.CancellationToken));
        await WaitForMessageAsync(sink, ProtocolConstants.MessageTypes.PairingToken);

        var tokenMessages = sink.Messages.Where(message => message.Contains("\"type\":\"pairing.token\"", StringComparison.Ordinal)).ToArray();
        var token = ReadToken(Assert.Single(tokenMessages));
        Assert.Equal(43, token.Length);

        await session.HandleTextMessageAsync(
            ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.MediaCommand, "m-1", new MediaCommandMessage(MediaCommand.Next)),
            TestContext.Current.CancellationToken);

        Assert.Equal(MediaCommand.Next, media.LastCommand);

        var secondSink = new MemoryProtocolSink();
        var secondMedia = new FakeMediaController();
        var secondSession = new DeviceProtocolSession(CreateDispatcher(secondMedia), service, secondSink, bearerToken: token);
        await secondSession.HandleTextMessageAsync(CreateHelloJson(), TestContext.Current.CancellationToken);
        await secondSession.HandleTextMessageAsync(
            ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.MediaCommand, "m-2", new MediaCommandMessage(MediaCommand.PlayPause)),
            TestContext.Current.CancellationToken);

        Assert.Equal(MediaCommand.PlayPause, secondMedia.LastCommand);
        Assert.DoesNotContain(secondSink.Messages, message => message.Contains("\"type\":\"pairing.token\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DuplicatePendingSessionCannotStealApprovedPairingToken()
    {
        var service = CreateService(out _);
        var legitimateSink = new MemoryProtocolSink();
        var duplicateSink = new MemoryProtocolSink();
        var legitimateSession = new DeviceProtocolSession(CreateDispatcher(), service, legitimateSink, remoteAddress: "192.168.1.20:41000");
        var duplicateSession = new DeviceProtocolSession(CreateDispatcher(), service, duplicateSink, remoteAddress: "192.168.1.99:41000");

        await legitimateSession.HandleTextMessageAsync(CreateHelloJson(), TestContext.Current.CancellationToken);
        await duplicateSession.HandleTextMessageAsync(CreateHelloJson(), TestContext.Current.CancellationToken);

        Assert.True(await service.ApproveAsync("CYD-A81F", TestContext.Current.CancellationToken));
        Assert.Null(service.TryConsumeUndeliveredToken("CYD-A81F"));
        await WaitForMessageAsync(legitimateSink, ProtocolConstants.MessageTypes.PairingToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Contains(legitimateSink.Messages, message => message.Contains("\"type\":\"pairing.token\"", StringComparison.Ordinal));
        Assert.DoesNotContain(duplicateSink.Messages, message => message.Contains("\"type\":\"pairing.token\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectBlocksCommands()
    {
        var service = CreateService(out _);
        var sink = new MemoryProtocolSink();
        var session = new DeviceProtocolSession(CreateDispatcher(), service, sink);

        await session.HandleTextMessageAsync(CreateHelloJson(), TestContext.Current.CancellationToken);
        Assert.True(await service.RejectAsync("CYD-A81F", TestContext.Current.CancellationToken));
        await session.HandleTextMessageAsync(
            ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.AudioToggleMute, "a-1", payload: (object?)null),
            TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(sink.Messages.Last());
        Assert.Equal("ack", document.RootElement.GetProperty("type").GetString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("unauthorized", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task WrongTokenDoesNotAuthorize()
    {
        var service = CreateService(out _);

        await service.AuthorizeAsync(CreateHello(), bearerToken: null, TestContext.Current.CancellationToken);
        Assert.True(await service.ApproveAsync("CYD-A81F", TestContext.Current.CancellationToken));

        var result = await service.AuthorizeAsync(CreateHello(), bearerToken: new string('a', 43), TestContext.Current.CancellationToken);

        Assert.Equal(DeviceAuthorizationStatus.Rejected, result.Status);
    }

    [Fact]
    public async Task ApprovedDeviceWithoutBearerCannotRecoverTokenAfterApprovalDeliveryWasLost()
    {
        var service = CreateService(out _);

        await service.AuthorizeAsync(CreateHello(), bearerToken: null, TestContext.Current.CancellationToken);
        Assert.True(await service.ApproveAsync("CYD-A81F", TestContext.Current.CancellationToken));
        Assert.NotNull(service.TryConsumeUndeliveredToken("CYD-A81F"));

        var wrongTokenResult = await service.AuthorizeAsync(CreateHello(), new string('a', 43), TestContext.Current.CancellationToken);
        Assert.Equal(DeviceAuthorizationStatus.Rejected, wrongTokenResult.Status);

        var media = new FakeMediaController();
        var sink = new MemoryProtocolSink();
        var session = new DeviceProtocolSession(CreateDispatcher(media), service, sink);

        await session.HandleTextMessageAsync(CreateHelloJson(), TestContext.Current.CancellationToken);
        await session.HandleTextMessageAsync(
            ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.MediaCommand, "m-1", new MediaCommandMessage(MediaCommand.PlayPause)),
            TestContext.Current.CancellationToken);

        Assert.Null(media.LastCommand);
        Assert.DoesNotContain(sink.Messages, message => message.Contains("\"type\":\"pairing.token\"", StringComparison.Ordinal));
        Assert.Contains(sink.Messages, message => message.Contains("\"type\":\"connection.state\"", StringComparison.Ordinal)
            && message.Contains("\"state\":\"degraded\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApprovedDeviceWithInvalidNonemptyBearerDoesNotRecoverOrAcceptDeviceInfo()
    {
        var service = CreateService(out _);
        DeviceInfoMessage? capturedInfo = null;

        await service.AuthorizeAsync(CreateHello(), bearerToken: null, TestContext.Current.CancellationToken);
        Assert.True(await service.ApproveAsync("CYD-A81F", TestContext.Current.CancellationToken));

        var sink = new MemoryProtocolSink();
        var session = new DeviceProtocolSession(
            CreateDispatcher(),
            service,
            sink,
            bearerToken: new string('a', 43),
            deviceInfoHandler: (info, _) =>
            {
                capturedInfo = info;
                return ValueTask.CompletedTask;
            });

        await session.HandleTextMessageAsync(CreateHelloJson(), TestContext.Current.CancellationToken);
        await session.HandleTextMessageAsync(CreateDeviceInfoJson(), TestContext.Current.CancellationToken);

        Assert.Null(capturedInfo);
        Assert.DoesNotContain(sink.Messages, message => message.Contains("\"type\":\"pairing.token\"", StringComparison.Ordinal));
        Assert.Contains(sink.Messages, message => message.Contains("\"state\":\"degraded\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApprovedTokenPersistsAndReloads()
    {
        var service = CreateService(out var storagePath);
        var sink = new MemoryProtocolSink();
        var session = new DeviceProtocolSession(CreateDispatcher(), service, sink);

        await session.HandleTextMessageAsync(CreateHelloJson(), TestContext.Current.CancellationToken);
        Assert.True(await service.ApproveAsync("CYD-A81F", TestContext.Current.CancellationToken));
        await WaitForMessageAsync(sink, ProtocolConstants.MessageTypes.PairingToken);
        var token = ReadToken(sink.Messages.Single(message => message.Contains("\"type\":\"pairing.token\"", StringComparison.Ordinal)));

        var storedJson = await File.ReadAllTextAsync(storagePath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(token, storedJson, StringComparison.Ordinal);

        var reloaded = CreateService(storagePath);
        var result = await reloaded.AuthorizeAsync(CreateHello(), token, TestContext.Current.CancellationToken);

        Assert.Equal(DeviceAuthorizationStatus.Authorized, result.Status);
    }

    [Fact]
    public async Task DefaultProtectorApprovesReloadsAndAuthorizesReconnect()
    {
        var service = CreateDefaultProtectorService(out var storagePath);
        var sink = new MemoryProtocolSink();
        var session = new DeviceProtocolSession(CreateDispatcher(), service, sink);

        await session.HandleTextMessageAsync(CreateHelloJson(), TestContext.Current.CancellationToken);
        Assert.True(await service.ApproveAsync("CYD-A81F", TestContext.Current.CancellationToken));
        await WaitForMessageAsync(sink, ProtocolConstants.MessageTypes.PairingToken);
        var token = ReadToken(sink.Messages.Single(message => message.Contains("\"type\":\"pairing.token\"", StringComparison.Ordinal)));

        var storedJson = await File.ReadAllTextAsync(storagePath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(token, storedJson, StringComparison.Ordinal);

        var reloaded = CreateDefaultProtectorService(storagePath);
        var media = new FakeMediaController();
        var reconnectSession = new DeviceProtocolSession(CreateDispatcher(media), reloaded, new MemoryProtocolSink(), bearerToken: token);
        await reconnectSession.HandleTextMessageAsync(CreateHelloJson(), TestContext.Current.CancellationToken);
        await reconnectSession.HandleTextMessageAsync(
            ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.MediaCommand, "m-1", new MediaCommandMessage(MediaCommand.PlayPause)),
            TestContext.Current.CancellationToken);

        Assert.Equal(MediaCommand.PlayPause, media.LastCommand);
    }

    [Fact]
    public async Task CommandsRemainBlockedBeforeApproval()
    {
        var service = CreateService(out _);
        var media = new FakeMediaController();
        var sink = new MemoryProtocolSink();
        var session = new DeviceProtocolSession(CreateDispatcher(media), service, sink);

        await session.HandleTextMessageAsync(CreateHelloJson(), TestContext.Current.CancellationToken);
        await session.HandleTextMessageAsync(
            ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.MediaCommand, "m-1", new MediaCommandMessage(MediaCommand.Next)),
            TestContext.Current.CancellationToken);

        Assert.Null(media.LastCommand);
        using var document = JsonDocument.Parse(sink.Messages.Last());
        Assert.Equal("ack", document.RootElement.GetProperty("type").GetString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("unauthorized", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ForgetRemovesDeviceTokenAndPendingStateButPreservesOtherDevices()
    {
        var service = CreateService(out var storagePath);
        var sink = new MemoryProtocolSink();
        var session = new DeviceProtocolSession(CreateDispatcher(), service, sink);

        await session.HandleTextMessageAsync(CreateHelloJson(), TestContext.Current.CancellationToken);
        Assert.True(await service.ApproveAsync("CYD-A81F", TestContext.Current.CancellationToken));
        await WaitForMessageAsync(sink, ProtocolConstants.MessageTypes.PairingToken);
        var forgottenToken = ReadToken(sink.Messages.Single(message => message.Contains("\"type\":\"pairing.token\"", StringComparison.Ordinal)));

        await service.AuthorizeAsync(CreateHello(deviceId: "CYD-OTHER", name: "Other CYD"), bearerToken: null, TestContext.Current.CancellationToken);
        Assert.True(await service.ApproveAsync("CYD-OTHER", TestContext.Current.CancellationToken));

        await service.AuthorizeAsync(CreateHello(deviceId: "CYD-PENDING", name: "Pending CYD"), bearerToken: null, TestContext.Current.CancellationToken);

        Assert.True(await service.ForgetAsync("CYD-A81F", TestContext.Current.CancellationToken));

        var pending = await service.ListPendingAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(pending, device => device.DeviceId == "CYD-A81F");
        Assert.Contains(pending, device => device.DeviceId == "CYD-PENDING");

        var storedJson = await File.ReadAllTextAsync(storagePath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("CYD-A81F", storedJson, StringComparison.Ordinal);
        Assert.Contains("CYD-OTHER", storedJson, StringComparison.Ordinal);
        Assert.Contains("CYD-PENDING", storedJson, StringComparison.Ordinal);

        var previousTokenResult = await service.AuthorizeAsync(CreateHello(), forgottenToken, TestContext.Current.CancellationToken);
        Assert.NotEqual(DeviceAuthorizationStatus.Authorized, previousTokenResult.Status);
    }

    private static PairingApprovalService CreateService(out string storagePath)
    {
        storagePath = Path.Combine(Path.GetTempPath(), "stream-deck-host-tests", Guid.NewGuid().ToString("N"), "pairing.json");
        return CreateService(storagePath);
    }

    private static PairingApprovalService CreateService(string storagePath)
    {
        return new PairingApprovalService(
            storagePath,
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero)),
            new TestPairingTokenProtector());
    }

    private static PairingApprovalService CreateDefaultProtectorService(out string storagePath)
    {
        storagePath = Path.Combine(Path.GetTempPath(), "stream-deck-host-tests", Guid.NewGuid().ToString("N"), "pairing.json");
        return CreateDefaultProtectorService(storagePath);
    }

    private static PairingApprovalService CreateDefaultProtectorService(string storagePath)
    {
        return new PairingApprovalService(
            storagePath,
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero)));
    }

    private static HelloMessage CreateHello(string role = "esp32", string deviceId = "CYD-A81F", string name = "CYD")
    {
        return new HelloMessage(deviceId, name, role, []);
    }

    private static string CreateHelloJson()
    {
        return ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.Hello, requestId: null, CreateHello());
    }

    private static string CreateDeviceInfoJson()
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

    private static string ReadToken(string message)
    {
        using var document = JsonDocument.Parse(message);
        return document.RootElement.GetProperty("data").GetProperty("token").GetString()!;
    }

    private static async Task WaitForMessageAsync(MemoryProtocolSink sink, string messageType)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (sink.Messages.Any(message => message.Contains($"\"type\":\"{messageType}\"", StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Timed out waiting for {messageType}.");
    }

    private static DeviceCommandDispatcher CreateDispatcher(IHostMediaController? mediaController = null)
    {
        return new DeviceCommandDispatcher(
            mediaController ?? new FakeMediaController(),
            new FakeAudioController(),
            new FakeShortcutExecutor(),
            new RejectingSystemCommandExecutor());
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

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }

    private sealed class TestPairingTokenProtector : IPairingTokenProtector
    {
        public string Protect(string token)
        {
            var bytes = token.Select(character => (byte)(character ^ 0x5a)).ToArray();
            return Convert.ToBase64String(bytes);
        }

        public string Unprotect(string protectedToken)
        {
            var bytes = Convert.FromBase64String(protectedToken);
            return new string(bytes.Select(value => (char)(value ^ 0x5a)).ToArray());
        }
    }
}
