using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace StreamDeck.Covers;

public static class CoverAccentExtractor
{
    public static string? ExtractAccentHex(Image<Rgba32> image)
    {
        Dictionary<int, Bucket> buckets = [];

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    if (pixel.A < 32)
                    {
                        continue;
                    }

                    var color = ToCandidateColor(pixel);
                    if (color is null)
                    {
                        continue;
                    }

                    var key = Quantize(color.Value.R, color.Value.G, color.Value.B);
                    if (!buckets.TryGetValue(key, out var bucket))
                    {
                        bucket = new Bucket();
                        buckets.Add(key, bucket);
                    }

                    bucket.Add(color.Value);
                }
            }
        });

        if (buckets.Count == 0)
        {
            return null;
        }

        var best = buckets
            .Values
            .OrderByDescending(bucket => bucket.Score)
            .First()
            .Average();

        return $"#{best.R:X2}{best.G:X2}{best.B:X2}";
    }

    private static CandidateColor? ToCandidateColor(Rgba32 pixel)
    {
        var r = pixel.R / 255d;
        var g = pixel.G / 255d;
        var b = pixel.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var saturation = max <= 0 ? 0 : (max - min) / max;
        var luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;

        if (luminance is < 0.08 or > 0.92)
        {
            return null;
        }

        if (saturation < 0.22)
        {
            return null;
        }

        return new CandidateColor(pixel.R, pixel.G, pixel.B, saturation);
    }

    private static int Quantize(byte r, byte g, byte b)
    {
        return (r >> 3) << 10 | (g >> 3) << 5 | (b >> 3);
    }

    private readonly record struct CandidateColor(byte R, byte G, byte B, double Saturation);

    private sealed class Bucket
    {
        private long r;
        private long g;
        private long b;
        private double saturation;

        public int Count { get; private set; }

        public double Score => Count * (0.65d + saturation / Count);

        public void Add(CandidateColor color)
        {
            Count++;
            r += color.R;
            g += color.G;
            b += color.B;
            saturation += color.Saturation;
        }

        public Rgba32 Average()
        {
            return new Rgba32(
                (byte)(r / Count),
                (byte)(g / Count),
                (byte)(b / Count));
        }
    }
}
