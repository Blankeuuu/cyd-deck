using System.Drawing;
using System.Drawing.Imaging;
using StreamDeck.WindowsAgent.Tray;

namespace StreamDeck.WindowsAgent.BrandingTests;

internal static class IconGenerator
{
    public static readonly int[] StandardIcoSizes = [16, 24, 32, 48, 64, 128, 256];

    public static void GenerateAll(string assetsDir)
    {
        Directory.CreateDirectory(assetsDir);

        var pngPath = Path.Combine(assetsDir, "cyd-deck.png");
        var icoPath = Path.Combine(assetsDir, "cyd-deck.ico");

        GeneratePng(pngPath, 300);
        GenerateIco(icoPath, StandardIcoSizes);
    }

    public static void GeneratePng(string outputPath, int size = 300)
    {
        using var bitmap = AppIconFactory.CreateCanvasBitmap(size);
        bitmap.Save(outputPath, ImageFormat.Png);
    }

    public static void GenerateIco(string outputPath, int[] sizes)
    {
        var icoBytes = BuildIcoBytes(sizes);
        File.WriteAllBytes(outputPath, icoBytes);
    }

    public static byte[] BuildIcoBytes(int[] sizes)
    {
        var frames = new List<(int Size, byte[] PngBytes)>();
        foreach (var size in sizes)
        {
            using var bmp = AppIconFactory.CreateBitmap(size);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            frames.Add((size, ms.ToArray()));
        }

        using var outputMs = new MemoryStream();
        using var writer = new BinaryWriter(outputMs);

        // ICONDIR Header (6 bytes)
        writer.Write((ushort)0); // idReserved
        writer.Write((ushort)1); // idType: 1 = Icon
        writer.Write((ushort)frames.Count); // idCount

        // ICONDIRENTRY (16 bytes each)
        var offset = 6 + (frames.Count * 16);
        foreach (var frame in frames)
        {
            var bWidth = frame.Size >= 256 ? (byte)0 : (byte)frame.Size;
            var bHeight = frame.Size >= 256 ? (byte)0 : (byte)frame.Size;

            writer.Write(bWidth);
            writer.Write(bHeight);
            writer.Write((byte)0); // bColorCount
            writer.Write((byte)0); // bReserved
            writer.Write((ushort)1); // wPlanes
            writer.Write((ushort)32); // wBitCount
            writer.Write((uint)frame.PngBytes.Length); // dwBytesInRes
            writer.Write((uint)offset); // dwImageOffset

            offset += frame.PngBytes.Length;
        }

        // Image data
        foreach (var frame in frames)
        {
            writer.Write(frame.PngBytes);
        }

        return outputMs.ToArray();
    }
}
