using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;

namespace StreamDeck.Covers;

public sealed class CoverProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly CoverProcessingOptions options;
    private readonly string coversDirectory;
    private readonly string indexDirectory;
    private readonly string temporaryDirectory;

    public CoverProcessor(CoverProcessingOptions options)
    {
        options.Validate();
        this.options = options;
        coversDirectory = Path.Combine(options.CacheDirectory, "covers");
        indexDirectory = Path.Combine(options.CacheDirectory, "index");
        temporaryDirectory = Path.Combine(options.CacheDirectory, "tmp");
    }

    public async Task<CoverProcessResult?> ProcessAsync(Stream? thumbnailStream, CancellationToken cancellationToken = default)
    {
        if (thumbnailStream is null)
        {
            return null;
        }

        if (thumbnailStream.CanSeek && thumbnailStream.Length == 0)
        {
            return null;
        }

        Directory.CreateDirectory(coversDirectory);
        Directory.CreateDirectory(indexDirectory);
        Directory.CreateDirectory(temporaryDirectory);

        var preparedSource = await PrepareSourceAsync(thumbnailStream, cancellationToken).ConfigureAwait(false);

        try
        {
            var cacheKey = $"baseline-v2-{options.CoverSizePixels}-{options.JpegQuality}-{preparedSource.SourceHash}";
            var cached = await TryReadExistingAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                Touch(cached.FilePath);
                return cached with { WasCacheHit = true };
            }

            await using var decodeStream = new CancellationAwareStream(preparedSource.OpenRead(), cancellationToken);
            using var sourceBitmap = SKBitmap.Decode(decodeStream);
            cancellationToken.ThrowIfCancellationRequested();
            if (sourceBitmap is null)
            {
                throw new InvalidDataException("Thumbnail stream is not a supported image.");
            }

            using var image = CenterCropAndResize(sourceBitmap, options.CoverSizePixels);
            cancellationToken.ThrowIfCancellationRequested();
            var accentHex = CoverAccentExtractor.ExtractAccentHex(image);
            cancellationToken.ThrowIfCancellationRequested();
            var jpegBytes = EncodeJpeg(image, options.JpegQuality);
            cancellationToken.ThrowIfCancellationRequested();
            var coverId = Convert.ToHexString(SHA256.HashData(jpegBytes)).ToLowerInvariant();
            var finalPath = GetCoverPath(coverId);

            if (!File.Exists(finalPath) || new FileInfo(finalPath).Length == 0)
            {
                var tempPath = Path.Combine(temporaryDirectory, $"{Guid.NewGuid():N}.jpg");
                await File.WriteAllBytesAsync(tempPath, jpegBytes, cancellationToken).ConfigureAwait(false);
                File.Move(tempPath, finalPath, overwrite: true);
            }

            var result = new CoverProcessResult(coverId, finalPath, accentHex, jpegBytes.Length, WasCacheHit: false);
            await WriteIndexAsync(cacheKey, result, cancellationToken).ConfigureAwait(false);
            PruneCache();

            return result;
        }
        finally
        {
            preparedSource.Dispose();
        }
    }

    private static SKBitmap CenterCropAndResize(SKBitmap image, int size)
    {
        var square = Math.Min(image.Width, image.Height);
        var crop = new SKRectI(
            (image.Width - square) / 2,
            (image.Height - square) / 2,
            (image.Width + square) / 2,
            (image.Height + square) / 2);

        var output = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.White);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
        canvas.DrawBitmap(image, crop, new SKRect(0, 0, size, size), sampling);
        canvas.Flush();
        return output;
    }

    private static byte[] EncodeJpeg(SKBitmap image, int quality)
    {
        using var skImage = SKImage.FromBitmap(image);
        using var data = skImage.Encode(SKEncodedImageFormat.Jpeg, quality);
        if (data is null)
        {
            throw new InvalidOperationException("Failed to encode cover JPEG.");
        }

        return data.ToArray();
    }

    private async Task<PreparedSource> PrepareSourceAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            var startPosition = stream.Position;
            var sourceHash = await ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
            stream.Position = startPosition;
            return new PreparedSource(sourceHash, () => new NonDisposingStream(stream), disposeSource: null);
        }

        var tempPath = Path.Combine(temporaryDirectory, $"{Guid.NewGuid():N}.source");
        await using (var file = File.Create(tempPath))
        {
            using var sha256 = SHA256.Create();
            var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
            try
            {
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    sha256.TransformBlock(buffer, 0, read, null, 0);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                sha256.TransformFinalBlock([], 0, 0);
                return new PreparedSource(
                    Convert.ToHexString(sha256.Hash!).ToLowerInvariant(),
                    () => File.OpenRead(tempPath),
                    () => File.Delete(tempPath));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private static async Task<string> ComputeHashAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<CoverProcessResult?> TryReadExistingAsync(string sourceHash, CancellationToken cancellationToken)
    {
        var indexPath = GetIndexPath(sourceHash);
        if (!File.Exists(indexPath))
        {
            return null;
        }

        CoverCacheIndexEntry? entry = null;
        try
        {
            await using var stream = File.OpenRead(indexPath);
            if (stream.Length == 0)
            {
                try { File.Delete(indexPath); } catch { }
                return null;
            }

            entry = await JsonSerializer.DeserializeAsync<CoverCacheIndexEntry>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            try { File.Delete(indexPath); } catch { }
            return null;
        }

        if (entry is null || string.IsNullOrWhiteSpace(entry.CoverId))
        {
            try { File.Delete(indexPath); } catch { }
            return null;
        }

        var coverPath = GetCoverPath(entry.CoverId);
        if (!File.Exists(coverPath))
        {
            return null;
        }

        var fileInfo = new FileInfo(coverPath);
        if (fileInfo.Length == 0)
        {
            try { File.Delete(coverPath); } catch { }
            try { File.Delete(indexPath); } catch { }
            return null;
        }

        return new CoverProcessResult(
            entry.CoverId,
            coverPath,
            entry.AccentHex,
            fileInfo.Length,
            WasCacheHit: true);
    }

    private async Task WriteIndexAsync(string sourceHash, CoverProcessResult result, CancellationToken cancellationToken)
    {
        var entry = new CoverCacheIndexEntry(result.CoverId, result.AccentHex);
        var indexPath = GetIndexPath(sourceHash);
        var tempPath = Path.Combine(temporaryDirectory, $"{Guid.NewGuid():N}.json");
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, entry, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, indexPath, overwrite: true);
    }

    private void PruneCache()
    {
        var files = Directory
            .EnumerateFiles(coversDirectory, "*.jpg", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderBy(info => info.LastAccessTimeUtc)
            .ThenBy(info => info.LastWriteTimeUtc)
            .ToList();

        var totalBytes = files.Sum(info => info.Length);
        foreach (var file in files)
        {
            if (totalBytes <= options.MaxCacheBytes)
            {
                break;
            }

            totalBytes -= file.Length;
            file.Delete();
        }
    }

    private static void Touch(string path)
    {
        var now = DateTime.UtcNow;
        File.SetLastAccessTimeUtc(path, now);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path));
    }

    private string GetCoverPath(string coverId)
    {
        return Path.Combine(coversDirectory, $"{coverId}.jpg");
    }

    private string GetIndexPath(string sourceHash)
    {
        return Path.Combine(indexDirectory, $"{sourceHash}.json");
    }

    private sealed record CoverCacheIndexEntry(string CoverId, string? AccentHex);

    private sealed class PreparedSource(string sourceHash, Func<Stream> openRead, Action? disposeSource) : IDisposable
    {
        public string SourceHash { get; } = sourceHash;

        public Stream OpenRead()
        {
            return openRead();
        }

        public void Dispose()
        {
            disposeSource?.Invoke();
        }
    }

    private sealed class NonDisposingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush()
        {
            inner.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class CancellationAwareStream(Stream inner, CancellationToken cancellationToken) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set
            {
                cancellationToken.ThrowIfCancellationRequested();
                inner.Position = value;
            }
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = inner.Read(buffer, offset, count);
            cancellationToken.ThrowIfCancellationRequested();
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = inner.Read(buffer);
            cancellationToken.ThrowIfCancellationRequested();
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.Seek(offset, origin);
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
