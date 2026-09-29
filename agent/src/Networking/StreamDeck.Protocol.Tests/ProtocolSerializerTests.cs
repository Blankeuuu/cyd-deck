using System.Text.Json;
using StreamDeck.Protocol;
using Xunit;

namespace StreamDeck.Protocol.Tests;

public sealed class ProtocolSerializerTests
{
    [Fact]
    public void SerializesVersionedEnvelopeWithCamelCasePayload()
    {
        var json = ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.MediaCommand,
            "42",
            new MediaCommandMessage(MediaCommand.Next));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("protocol").GetInt32());
        Assert.Equal("media.command", root.GetProperty("type").GetString());
        Assert.Equal("42", root.GetProperty("requestId").GetString());
        Assert.Equal("next", root.GetProperty("data").GetProperty("command").GetString());
    }

    [Fact]
    public void ParsesKnownEnvelopeAndTypedPayload()
    {
        var json = """
            {
              "protocol": 1,
              "type": "audio.setVolume",
              "requestId": "vol-1",
              "data": { "volumePercent": 42 }
            }
            """;

        var result = ProtocolSerializer.Parse(json);

        Assert.True(result.IsSuccess);
        var payload = ProtocolSerializer.DeserializeData<AudioSetVolumeMessage>(result.Envelope!);
        Assert.Equal(42, payload.VolumePercent);
    }

    [Fact]
    public void ParsesSuccessfulAck()
    {
        var json = ProtocolSerializer.SerializeAck("42", success: true);

        var result = ProtocolSerializer.Parse(json);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProtocolConstants.MessageTypes.Ack, result.Envelope!.Type);
        Assert.True(result.Envelope.Success);
        Assert.Null(result.Envelope.Error);
    }

    [Fact]
    public void FailedAckRequiresError()
    {
        var result = ProtocolSerializer.Parse("""
            { "protocol": 1, "type": "ack", "requestId": "42", "success": false }
            """);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolErrorCode.InvalidPayload, result.ErrorCode);
    }

    [Fact]
    public void RejectsInvalidJsonDeterministically()
    {
        var result = ProtocolSerializer.Parse("{ nope");

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolErrorCode.InvalidJson, result.ErrorCode);
        Assert.Equal("Message JSON is malformed.", result.ErrorMessage);
    }

    [Fact]
    public void RejectsUnknownProtocolVersion()
    {
        var result = ProtocolSerializer.Parse("""
            { "protocol": 2, "type": "ping", "data": { "sentAtUnixMs": 10 } }
            """);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolErrorCode.UnsupportedProtocol, result.ErrorCode);
    }

    [Fact]
    public void RejectsMissingProtocol()
    {
        var result = ProtocolSerializer.Parse("""
            { "type": "ping", "data": { "sentAtUnixMs": 10 } }
            """);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolErrorCode.MissingProtocol, result.ErrorCode);
    }

    [Fact]
    public void RejectsMissingType()
    {
        var result = ProtocolSerializer.Parse("""
            { "protocol": 1, "data": { "sentAtUnixMs": 10 } }
            """);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolErrorCode.MissingType, result.ErrorCode);
    }

    [Fact]
    public void RejectsUnknownType()
    {
        var result = ProtocolSerializer.Parse("""
            { "protocol": 1, "type": "media.magic" }
            """);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolErrorCode.UnknownType, result.ErrorCode);
    }

    [Fact]
    public void ParsesShortcutsReportPayload()
    {
        var result = ProtocolSerializer.Parse("""
            {
              "protocol": 1,
              "type": "shortcuts.report",
              "data": {
                "shortcuts": [
                  { "id": "shortcut-1", "label": "Device One", "actionType": "launch" }
                ]
              }
            }
            """);

        Assert.True(result.IsSuccess);
        var report = ProtocolSerializer.DeserializeData<ShortcutsReportMessage>(result.Envelope!);
        Assert.Single(report.Shortcuts);
        Assert.Equal("shortcut-1", report.Shortcuts[0].Id);
        Assert.Equal("Device One", report.Shortcuts[0].Label);
        Assert.Equal("launch", report.Shortcuts[0].ActionType);
    }

    [Fact]
    public void RejectsShortcutsReportAboveDeviceLimit()
    {
        var result = ProtocolSerializer.Parse("""
            {
              "protocol": 1,
              "type": "shortcuts.report",
              "data": {
                "shortcuts": [
                  { "id": "shortcut-1", "label": "One", "actionType": "launch" },
                  { "id": "shortcut-2", "label": "Two", "actionType": "launch" },
                  { "id": "shortcut-3", "label": "Three", "actionType": "launch" },
                  { "id": "shortcut-4", "label": "Four", "actionType": "launch" },
                  { "id": "shortcut-5", "label": "Five", "actionType": "launch" },
                  { "id": "shortcut-6", "label": "Six", "actionType": "launch" },
                  { "id": "shortcut-7", "label": "Seven", "actionType": "launch" }
                ]
              }
            }
            """);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolErrorCode.InvalidPayload, result.ErrorCode);
        Assert.Contains("up to 6", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("with space")]
    [InlineData("ą")]
    public void RejectsInvalidRequestId(string requestId)
    {
        var json = $$"""
            { "protocol": 1, "type": "media.command", "requestId": "{{requestId}}", "data": { "command": "next" } }
            """;

        var result = ProtocolSerializer.Parse(json);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolErrorCode.InvalidRequestId, result.ErrorCode);
    }

    [Fact]
    public void CommandMessagesRequireRequestId()
    {
        var result = ProtocolSerializer.Parse("""
            { "protocol": 1, "type": "shortcut.execute", "data": { "shortcutId": "screenshot" } }
            """);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolErrorCode.MissingRequestId, result.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(ValidMessages))]
    public void EveryRequiredMessageTypeParsesWhenPayloadIsValid(string type, string json)
    {
        var result = ProtocolSerializer.Parse(json);

        Assert.True(result.IsSuccess);
        Assert.Equal(type, result.Envelope!.Type);
    }

    [Theory]
    [MemberData(nameof(InvalidCommandPayloads))]
    public void RejectsInvalidEsp32CommandPayloads(string type, string json)
    {
        var result = ProtocolSerializer.Parse(json);

        Assert.False(string.IsNullOrWhiteSpace(type));
        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolErrorCode.InvalidPayload, result.ErrorCode);
    }

    [Fact]
    public void RejectsUnsupportedCommandPayloadProperties()
    {
        var result = ProtocolSerializer.Parse(CreateMessage(
            ProtocolConstants.MessageTypes.MediaCommand,
            "media-1",
            """{ "command": "next", "unexpected": true }"""));

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolErrorCode.InvalidPayload, result.ErrorCode);
        Assert.Contains("unexpected", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dimAfterSec")]
    [InlineData("screenOffAfterSec")]
    public void DeviceSettingsAcceptsMaximumSecondsBoundary(string propertyName)
    {
        var result = ProtocolSerializer.Parse(CreateMessage(
            ProtocolConstants.MessageTypes.DeviceSettings,
            "settings-1",
            $$"""{ "version": 1, "{{propertyName}}": 86400 }"""));

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("dimAfterSec")]
    [InlineData("screenOffAfterSec")]
    public void DeviceSettingsRejectsSecondsAboveMaximum(string propertyName)
    {
        var result = ProtocolSerializer.Parse(CreateMessage(
            ProtocolConstants.MessageTypes.DeviceSettings,
            "settings-1",
            $$"""{ "version": 1, "{{propertyName}}": 86401 }"""));

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolErrorCode.InvalidPayload, result.ErrorCode);
    }

    [Fact]
    public void DeviceInfoParsesDiagnosticsPayload()
    {
        var result = ProtocolSerializer.Parse(CreateMessage(
            ProtocolConstants.MessageTypes.DeviceInfo,
            null,
            """{ "firmwareVersion": "1.0.0", "uptimeSeconds": 12, "wifi": { "connected": true, "ssid": "Lab", "rssi": -55, "ip": "192.168.1.22" }, "agentHost": "192.168.1.10", "sd": { "mounted": true, "ready": true, "capacityBytes": 1024, "freeBytes": 512 }, "heap": { "freeBytes": 100000, "minFreeBytes": 80000, "largestBlockBytes": 64000 } }"""));

        Assert.True(result.IsSuccess);
        var info = ProtocolSerializer.DeserializeData<DeviceInfoMessage>(result.Envelope!);
        Assert.Equal("1.0.0", info.FirmwareVersion);
        Assert.True(info.Wifi.Connected);
        Assert.Equal("Lab", info.Wifi.Ssid);
        Assert.Equal(512, info.Sd.FreeBytes);
        Assert.Equal(64000, info.Heap.LargestBlockBytes);
    }

    [Fact]
    public void DeviceInfoAcceptsSdCapacityAndFreeBytesAboveInt32Maximum()
    {
        var result = ProtocolSerializer.Parse(CreateMessage(
            ProtocolConstants.MessageTypes.DeviceInfo,
            null,
            """{ "firmwareVersion": "1.0.0", "uptimeSeconds": 12, "wifi": { "connected": true }, "sd": { "mounted": true, "ready": true, "capacityBytes": 8589934592, "freeBytes": 4294967296 }, "heap": { "freeBytes": 100000, "minFreeBytes": 80000, "largestBlockBytes": 64000 } }"""));

        Assert.True(result.IsSuccess);
        var info = ProtocolSerializer.DeserializeData<DeviceInfoMessage>(result.Envelope!);
        Assert.Equal(8589934592, info.Sd.CapacityBytes);
        Assert.Equal(4294967296, info.Sd.FreeBytes);
    }

    [Theory]
    [InlineData("capacityBytes")]
    [InlineData("freeBytes")]
    public void DeviceInfoRejectsNegativeSdByteCounts(string propertyName)
    {
        var result = ProtocolSerializer.Parse(CreateMessage(
            ProtocolConstants.MessageTypes.DeviceInfo,
            null,
            $$"""{ "firmwareVersion": "1.0.0", "uptimeSeconds": 12, "wifi": { "connected": true }, "sd": { "mounted": true, "ready": true, "{{propertyName}}": -1 }, "heap": { "freeBytes": 100000, "minFreeBytes": 80000, "largestBlockBytes": 64000 } }"""));

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolErrorCode.InvalidPayload, result.ErrorCode);
    }

    [Theory]
    [InlineData("capacityBytes")]
    [InlineData("freeBytes")]
    public void DeviceInfoRejectsSdByteCountsAboveInt64Maximum(string propertyName)
    {
        var result = ProtocolSerializer.Parse(CreateMessage(
            ProtocolConstants.MessageTypes.DeviceInfo,
            null,
            $$"""{ "firmwareVersion": "1.0.0", "uptimeSeconds": 12, "wifi": { "connected": true }, "sd": { "mounted": true, "ready": true, "{{propertyName}}": 9223372036854775808 }, "heap": { "freeBytes": 100000, "minFreeBytes": 80000, "largestBlockBytes": 64000 } }"""));

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolErrorCode.InvalidPayload, result.ErrorCode);
    }

    [Fact]
    public void DeviceInfoRejectsSecretsAndUnknownFields()
    {
        var result = ProtocolSerializer.Parse(CreateMessage(
            ProtocolConstants.MessageTypes.DeviceInfo,
            null,
            """{ "firmwareVersion": "1.0.0", "uptimeSeconds": 12, "wifi": { "connected": true, "password": "secret" }, "sd": { "mounted": true, "ready": true }, "heap": { "freeBytes": 1, "minFreeBytes": 1, "largestBlockBytes": 1 } }"""));

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolErrorCode.InvalidPayload, result.ErrorCode);
    }

    [Fact]
    public void RejectsSuccessOutsideAck()
    {
        var result = ProtocolSerializer.Parse("""
            { "protocol": 1, "type": "pong", "success": true }
            """);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolErrorCode.InvalidPayload, result.ErrorCode);
    }

    public static IEnumerable<object[]> ValidMessages()
    {
        yield return [ProtocolConstants.MessageTypes.Hello, CreateMessage(ProtocolConstants.MessageTypes.Hello, null, """{ "deviceId": "cyd-001", "name": "CYD", "role": "esp32", "capabilities": ["media", "audio"] }""")];
        yield return [ProtocolConstants.MessageTypes.MediaCommand, CreateMessage(ProtocolConstants.MessageTypes.MediaCommand, "media-1", """{ "command": "next" }""")];
        yield return [ProtocolConstants.MessageTypes.AudioSetVolume, CreateMessage(ProtocolConstants.MessageTypes.AudioSetVolume, "volume-1", """{ "volumePercent": 42 }""")];
        yield return [ProtocolConstants.MessageTypes.AudioToggleMute, CreateMessage(ProtocolConstants.MessageTypes.AudioToggleMute, "mute-1", null)];
        yield return [ProtocolConstants.MessageTypes.ShortcutExecute, CreateMessage(ProtocolConstants.MessageTypes.ShortcutExecute, "shortcut-1", """{ "shortcutId": "obs.toggle-recording" }""")];
        yield return [ProtocolConstants.MessageTypes.SystemCommand, CreateMessage(ProtocolConstants.MessageTypes.SystemCommand, "system-1", """{ "command": "lock" }""")];
        yield return [ProtocolConstants.MessageTypes.DeviceRestart, CreateMessage(ProtocolConstants.MessageTypes.DeviceRestart, "device-1", null)];
        yield return [ProtocolConstants.MessageTypes.DeviceFactoryReset, CreateMessage(ProtocolConstants.MessageTypes.DeviceFactoryReset, "device-2", null)];
        yield return [ProtocolConstants.MessageTypes.DeviceSettings, CreateMessage(ProtocolConstants.MessageTypes.DeviceSettings, "settings-1", """{ "version": 1, "theme": "dark", "accent": "#D43B48", "background": "#101216", "buttonStyle": "rounded", "pageOrder": ["media", "pc", "shortcuts", "info"], "pageVisibility": ["media", "pc", "info"], "brightness": 70, "dimAfterSec": 300, "dimBrightness": 20, "screenOffAfterSec": 1800, "wakeOnTouch": true }""")];
        yield return [ProtocolConstants.MessageTypes.DeviceInfo, CreateMessage(ProtocolConstants.MessageTypes.DeviceInfo, null, """{ "firmwareVersion": "1.0.0", "uptimeSeconds": 12, "wifi": { "connected": true, "ssid": "Lab", "rssi": -55, "ip": "192.168.1.22" }, "agentHost": "192.168.1.10", "sd": { "mounted": true, "ready": true, "capacityBytes": 1024, "freeBytes": 512 }, "heap": { "freeBytes": 100000, "minFreeBytes": 80000, "largestBlockBytes": 64000 } }""")];
        yield return [ProtocolConstants.MessageTypes.Ping, CreateMessage(ProtocolConstants.MessageTypes.Ping, null, """{ "sentAtUnixMs": 123456789 }""")];
        yield return [ProtocolConstants.MessageTypes.MediaUpdate, CreateMessage(ProtocolConstants.MessageTypes.MediaUpdate, null, null)];
        yield return [ProtocolConstants.MessageTypes.MediaTimeline, CreateMessage(ProtocolConstants.MessageTypes.MediaTimeline, null, null)];
        yield return [ProtocolConstants.MessageTypes.MediaPlayback, CreateMessage(ProtocolConstants.MessageTypes.MediaPlayback, null, null)];
        yield return [ProtocolConstants.MessageTypes.AudioUpdate, CreateMessage(ProtocolConstants.MessageTypes.AudioUpdate, null, null)];
        yield return [ProtocolConstants.MessageTypes.SystemStats, CreateMessage(ProtocolConstants.MessageTypes.SystemStats, null, null)];
        yield return [ProtocolConstants.MessageTypes.ShortcutsUpdate, CreateMessage(ProtocolConstants.MessageTypes.ShortcutsUpdate, null, null)];
        yield return [ProtocolConstants.MessageTypes.ConnectionState, CreateMessage(ProtocolConstants.MessageTypes.ConnectionState, null, null)];
        yield return [ProtocolConstants.MessageTypes.PairingToken, ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.PairingToken, null, new PairingTokenMessage("abc"))];
        yield return [ProtocolConstants.MessageTypes.Ack, ProtocolSerializer.SerializeAck("ack-1", success: true)];
        yield return [ProtocolConstants.MessageTypes.Pong, CreateMessage(ProtocolConstants.MessageTypes.Pong, null, null)];
    }

    public static IEnumerable<object[]> InvalidCommandPayloads()
    {
        yield return [ProtocolConstants.MessageTypes.Hello, CreateMessage(ProtocolConstants.MessageTypes.Hello, null, """{ "deviceId": "cyd-001", "name": "CYD", "role": "speaker", "capabilities": [] }""")];
        yield return [ProtocolConstants.MessageTypes.Hello, CreateMessage(ProtocolConstants.MessageTypes.Hello, null, """{ "deviceId": "cyd-001", "name": "", "role": "esp32", "capabilities": [] }""")];
        yield return [ProtocolConstants.MessageTypes.Hello, CreateMessage(ProtocolConstants.MessageTypes.Hello, null, """{ "deviceId": "cyd-001", "name": "CYD", "role": "esp32", "capabilities": "media" }""")];
        yield return [ProtocolConstants.MessageTypes.MediaCommand, CreateMessage(ProtocolConstants.MessageTypes.MediaCommand, "media-1", """{ "command": "shuffle" }""")];
        yield return [ProtocolConstants.MessageTypes.MediaCommand, CreateMessage(ProtocolConstants.MessageTypes.MediaCommand, "media-1", """{ "command": 1 }""")];
        yield return [ProtocolConstants.MessageTypes.MediaCommand, CreateMessage(ProtocolConstants.MessageTypes.MediaCommand, "media-1", """{}""")];
        yield return [ProtocolConstants.MessageTypes.AudioSetVolume, CreateMessage(ProtocolConstants.MessageTypes.AudioSetVolume, "volume-1", """{ "volumePercent": -1 }""")];
        yield return [ProtocolConstants.MessageTypes.AudioSetVolume, CreateMessage(ProtocolConstants.MessageTypes.AudioSetVolume, "volume-1", """{ "volumePercent": 101 }""")];
        yield return [ProtocolConstants.MessageTypes.AudioSetVolume, CreateMessage(ProtocolConstants.MessageTypes.AudioSetVolume, "volume-1", """{ "volumePercent": "42" }""")];
        yield return [ProtocolConstants.MessageTypes.AudioSetVolume, CreateMessage(ProtocolConstants.MessageTypes.AudioSetVolume, "volume-1", """{}""")];
        yield return [ProtocolConstants.MessageTypes.AudioToggleMute, CreateMessage(ProtocolConstants.MessageTypes.AudioToggleMute, "mute-1", """{ "muted": true }""")];
        yield return [ProtocolConstants.MessageTypes.ShortcutExecute, CreateMessage(ProtocolConstants.MessageTypes.ShortcutExecute, "shortcut-1", """{ "shortcutId": "bad id" }""")];
        yield return [ProtocolConstants.MessageTypes.ShortcutExecute, CreateMessage(ProtocolConstants.MessageTypes.ShortcutExecute, "shortcut-1", """{ "shortcutId": 1 }""")];
        yield return [ProtocolConstants.MessageTypes.ShortcutExecute, CreateMessage(ProtocolConstants.MessageTypes.ShortcutExecute, "shortcut-1", """{}""")];
        yield return [ProtocolConstants.MessageTypes.SystemCommand, CreateMessage(ProtocolConstants.MessageTypes.SystemCommand, "system-1", """{ "command": "shutdown" }""")];
        yield return [ProtocolConstants.MessageTypes.SystemCommand, CreateMessage(ProtocolConstants.MessageTypes.SystemCommand, "system-1", """{ "command": false }""")];
        yield return [ProtocolConstants.MessageTypes.DeviceRestart, CreateMessage(ProtocolConstants.MessageTypes.DeviceRestart, "device-1", """{ "unexpected": true }""")];
        yield return [ProtocolConstants.MessageTypes.DeviceFactoryReset, CreateMessage(ProtocolConstants.MessageTypes.DeviceFactoryReset, "device-2", """{ "unexpected": true }""")];
        yield return [ProtocolConstants.MessageTypes.DeviceSettings, CreateMessage(ProtocolConstants.MessageTypes.DeviceSettings, "settings-1", """{ "version": 2 }""")];
        yield return [ProtocolConstants.MessageTypes.DeviceSettings, CreateMessage(ProtocolConstants.MessageTypes.DeviceSettings, "settings-1", """{ "version": 1, "accent": "red" }""")];
        yield return [ProtocolConstants.MessageTypes.DeviceSettings, CreateMessage(ProtocolConstants.MessageTypes.DeviceSettings, "settings-1", """{ "version": 1, "pageOrder": ["media", "pc", "info"] }""")];
        yield return [ProtocolConstants.MessageTypes.DeviceSettings, CreateMessage(ProtocolConstants.MessageTypes.DeviceSettings, "settings-1", """{ "version": 1, "pageVisibility": ["media", "pc"] }""")];
        yield return [ProtocolConstants.MessageTypes.Ping, CreateMessage(ProtocolConstants.MessageTypes.Ping, null, """{ "sentAtUnixMs": -1 }""")];
        yield return [ProtocolConstants.MessageTypes.Ping, CreateMessage(ProtocolConstants.MessageTypes.Ping, null, """{ "sentAtUnixMs": "123" }""")];
        yield return [ProtocolConstants.MessageTypes.Ping, CreateMessage(ProtocolConstants.MessageTypes.Ping, null, """{}""")];
    }

    private static string CreateMessage(string type, string? requestId, string? data)
    {
        var requestIdFragment = requestId is null ? string.Empty : $", \"requestId\": \"{requestId}\"";
        var dataFragment = data is null ? string.Empty : $", \"data\": {data}";

        return $"{{ \"protocol\": 1, \"type\": \"{type}\"{requestIdFragment}{dataFragment} }}";
    }
}
