using System.Text.Json;
using SkiaSharp;

namespace StreamDeck.WindowsAgent.Shell;

internal static class ShortcutIconCatalog
{
    internal sealed record Entry(string Key, string Alpha);
    private static readonly Entry[] Entries = Load();
    internal static IReadOnlyList<Entry> All => Entries;
    private static readonly Dictionary<string, Bitmap> Cache = new();
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static Entry[] Load()
    {
        using var stream = typeof(ShortcutIconCatalog).Assembly.GetManifestResourceStream("StreamDeck.WindowsAgent.Shell.shortcut-icons.json")!;
        return JsonSerializer.Deserialize<Entry[]>(stream)!;
    }
    internal static string Normalize(string? key) => (key ?? "shortcut").Trim().ToLowerInvariant() switch
    {
        "microphone" => "mic", "web" or "url" => "browser", "keyboard" or "key" => "hotkey", "tools" or "settings" => "gear",
        "media" => "play", "previous" => "prev", "file" => "shortcut", "script" or "powershell" => "terminal",
        var value => value
    };
    internal static void Draw(Graphics graphics, string? key, string? data, Rectangle bounds, Color foreground)
    {
        var normalized = Normalize(key);
        if (normalized == "empty" && data is null) return;
        var cacheKey = data ?? normalized + ":" + foreground.ToArgb();
        if (!Cache.TryGetValue(cacheKey, out var bitmap))
        {
            bitmap = new Bitmap(24, 24, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var raw = data is null ? null : Convert.FromBase64String(data);
            var entry = Entries.FirstOrDefault(item => item.Key == normalized) ?? Entries.First(item => item.Key == "shortcut");
            var alpha = raw is null ? Convert.FromBase64String(entry.Alpha) : null;
            for (var i = 0; i < 576; ++i)
            {
                var color = foreground;
                if (raw is not null && raw.Length == 1728)
                {
                    var pixel = raw[i * 2] | raw[i * 2 + 1] << 8;
                    color = Color.FromArgb(raw[1152 + i], ((pixel >> 11) & 31) * 255 / 31, ((pixel >> 5) & 63) * 255 / 63, (pixel & 31) * 255 / 31);
                }
                else color = Color.FromArgb(alpha?[i] ?? 0, foreground);
                bitmap.SetPixel(i % 24, i / 24, color);
            }
            if (Cache.Count >= 128) { foreach (var image in Cache.Values) image.Dispose(); Cache.Clear(); }
            Cache[cacheKey] = bitmap;
        }
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        graphics.DrawImage(bitmap, bounds);
    }
    internal static async Task<string> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidDataException("Paste a direct HTTP or HTTPS image URL.");
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        const int maxBytes = 2 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException("Image is too large (maximum 2 MB).");
        using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var content = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (content.Length + count > maxBytes) throw new InvalidDataException("Image is too large (maximum 2 MB).");
            content.Write(buffer, 0, count);
        }
        return Encode(content.ToArray());
    }
    internal static string Encode(byte[] encoded)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(encoded));
        if (codec is null || codec.Info.Width < 1 || codec.Info.Height < 1 || codec.Info.Width > 4096 || codec.Info.Height > 4096)
            throw new InvalidDataException("Use a PNG, JPEG, WebP, GIF or BMP image up to 4096 × 4096 px.");
        using var source = SKBitmap.Decode(codec) ?? throw new InvalidDataException("Cannot decode this image.");
        using var icon = new SKBitmap(24, 24, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(icon);
        canvas.Clear(SKColors.Transparent);
        float scale = Math.Min(24f / source.Width, 24f / source.Height);
        var width = source.Width * scale; var height = source.Height * scale;
        canvas.DrawBitmap(source, new SKRect((24 - width) / 2, (24 - height) / 2, (24 + width) / 2, (24 + height) / 2), new SKSamplingOptions(SKFilterMode.Linear));
        var raw = new byte[1728];
        for (var i = 0; i < 576; ++i)
        {
            var color = icon.GetPixel(i % 24, i / 24);
            var rgb = (color.Red >> 3) << 11 | (color.Green >> 2) << 5 | color.Blue >> 3;
            raw[i * 2] = (byte)rgb; raw[i * 2 + 1] = (byte)(rgb >> 8); raw[1152 + i] = color.Alpha;
        }
        return Convert.ToBase64String(raw);
    }
}
