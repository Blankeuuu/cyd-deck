namespace StreamDeck.Covers;

public sealed class CoverProcessingOptions
{
    public const int DefaultCoverSizePixels = 140;
    public const int DefaultJpegQuality = 80;
    public const long DefaultMaxCacheBytes = 256L * 1024L * 1024L;

    public required string CacheDirectory { get; init; }

    public int CoverSizePixels { get; init; } = DefaultCoverSizePixels;

    public int JpegQuality { get; init; } = DefaultJpegQuality;

    public long MaxCacheBytes { get; init; } = DefaultMaxCacheBytes;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CacheDirectory))
        {
            throw new InvalidOperationException("Cover cache directory is required.");
        }

        if (CoverSizePixels is < 32 or > 512)
        {
            throw new InvalidOperationException("Cover size must be between 32 and 512 pixels.");
        }

        if (JpegQuality is < 40 or > 95)
        {
            throw new InvalidOperationException("JPEG quality must be between 40 and 95.");
        }

        if (MaxCacheBytes < 1024)
        {
            throw new InvalidOperationException("Cover cache limit must be at least 1024 bytes.");
        }
    }
}
