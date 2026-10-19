using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace StreamDeck.WindowsAgent.Tray;

internal static class AppIconFactory
{
    internal static readonly Color BackgroundColor = Color.FromArgb(255, 0x16, 0x16, 0x16);
    internal static readonly Color PrimaryTileColor = Color.FromArgb(255, 0xDD, 0x31, 0x27);
    internal static readonly Color AccentTileColor = Color.FromArgb(255, 0x4E, 0xAB, 0x41);

    public static Icon CreateIcon()
    {
        using var bitmap = CreateBitmap(32);

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    internal static Bitmap CreateBitmap(int size = 32)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.Clear(Color.Transparent);

        var pad = size * (1f / 32f);
        var bodySize = size - (2f * pad);
        var scale = bodySize / 200f;

        using var background = new SolidBrush(BackgroundColor);
        graphics.FillRoundedRectangle(
            background,
            new RectangleF(pad, pad, bodySize, bodySize),
            25f * scale);

        var tileSize = 60f * scale;
        var tileRadius = 12f * scale;
        var p1 = pad + (37f * scale);
        var p2 = pad + (103f * scale);

        using var primary = new SolidBrush(PrimaryTileColor);
        graphics.FillRoundedRectangle(primary, new RectangleF(p1, p1, tileSize, tileSize), tileRadius);
        graphics.FillRoundedRectangle(primary, new RectangleF(p2, p1, tileSize, tileSize), tileRadius);
        graphics.FillRoundedRectangle(primary, new RectangleF(p1, p2, tileSize, tileSize), tileRadius);

        using var accent = new SolidBrush(AccentTileColor);
        graphics.FillRoundedRectangle(accent, new RectangleF(p2, p2, tileSize, tileSize), tileRadius);

        return bitmap;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}

file static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF bounds, float radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2f;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
