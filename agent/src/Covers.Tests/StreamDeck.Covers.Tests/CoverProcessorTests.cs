using System.Security.Cryptography;
using StreamDeck.Covers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace StreamDeck.Covers.Tests;

public sealed class CoverProcessorTests
{
    [Fact]
    public async Task MissingThumbnailReturnsNull()
    {
        var processor = CreateProcessor();

        var result = await processor.ProcessAsync(null, TestContext.Current.CancellationToken);

        Assert.Null(result);
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

        using var image = await Image.LoadAsync<Rgba32>(result.FilePath, TestContext.Current.CancellationToken);
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
        Assert.InRange(accent.R, 205, 220);
        Assert.InRange(accent.G, 50, 70);
        Assert.InRange(accent.B, 65, 80);
    }

    [Fact]
    public async Task AccentIsNullForUnsaturatedCover()
    {
        await using var source = CreateSolidSource(new Rgba32(128, 128, 128));
        var processor = CreateProcessor();

        var result = await processor.ProcessAsync(source, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Null(result.AccentHex);
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
        using var image = new Image<Rgba32>(300, 100);
        Fill(image, new Rgba32(230, 230, 230));
        FillRectangle(image, new Rectangle(100, 0, 100, 100), new Rgba32(42, 88, 210));

        return SaveJpeg(image);
    }

    private static MemoryStream CreateAccentSource()
    {
        using var image = new Image<Rgba32>(140, 140);
        Fill(image, new Rgba32(245, 245, 245));
        FillRectangle(image, new Rectangle(0, 0, 24, 140), new Rgba32(12, 12, 12));
        FillRectangle(image, new Rectangle(24, 0, 24, 140), new Rgba32(120, 120, 120));
        FillRectangle(image, new Rectangle(48, 0, 92, 140), new Rgba32(212, 59, 72));

        return SaveJpeg(image);
    }

    private static MemoryStream CreateSolidSource(Rgba32 color)
    {
        using var image = new Image<Rgba32>(140, 140);
        Fill(image, color);
        return SaveJpeg(image);
    }

    private static void Fill(Image<Rgba32> image, Rgba32 color)
    {
        FillRectangle(image, new Rectangle(0, 0, image.Width, image.Height), color);
    }

    private static void FillRectangle(Image<Rgba32> image, Rectangle rectangle, Rgba32 color)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = rectangle.Top; y < rectangle.Bottom; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = rectangle.Left; x < rectangle.Right; x++)
                {
                    row[x] = color;
                }
            }
        });
    }

    private static MemoryStream SaveJpeg(Image<Rgba32> image)
    {
        var stream = new MemoryStream();
        image.SaveAsJpeg(stream, new JpegEncoder { Quality = 95 });
        stream.Position = 0;
        return stream;
    }

    private static Rgba32 ParseHex(string value)
    {
        return new Rgba32(
            Convert.ToByte(value[1..3], 16),
            Convert.ToByte(value[3..5], 16),
            Convert.ToByte(value[5..7], 16));
    }

    private sealed class NonSeekableMemoryStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override bool CanSeek => false;
    }
}
