using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace StreamDeck.WindowsAgent.Tray;

internal static class AppIconFactory
{
    public static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var background = new LinearGradientBrush(
            new Rectangle(0, 0, 32, 32),
            Color.FromArgb(255, 34, 39, 52),
            Color.FromArgb(255, 12, 15, 22),
            LinearGradientMode.ForwardDiagonal);
        graphics.FillRoundedRectangle(background, new RectangleF(1.5f, 1.5f, 29f, 29f), 7f);

        using var accent = new SolidBrush(Color.FromArgb(255, 212, 59, 72));
        graphics.FillRoundedRectangle(accent, new RectangleF(7f, 7f, 8f, 8f), 2f);
        graphics.FillRoundedRectangle(accent, new RectangleF(17f, 7f, 8f, 8f), 2f);
        graphics.FillRoundedRectangle(accent, new RectangleF(7f, 17f, 8f, 8f), 2f);

        using var muted = new SolidBrush(Color.FromArgb(255, 74, 222, 128));
        graphics.FillRoundedRectangle(muted, new RectangleF(17f, 17f, 8f, 8f), 2f);

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
