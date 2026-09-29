using System.Text.Json;
using System.Text.Json.Serialization;

namespace StreamDeck.Protocol;

public static class ProtocolSerializer
{
    private static readonly HashSet<string> KnownTypes =
    [
        ProtocolConstants.MessageTypes.Hello,
        ProtocolConstants.MessageTypes.MediaCommand,
        ProtocolConstants.MessageTypes.AudioSetVolume,
        ProtocolConstants.MessageTypes.AudioToggleMute,
        ProtocolConstants.MessageTypes.ShortcutExecute,
        ProtocolConstants.MessageTypes.SystemCommand,
        ProtocolConstants.MessageTypes.DeviceRestart,
        ProtocolConstants.MessageTypes.DeviceFactoryReset,
        ProtocolConstants.MessageTypes.DeviceSettings,
        ProtocolConstants.MessageTypes.DeviceInfo,
        ProtocolConstants.MessageTypes.Ping,
        ProtocolConstants.MessageTypes.MediaUpdate,
        ProtocolConstants.MessageTypes.MediaTimeline,
        ProtocolConstants.MessageTypes.MediaPlayback,
        ProtocolConstants.MessageTypes.AudioUpdate,
        ProtocolConstants.MessageTypes.SystemStats,
        ProtocolConstants.MessageTypes.ShortcutsUpdate,
        ProtocolConstants.MessageTypes.ShortcutsReport,
        ProtocolConstants.MessageTypes.ConnectionState,
        ProtocolConstants.MessageTypes.PairingToken,
        ProtocolConstants.MessageTypes.Ack,
        ProtocolConstants.MessageTypes.Pong
    ];

    private static readonly HashSet<string> HelloRoles = ["esp32", "agent"];

    private static readonly HashSet<string> MediaCommandValues = ["playPause", "play", "pause", "previous", "next", "stop"];

    private static readonly HashSet<string> SystemCommandValues = ["sleep", "lock", "restartAgent"];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize<TPayload>(string type, string? requestId, TPayload? payload)
    {
        var envelope = CreateEnvelope(type, requestId, payload);
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    public static string SerializeAck(string requestId, bool success, ProtocolError? error = null)
    {
        var envelope = new ProtocolEnvelope
        {
            Protocol = ProtocolConstants.CurrentVersion,
            Type = ProtocolConstants.MessageTypes.Ack,
            RequestId = requestId,
            Success = success,
            Error = error
        };

        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    public static ProtocolParseResult Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidJson, "Message JSON is empty.");
        }

        ProtocolEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ProtocolEnvelope>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidJson, "Message JSON is malformed.");
        }

        if (envelope is null)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidJson, "Message JSON is empty.");
        }

        return Validate(envelope);
    }

    public static TPayload DeserializeData<TPayload>(ProtocolEnvelope envelope)
    {
        if (envelope.Data is null)
        {
            throw new InvalidOperationException("Envelope does not contain data.");
        }

        var payload = envelope.Data.Value.Deserialize<TPayload>(JsonOptions);
        return payload ?? throw new InvalidOperationException("Envelope data is empty.");
    }

    public static ProtocolEnvelope CreateEnvelope<TPayload>(string type, string? requestId, TPayload? payload)
    {
        return new ProtocolEnvelope
        {
            Protocol = ProtocolConstants.CurrentVersion,
            Type = type,
            RequestId = NormalizeRequestId(requestId),
            Data = payload is null ? null : JsonSerializer.SerializeToElement(payload, JsonOptions)
        };
    }

    public static ProtocolParseResult Validate(ProtocolEnvelope envelope)
    {
        if (envelope.Protocol is null)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.MissingProtocol, "Message protocol is required.");
        }

        if (envelope.Protocol != ProtocolConstants.CurrentVersion)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.UnsupportedProtocol, $"Unsupported protocol version {envelope.Protocol}.");
        }

        if (string.IsNullOrWhiteSpace(envelope.Type))
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.MissingType, "Message type is required.");
        }

        if (!KnownTypes.Contains(envelope.Type))
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.UnknownType, $"Unknown message type '{envelope.Type}'.");
        }

        var requestIdValidation = ValidateRequestId(envelope);
        if (requestIdValidation is not null)
        {
            return requestIdValidation;
        }

        var payloadValidation = ValidatePayloadShape(envelope);
        if (payloadValidation is not null)
        {
            return payloadValidation;
        }

        return ProtocolParseResult.Success(envelope);
    }

    private static ProtocolParseResult? ValidateRequestId(ProtocolEnvelope envelope)
    {
        if (envelope.RequestId is null)
        {
            if (RequiresRequestId(envelope.Type!))
            {
                return ProtocolParseResult.Failure(ProtocolErrorCode.MissingRequestId, $"Message type '{envelope.Type}' requires requestId.");
            }

            return null;
        }

        if (string.IsNullOrWhiteSpace(envelope.RequestId) || envelope.RequestId.Length > 64)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidRequestId, "requestId must be 1-64 visible ASCII characters.");
        }

        foreach (var character in envelope.RequestId)
        {
            if (character < 0x21 || character > 0x7e)
            {
                return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidRequestId, "requestId must be 1-64 visible ASCII characters.");
            }
        }

        return null;
    }

    private static ProtocolParseResult? ValidatePayloadShape(ProtocolEnvelope envelope)
    {
        if (envelope.Type == ProtocolConstants.MessageTypes.Ack)
        {
            if (envelope.Success is null)
            {
                return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, "ack requires success.");
            }

            if (envelope.Success == false && envelope.Error is null)
            {
                return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, "failed ack requires error.");
            }

            return null;
        }

        if (envelope.Success is not null || envelope.Error is not null)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, "success/error fields are only valid for ack.");
        }

        return ValidateCommandPayload(envelope);
    }

    private static ProtocolParseResult? ValidateCommandPayload(ProtocolEnvelope envelope)
    {
        return envelope.Type switch
        {
            ProtocolConstants.MessageTypes.Hello => ValidateHello(envelope),
            ProtocolConstants.MessageTypes.MediaCommand => ValidateStringEnumPayload(envelope, "command", MediaCommandValues),
            ProtocolConstants.MessageTypes.AudioSetVolume => ValidateIntegerRangePayload(envelope, "volumePercent", 0, 100),
            ProtocolConstants.MessageTypes.AudioToggleMute => ValidateEmptyPayload(envelope),
            ProtocolConstants.MessageTypes.ShortcutExecute => ValidateStableIdPayload(envelope, "shortcutId"),
            ProtocolConstants.MessageTypes.SystemCommand => ValidateStringEnumPayload(envelope, "command", SystemCommandValues),
            ProtocolConstants.MessageTypes.DeviceRestart => ValidateEmptyPayload(envelope),
            ProtocolConstants.MessageTypes.DeviceFactoryReset => ValidateEmptyPayload(envelope),
            ProtocolConstants.MessageTypes.DeviceSettings => ValidateDeviceSettings(envelope),
            ProtocolConstants.MessageTypes.DeviceInfo => ValidateDeviceInfo(envelope),
            ProtocolConstants.MessageTypes.ShortcutsReport => ValidateShortcutsReport(envelope),
            ProtocolConstants.MessageTypes.Ping => ValidateIntegerRangePayload(envelope, "sentAtUnixMs", 0, long.MaxValue),
            _ => null
        };
    }

    private static ProtocolParseResult? ValidateHello(ProtocolEnvelope envelope)
    {
        var payloadValidation = RequireObjectPayload(envelope, out var data);
        if (payloadValidation is not null)
        {
            return payloadValidation;
        }

        var propertiesValidation = ValidateOnlyProperties(envelope, data, "deviceId", "name", "role", "capabilities");
        if (propertiesValidation is not null)
        {
            return propertiesValidation;
        }

        var deviceIdValidation = ValidateStableStringProperty(data, "deviceId");
        if (deviceIdValidation is not null)
        {
            return deviceIdValidation;
        }

        var nameValidation = ValidateDisplayStringProperty(data, "name");
        if (nameValidation is not null)
        {
            return nameValidation;
        }

        var roleValidation = ValidateStringEnumProperty(data, "role", HelloRoles);
        if (roleValidation is not null)
        {
            return roleValidation;
        }

        if (!data.TryGetProperty("capabilities", out var capabilities) || capabilities.ValueKind != JsonValueKind.Array)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, "hello requires data.capabilities array.");
        }

        foreach (var capability in capabilities.EnumerateArray())
        {
            if (capability.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(capability.GetString()))
            {
                return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, "hello capabilities must be non-empty strings.");
            }
        }

        return null;
    }

    private static ProtocolParseResult? ValidateStringEnumPayload(ProtocolEnvelope envelope, string propertyName, IReadOnlySet<string> allowedValues)
    {
        var payloadValidation = RequireObjectPayload(envelope, out var data);
        if (payloadValidation is not null)
        {
            return payloadValidation;
        }

        var propertiesValidation = ValidateOnlyProperties(envelope, data, propertyName);
        if (propertiesValidation is not null)
        {
            return propertiesValidation;
        }

        return ValidateStringEnumProperty(data, propertyName, allowedValues);
    }

    private static ProtocolParseResult? ValidateIntegerRangePayload(ProtocolEnvelope envelope, string propertyName, long min, long max)
    {
        var payloadValidation = RequireObjectPayload(envelope, out var data);
        if (payloadValidation is not null)
        {
            return payloadValidation;
        }

        var propertiesValidation = ValidateOnlyProperties(envelope, data, propertyName);
        if (propertiesValidation is not null)
        {
            return propertiesValidation;
        }

        if (!data.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var integer))
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"{envelope.Type} requires integer data.{propertyName}.");
        }

        if (integer < min || integer > max)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"{envelope.Type} data.{propertyName} must be between {min} and {max}.");
        }

        return null;
    }

    private static ProtocolParseResult? ValidateEmptyPayload(ProtocolEnvelope envelope)
    {
        if (envelope.Data is null)
        {
            return null;
        }

        var data = envelope.Data.Value;
        if (data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (data.ValueKind == JsonValueKind.Object && !data.EnumerateObject().Any())
        {
            return null;
        }

        return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"{envelope.Type} requires empty or missing data.");
    }

    private static ProtocolParseResult? ValidateStableIdPayload(ProtocolEnvelope envelope, string propertyName)
    {
        var payloadValidation = RequireObjectPayload(envelope, out var data);
        if (payloadValidation is not null)
        {
            return payloadValidation;
        }

        var propertiesValidation = ValidateOnlyProperties(envelope, data, propertyName);
        if (propertiesValidation is not null)
        {
            return propertiesValidation;
        }

        return ValidateStableStringProperty(data, propertyName);
    }

    private static ProtocolParseResult? ValidateShortcutsReport(ProtocolEnvelope envelope)
    {
        var payloadValidation = RequireObjectPayload(envelope, out var data);
        if (payloadValidation is not null)
        {
            return payloadValidation;
        }

        var propertiesValidation = ValidateOnlyProperties(envelope, data, "shortcuts");
        if (propertiesValidation is not null)
        {
            return propertiesValidation;
        }

        if (!data.TryGetProperty("shortcuts", out var shortcuts) || shortcuts.ValueKind != JsonValueKind.Array)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, "shortcuts.report requires data.shortcuts array.");
        }

        if (shortcuts.GetArrayLength() > 6)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, "shortcuts.report supports up to 6 shortcuts.");
        }

        foreach (var shortcut in shortcuts.EnumerateArray())
        {
            if (shortcut.ValueKind != JsonValueKind.Object)
            {
                return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, "shortcuts.report shortcuts must be objects.");
            }

            var shortcutEnvelope = envelope with { Data = shortcut };
            var shortcutPropertiesValidation = ValidateOnlyProperties(shortcutEnvelope, shortcut, "id", "label", "actionType");
            if (shortcutPropertiesValidation is not null)
            {
                return shortcutPropertiesValidation;
            }

            var idValidation = ValidateStableStringProperty(shortcut, "id");
            if (idValidation is not null)
            {
                return idValidation;
            }

            var labelValidation = ValidateDisplayStringProperty(shortcut, "label");
            if (labelValidation is not null)
            {
                return labelValidation;
            }

            var actionTypeValidation = ValidateStableStringProperty(shortcut, "actionType");
            if (actionTypeValidation is not null)
            {
                return actionTypeValidation;
            }
        }

        return null;
    }

    private static ProtocolParseResult? RequireObjectPayload(ProtocolEnvelope envelope, out JsonElement data)
    {
        data = default;
        if (envelope.Data is null)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"{envelope.Type} requires data object.");
        }

        data = envelope.Data.Value;
        if (data.ValueKind != JsonValueKind.Object)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"{envelope.Type} requires data object.");
        }

        return null;
    }

    private static ProtocolParseResult? ValidateOnlyProperties(ProtocolEnvelope envelope, JsonElement data, params string[] allowedProperties)
    {
        foreach (var property in data.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name))
            {
                return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"{envelope.Type} data.{property.Name} is not supported.");
            }
        }

        return null;
    }

    private static ProtocolParseResult? ValidateStableStringProperty(JsonElement data, string propertyName)
    {
        if (!data.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"data.{propertyName} must be a stable 1-64 character token.");
        }

        return IsStableToken(value.GetString())
            ? null
            : ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"data.{propertyName} must be a stable 1-64 character token.");
    }

    private static ProtocolParseResult? ValidateDisplayStringProperty(JsonElement data, string propertyName)
    {
        if (!data.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"data.{propertyName} must be a non-empty string.");
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) || text.Length > 64
            ? ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"data.{propertyName} must be a non-empty string up to 64 characters.")
            : null;
    }

    private static ProtocolParseResult? ValidateStringEnumProperty(JsonElement data, string propertyName, IReadOnlySet<string> allowedValues)
    {
        if (!data.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"data.{propertyName} must be one of: {string.Join(", ", allowedValues)}.");
        }

        var text = value.GetString();
        return text is not null && allowedValues.Contains(text)
            ? null
            : ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"data.{propertyName} must be one of: {string.Join(", ", allowedValues)}.");
    }

    private static bool IsStableToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || !char.IsAsciiLetterOrDigit(value[0]))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool RequiresRequestId(string type)
    {
        return type is ProtocolConstants.MessageTypes.MediaCommand
            or ProtocolConstants.MessageTypes.AudioSetVolume
            or ProtocolConstants.MessageTypes.AudioToggleMute
            or ProtocolConstants.MessageTypes.ShortcutExecute
            or ProtocolConstants.MessageTypes.SystemCommand
            or ProtocolConstants.MessageTypes.DeviceRestart
            or ProtocolConstants.MessageTypes.DeviceFactoryReset
            or ProtocolConstants.MessageTypes.DeviceSettings
            or ProtocolConstants.MessageTypes.Ack;
    }

    private static string? NormalizeRequestId(string? requestId)
    {
        return string.IsNullOrWhiteSpace(requestId) ? null : requestId;
    }

    private static ProtocolParseResult? ValidateDeviceSettings(ProtocolEnvelope envelope)
    {
        var payloadValidation = RequireObjectPayload(envelope, out var data);
        if (payloadValidation is not null)
        {
            return payloadValidation;
        }

        var propertiesValidation = ValidateOnlyProperties(
            envelope,
            data,
            "version",
            "theme",
            "accent",
            "background",
            "buttonStyle",
            "pageOrder",
            "pageVisibility",
            "brightness",
            "dimAfterSec",
            "dimBrightness",
            "screenOffAfterSec",
            "wakeOnTouch");
        if (propertiesValidation is not null)
        {
            return propertiesValidation;
        }

        if (!data.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var versionValue) || versionValue != 1)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, "device.settings requires data.version 1.");
        }

        var enumValidation = ValidateOptionalEnum(data, "theme", new HashSet<string> { "dark", "light" })
            ?? ValidateOptionalEnum(data, "buttonStyle", new HashSet<string> { "flat", "rounded", "pill" });
        if (enumValidation is not null)
        {
            return enumValidation;
        }

        var colorValidation = ValidateOptionalHexColor(data, "accent")
            ?? ValidateOptionalHexColor(data, "background");
        if (colorValidation is not null)
        {
            return colorValidation;
        }

        var rangeValidation = ValidateOptionalIntegerRange(data, "brightness", 10, 100)
            ?? ValidateOptionalIntegerRange(data, "dimAfterSec", 0, 86400)
            ?? ValidateOptionalIntegerRange(data, "dimBrightness", 1, 100)
            ?? ValidateOptionalIntegerRange(data, "screenOffAfterSec", 0, 86400);
        if (rangeValidation is not null)
        {
            return rangeValidation;
        }

        if (data.TryGetProperty("wakeOnTouch", out var wakeOnTouch) && wakeOnTouch.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, "device.settings data.wakeOnTouch must be boolean.");
        }

        return ValidateOptionalPageList(data, "pageOrder", requireInfo: true, allowSubset: false)
            ?? ValidateOptionalPageList(data, "pageVisibility", requireInfo: true, allowSubset: true);
    }

    private static ProtocolParseResult? ValidateDeviceInfo(ProtocolEnvelope envelope)
    {
        var payloadValidation = RequireObjectPayload(envelope, out var data);
        if (payloadValidation is not null)
        {
            return payloadValidation;
        }

        var propertiesValidation = ValidateOnlyProperties(envelope, data, "firmwareVersion", "uptimeSeconds", "wifi", "agentHost", "sd", "heap");
        if (propertiesValidation is not null)
        {
            return propertiesValidation;
        }

        var firmwareValidation = ValidateDisplayStringProperty(data, "firmwareVersion");
        if (firmwareValidation is not null)
        {
            return firmwareValidation;
        }

        var uptimeValidation = ValidateRequiredIntegerRange(data, "uptimeSeconds", 0, long.MaxValue, "device.info");
        if (uptimeValidation is not null)
        {
            return uptimeValidation;
        }

        return ValidateDeviceWifi(data)
            ?? ValidateDeviceStorage(data)
            ?? ValidateDeviceHeap(data);
    }

    private static ProtocolParseResult? ValidateDeviceWifi(JsonElement data)
    {
        if (!data.TryGetProperty("wifi", out var wifi) || wifi.ValueKind != JsonValueKind.Object)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, "device.info requires data.wifi object.");
        }

        var propertiesValidation = ValidateOnlyProperties(new ProtocolEnvelope { Type = "device.info" }, wifi, "connected", "ssid", "rssi", "ip");
        if (propertiesValidation is not null)
        {
            return propertiesValidation;
        }

        if (!wifi.TryGetProperty("connected", out var connected) || connected.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, "device.info data.wifi.connected must be boolean.");
        }

        return ValidateOptionalDisplayString(wifi, "ssid", 64)
            ?? ValidateOptionalDisplayString(wifi, "ip", 45)
            ?? ValidateOptionalIntegerRange(wifi, "rssi", -127, 0);
    }

    private static ProtocolParseResult? ValidateDeviceStorage(JsonElement data)
    {
        if (!data.TryGetProperty("sd", out var sd) || sd.ValueKind != JsonValueKind.Object)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, "device.info requires data.sd object.");
        }

        var propertiesValidation = ValidateOnlyProperties(new ProtocolEnvelope { Type = "device.info" }, sd, "mounted", "ready", "capacityBytes", "freeBytes");
        if (propertiesValidation is not null)
        {
            return propertiesValidation;
        }

        if (!sd.TryGetProperty("mounted", out var mounted) || mounted.ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || !sd.TryGetProperty("ready", out var ready) || ready.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, "device.info data.sd mounted/ready must be boolean.");
        }

        return ValidateOptionalIntegerRange(sd, "capacityBytes", 0, long.MaxValue)
            ?? ValidateOptionalIntegerRange(sd, "freeBytes", 0, long.MaxValue);
    }

    private static ProtocolParseResult? ValidateDeviceHeap(JsonElement data)
    {
        if (!data.TryGetProperty("heap", out var heap) || heap.ValueKind != JsonValueKind.Object)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, "device.info requires data.heap object.");
        }

        var propertiesValidation = ValidateOnlyProperties(new ProtocolEnvelope { Type = "device.info" }, heap, "freeBytes", "minFreeBytes", "largestBlockBytes");
        if (propertiesValidation is not null)
        {
            return propertiesValidation;
        }

        return ValidateRequiredIntegerRange(heap, "freeBytes", 0, long.MaxValue, "device.info")
            ?? ValidateRequiredIntegerRange(heap, "minFreeBytes", 0, long.MaxValue, "device.info")
            ?? ValidateRequiredIntegerRange(heap, "largestBlockBytes", 0, long.MaxValue, "device.info");
    }

    private static ProtocolParseResult? ValidateRequiredIntegerRange(JsonElement data, string propertyName, long min, long max, string type)
    {
        return data.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var integer) && integer >= min && integer <= max
            ? null
            : ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"{type} data.{propertyName} must be between {min} and {max}.");
    }

    private static ProtocolParseResult? ValidateOptionalDisplayString(JsonElement data, string propertyName, int maxLength)
    {
        if (!data.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return text is not null && text.Length <= maxLength
            ? null
            : ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"data.{propertyName} must be a string up to {maxLength} characters.");
    }

    private static ProtocolParseResult? ValidateOptionalEnum(JsonElement data, string propertyName, IReadOnlySet<string> allowedValues)
    {
        if (!data.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String && allowedValues.Contains(value.GetString() ?? string.Empty)
            ? null
            : ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"device.settings data.{propertyName} must be one of: {string.Join(", ", allowedValues)}.");
    }

    private static ProtocolParseResult? ValidateOptionalHexColor(JsonElement data, string propertyName)
    {
        if (!data.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (text is null || text.Length != 7 || text[0] != '#')
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"device.settings data.{propertyName} must be #RRGGBB.");
        }

        return text.Skip(1).All(Uri.IsHexDigit)
            ? null
            : ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"device.settings data.{propertyName} must be #RRGGBB.");
    }

    private static ProtocolParseResult? ValidateOptionalIntegerRange(JsonElement data, string propertyName, long min, long max)
    {
        if (!data.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var integer) && integer >= min && integer <= max
            ? null
            : ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"device.settings data.{propertyName} must be between {min} and {max}.");
    }

    private static ProtocolParseResult? ValidateOptionalPageList(JsonElement data, string propertyName, bool requireInfo, bool allowSubset)
    {
        if (!data.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"device.settings data.{propertyName} must be an array.");
        }

        HashSet<string> allowed = ["media", "shortcuts", "pc", "info"];
        HashSet<string> seen = [];
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"device.settings data.{propertyName} must contain page names.");
            }

            var page = item.GetString() ?? string.Empty;
            if (!allowed.Contains(page) || !seen.Add(page))
            {
                return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"device.settings data.{propertyName} contains invalid or duplicate page '{page}'.");
            }
        }

        if (!allowSubset && seen.Count != allowed.Count)
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"device.settings data.{propertyName} must include all pages.");
        }

        if (requireInfo && !seen.Contains("info"))
        {
            return ProtocolParseResult.Failure(ProtocolErrorCode.InvalidPayload, $"device.settings data.{propertyName} must include info.");
        }

        return null;
    }
}
