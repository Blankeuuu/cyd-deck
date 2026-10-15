using System.Text.Json;
using StreamDeck.Host;

namespace StreamDeck.WindowsAgent.Runtime;

public sealed class UsbCoverSender(string coverCacheDirectory, TimeSpan? chunkDelay = null)
{
    public const int MaxRawChunkBytes = 512;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly TimeSpan delay = chunkDelay ?? TimeSpan.FromMilliseconds(20);

    public async Task<bool> SendCoverAsync(string? coverId, IProtocolSink sink, CancellationToken cancellationToken = default)
    {
        if (!IsValidCoverId(coverId)) return false;
        var path = Path.Combine(coverCacheDirectory, "covers", $"{coverId}.jpg");
        if (!File.Exists(path)) return false;

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        for (var offset = 0; offset < bytes.Length; offset += MaxRawChunkBytes)
        {
            var count = Math.Min(MaxRawChunkBytes, bytes.Length - offset);
            var payload = new CoverChunkMessage(coverId!, offset, bytes.Length, Convert.ToBase64String(bytes, offset, count));
            await sink.SendTextAsync(CreateMessage("cover.chunk", payload), cancellationToken).ConfigureAwait(false);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        await sink.SendTextAsync(CreateMessage("cover.complete", new CoverCompleteMessage(coverId!)), cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static string CreateMessage<T>(string type, T data)
    {
        return JsonSerializer.Serialize(new UsbProtocolMessage<T>(1, type, null, data), JsonOptions);
    }

    private static bool IsValidCoverId(string? coverId)
    {
        return coverId is { Length: 64 } && coverId.All(Uri.IsHexDigit);
    }

    private sealed record UsbProtocolMessage<T>(int Protocol, string Type, string? RequestId, T Data);
    private sealed record CoverChunkMessage(string CoverId, int Offset, int Total, string Data);
    private sealed record CoverCompleteMessage(string CoverId);
}
