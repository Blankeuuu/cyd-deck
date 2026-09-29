using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

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

        Directory.CreateDirectory(coversDirectory);
        Directory.CreateDirectory(indexDirectory);
        Directory.CreateDirectory(temporaryDirectory);

        var preparedSource = await PrepareSourceAsync(thumbnailStream, cancellationToken).ConfigureAwait(false);

        try
        {
            var cached = await TryReadExistingAsync(preparedSource.SourceHash, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                Touch(cached.FilePath);
                return cached with { WasCacheHit = true };
            }

            await using var decodeStream = preparedSource.OpenRead();
            using var image = await Image.LoadAsync<Rgba32>(decodeStream, cancellationToken).ConfigureAwait(false);

            CenterCropAndResize(image, options.CoverSizePixels);
            var accentHex = CoverAccentExtractor.ExtractAccentHex(image);

            await using var jpegStream = new MemoryStream();
            await image.SaveAsJpegAsync(
                jpegStream,
                new JpegEncoder { Quality = options.JpegQuality },
                cancellationToken).ConfigureAwait(false);

            var jpegBytes = jpegStream.ToArray();
            var coverId = Convert.ToHexString(SHA256.HashData(jpegBytes)).ToLowerInvariant();
            var finalPath = GetCoverPath(coverId);

            if (!File.Exists(finalPath))
            {
                var tempPath = Path.Combine(temporaryDirectory, $"{Guid.NewGuid():N}.jpg");
                await File.WriteAllBytesAsync(tempPath, jpegBytes, cancellationToken).ConfigureAwait(false);
                File.Move(tempPath, finalPath);
            }

            var result = new CoverProcessResult(coverId, finalPath, accentHex, jpegBytes.Length, WasCacheHit: false);
            await WriteIndexAsync(preparedSource.SourceHash, result, cancellationToken).ConfigureAwait(false);
            PruneCache();

            return result;
        }
        finally
        {
            preparedSource.Dispose();
        }
    }

    private static void CenterCropAndResize(Image<Rgba32> image, int size)
    {
        var square = Math.Min(image.Width, image.Height);
        var crop = new Rectangle(
            (image.Width - square) / 2,
            (image.Height - square) / 2,
            square,
            square);

        image.Mutate(context => context.Crop(crop).Resize(size, size));
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

        await using var stream = File.OpenRead(indexPath);
        var entry = await JsonSerializer.DeserializeAsync<CoverCacheIndexEntry>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return null;
        }

        var coverPath = GetCoverPath(entry.CoverId);
        if (!File.Exists(coverPath))
        {
            return null;
        }

        return new CoverProcessResult(
            entry.CoverId,
            coverPath,
            entry.AccentHex,
            new FileInfo(coverPath).Length,
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
}
