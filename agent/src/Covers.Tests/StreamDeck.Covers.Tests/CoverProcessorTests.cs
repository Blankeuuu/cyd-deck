using System.Security.Cryptography;
using StreamDeck.Covers;
using SkiaSharp;
using Xunit;

namespace StreamDeck.Covers.Tests;

public sealed class CoverProcessorTests
{
    [Fact]
    public async Task CacheSeparatesDeviceSizeAndAlwaysProducesBaselineJpeg()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cyd-cover-profile-{Guid.NewGuid():N}");
        await using var source = CreateWideSource();
        var large = new CoverProcessor(new CoverProcessingOptions { CacheDirectory = directory, CoverSizePixels = 140 });
        await large.ProcessAsync(source, TestContext.Current.CancellationToken);
        source.Position = 0;
        var target = new CoverProcessor(new CoverProcessingOptions { CacheDirectory = directory, CoverSizePixels = 128, JpegQuality = 80 });
        var result = await target.ProcessAsync(source, TestContext.Current.CancellationToken);
        using var bitmap = SKBitmap.Decode(result!.FilePath);
        Assert.Equal(128, bitmap.Width);
        Assert.Equal(128, bitmap.Height);
        var bytes = await File.ReadAllBytesAsync(result.FilePath, TestContext.Current.CancellationToken);
        Assert.Contains(Enumerable.Range(0, bytes.Length - 1), i => bytes[i] == 0xff && bytes[i + 1] == 0xc0);
        Assert.DoesNotContain(Enumerable.Range(0, bytes.Length - 1), i => bytes[i] == 0xff && bytes[i + 1] == 0xc2);
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task ThumbnailFormatsBecomeDeviceSizedBaselineJpeg(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(256, 180);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 90);
        await using var source = new MemoryStream(encoded.ToArray());
        var processor = new CoverProcessor(new CoverProcessingOptions
        {
            CacheDirectory = Path.Combine(Path.GetTempPath(), $"cyd-cover-format-{Guid.NewGuid():N}"), CoverSizePixels = 128
        });
        var result = await processor.ProcessAsync(source, TestContext.Current.CancellationToken);
        using var output = SKBitmap.Decode(result!.FilePath);
        Assert.Equal(128, output.Width);
        Assert.Equal(128, output.Height);
        var bytes = await File.ReadAllBytesAsync(result.FilePath, TestContext.Current.CancellationToken);
        Assert.Contains(Enumerable.Range(0, bytes.Length - 1), i => bytes[i] == 0xff && bytes[i + 1] == 0xc0);
    }

    [Fact]
    public async Task MissingThumbnailReturnsNull()
    {
        var processor = CreateProcessor();

        var result = await processor.ProcessAsync(null, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task EmptyThumbnailStreamReturnsNull()
    {
        var processor = CreateProcessor();
        await using var source = new MemoryStream();

        var result = await processor.ProcessAsync(source, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task CorruptedIndexFileIsDeletedAndCoverRegenerated()
    {
        var dir = Path.Combine(Path.GetTempPath(), "streamdeck-covers-tests", Guid.NewGuid().ToString("N"));
        var processor = new CoverProcessor(new CoverProcessingOptions { CacheDirectory = dir, CoverSizePixels = 128 });
        await using var source1 = CreateWideSource();
        var result1 = await processor.ProcessAsync(source1, TestContext.Current.CancellationToken);
        Assert.NotNull(result1);

        // Corrupt all files in index
        var indexFiles = Directory.GetFiles(Path.Combine(dir, "index"));
        foreach (var file in indexFiles)
        {
            await File.WriteAllTextAsync(file, "{ bad-json-content }", TestContext.Current.CancellationToken);
        }

        // Processing again should gracefully handle bad index and regenerate
        await using var source2 = CreateWideSource();
        var result2 = await processor.ProcessAsync(source2, TestContext.Current.CancellationToken);
        Assert.NotNull(result2);
        Assert.Equal(result1.CoverId, result2.CoverId);
    }

    [Fact]
    public async Task ZeroByteCoverFileIsDeletedAndCoverRegenerated()
    {
        var dir = Path.Combine(Path.GetTempPath(), "streamdeck-covers-tests", Guid.NewGuid().ToString("N"));
        var processor = new CoverProcessor(new CoverProcessingOptions { CacheDirectory = dir, CoverSizePixels = 128 });
        await using var source1 = CreateWideSource();
        var result1 = await processor.ProcessAsync(source1, TestContext.Current.CancellationToken);
        Assert.NotNull(result1);

        // Zero-out the cover file
        await File.WriteAllBytesAsync(result1.FilePath, [], TestContext.Current.CancellationToken);

        // Next read should invalidate the zero-byte file and regenerate
        await using var source2 = CreateWideSource();
        var result2 = await processor.ProcessAsync(source2, TestContext.Current.CancellationToken);
        Assert.NotNull(result2);
        Assert.True(new FileInfo(result2.FilePath).Length > 0);
    }

    [Fact]
    public async Task ProcessCreatesSquareJpegWithSha256Id()
    {
        var processor = CreateProcessor();
        await using var source = CreateWideSource();

        var result = await processor.ProcessAsync(source, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.False(result.WasCacheHit);
        Assert.True(File.Exists(result.FilePath));
        Assert.Equal($"{result.CoverId}.jpg", Path.GetFileName(result.FilePath));

        var bytes = await File.ReadAllBytesAsync(result.FilePath, TestContext.Current.CancellationToken);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), result.CoverId);

        using var image = SKBitmap.Decode(result.FilePath);
        Assert.NotNull(image);
        Assert.Equal(140, image.Width);
        Assert.Equal(140, image.Height);
        Assert.StartsWith("#", result.AccentHex, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReusedSourceHitsCacheWithoutRewritingCover()
    {
        var processor = CreateProcessor();
        var sourceBytes = CreateWideSource().ToArray();

        await using var firstSource = new MemoryStream(sourceBytes);
        var first = await processor.ProcessAsync(firstSource, TestContext.Current.CancellationToken);
        Assert.NotNull(first);
        var firstWriteTime = File.GetLastWriteTimeUtc(first.FilePath);

        await using var secondSource = new NonSeekableMemoryStream(sourceBytes);
        var second = await processor.ProcessAsync(secondSource, TestContext.Current.CancellationToken);

        Assert.NotNull(second);
        Assert.True(second.WasCacheHit);
        Assert.Equal(first.CoverId, second.CoverId);
        Assert.Equal(first.FilePath, second.FilePath);
        Assert.Equal(firstWriteTime, File.GetLastWriteTimeUtc(second.FilePath));
    }

    [Fact]
    public async Task SeekableSourceStreamRemainsOpenForCaller()
    {
        var processor = CreateProcessor();
        await using var source = CreateWideSource();

        await processor.ProcessAsync(source, TestContext.Current.CancellationToken);

        Assert.True(source.CanRead);
        source.Position = 0;
        Assert.True(source.ReadByte() >= 0);
    }

    [Fact]
    public async Task AccentSkipsWhiteBlackAndGrayWhenSaturatedColorExists()
    {
        await using var source = CreateAccentSource();
        var processor = CreateProcessor();

        var result = await processor.ProcessAsync(source, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(result.AccentHex);
        var accent = ParseHex(result.AccentHex);
        Assert.InRange(accent.Red, 205, 220);
        Assert.InRange(accent.Green, 50, 70);
        Assert.InRange(accent.Blue, 65, 80);
    }

    [Fact]
    public async Task AccentIsNullForUnsaturatedCover()
    {
        await using var source = CreateSolidSource(new SKColor(128, 128, 128));
        var processor = CreateProcessor();

        var result = await processor.ProcessAsync(source, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Null(result.AccentHex);
    }

    [Fact]
    public async Task TransparentPixelsAreCompositedAgainstWhiteBeforeJpegEncoding()
    {
        var processor = CreateProcessor();
        await using var source = CreateTransparentPngSource();

        var result = await processor.ProcessAsync(source, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        using var image = SKBitmap.Decode(result.FilePath);
        Assert.NotNull(image);
        var transparentRegion = image.GetPixel(10, 10);
        Assert.InRange(transparentRegion.Red, 245, 255);
        Assert.InRange(transparentRegion.Green, 245, 255);
        Assert.InRange(transparentRegion.Blue, 245, 255);
    }

    [Fact]
    public async Task CancellationDuringImageDecodeIsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        await using var source = new CancelOnDecodeStream(CreateWideSource().ToArray(), cancellation);
        var processor = CreateProcessor();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => processor.ProcessAsync(source, cancellation.Token));
    }

    [Fact]
    public async Task UnsupportedImageDataThrowsInvalidDataException()
    {
        var processor = CreateProcessor();
        await using var source = new MemoryStream([1, 2, 3, 4, 5]);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => processor.ProcessAsync(source, TestContext.Current.CancellationToken));
    }

    private static CoverProcessor CreateProcessor()
    {
        return new CoverProcessor(new CoverProcessingOptions
        {
            CacheDirectory = Path.Combine(Path.GetTempPath(), "streamdeck-covers-tests", Guid.NewGuid().ToString("N")),
            MaxCacheBytes = 10 * 1024 * 1024
        });
    }

    private static MemoryStream CreateWideSource()
    {
        using var image = new SKBitmap(new SKImageInfo(300, 100, SKColorType.Rgba8888, SKAlphaType.Premul));
        Fill(image, new SKColor(230, 230, 230));
        FillRectangle(image, new SKRectI(100, 0, 200, 100), new SKColor(42, 88, 210));

        return SaveJpeg(image);
    }

    private static MemoryStream CreateAccentSource()
    {
        using var image = new SKBitmap(new SKImageInfo(140, 140, SKColorType.Rgba8888, SKAlphaType.Premul));
        Fill(image, new SKColor(245, 245, 245));
        FillRectangle(image, new SKRectI(0, 0, 24, 140), new SKColor(12, 12, 12));
        FillRectangle(image, new SKRectI(24, 0, 48, 140), new SKColor(120, 120, 120));
        FillRectangle(image, new SKRectI(48, 0, 140, 140), new SKColor(212, 59, 72));

        return SaveJpeg(image);
    }

    private static MemoryStream CreateSolidSource(SKColor color)
    {
        using var image = new SKBitmap(new SKImageInfo(140, 140, SKColorType.Rgba8888, SKAlphaType.Premul));
        Fill(image, color);
        return SaveJpeg(image);
    }

    private static MemoryStream CreateTransparentPngSource()
    {
        using var image = new SKBitmap(new SKImageInfo(140, 140, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(image);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = new SKColor(210, 45, 55), Style = SKPaintStyle.Fill };
        canvas.DrawRect(new SKRectI(40, 40, 100, 100), paint);
        canvas.Flush();

        var stream = new MemoryStream();
        using var skImage = SKImage.FromBitmap(image);
        using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
        Assert.NotNull(data);
        data.SaveTo(stream);
        stream.Position = 0;
        return stream;
    }

    private static void Fill(SKBitmap image, SKColor color)
    {
        FillRectangle(image, new SKRectI(0, 0, image.Width, image.Height), color);
    }

    private static void FillRectangle(SKBitmap image, SKRectI rectangle, SKColor color)
    {
        using var canvas = new SKCanvas(image);
        using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
        canvas.DrawRect(rectangle, paint);
        canvas.Flush();
    }

    private static MemoryStream SaveJpeg(SKBitmap image)
    {
        var stream = new MemoryStream();
        using var skImage = SKImage.FromBitmap(image);
        using var data = skImage.Encode(SKEncodedImageFormat.Jpeg, 95);
        Assert.NotNull(data);
        data.SaveTo(stream);
        stream.Position = 0;
        return stream;
    }

    private static SKColor ParseHex(string value)
    {
        return new SKColor(
            Convert.ToByte(value[1..3], 16),
            Convert.ToByte(value[3..5], 16),
            Convert.ToByte(value[5..7], 16));
    }

    private sealed class NonSeekableMemoryStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override bool CanSeek => false;
    }

    private sealed class CancelOnDecodeStream(byte[] buffer, CancellationTokenSource cancellation) : MemoryStream(buffer)
    {
        private bool decodeStarted;

        public override long Position
        {
            get => base.Position;
            set
            {
                if (value == 0 && base.Position == Length)
                {
                    decodeStarted = true;
                }

                base.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            CancelWhenDecoding();
            return base.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            CancelWhenDecoding();
            return base.Read(buffer);
        }

        private void CancelWhenDecoding()
        {
            if (decodeStarted)
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }
        }
    }
}
