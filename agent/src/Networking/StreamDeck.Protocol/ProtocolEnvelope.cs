using System.Text.Json;
using System.Text.Json.Serialization;

namespace StreamDeck.Protocol;

public sealed record ProtocolEnvelope
{
    public int? Protocol { get; init; }

    public string? Type { get; init; }

    public string? RequestId { get; init; }

    public JsonElement? Data { get; init; }

    public bool? Success { get; init; }

    public ProtocolError? Error { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
