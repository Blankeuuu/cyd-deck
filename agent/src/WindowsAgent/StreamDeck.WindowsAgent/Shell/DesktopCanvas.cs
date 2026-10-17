using System.Drawing.Drawing2D;
using StreamDeck.Configuration;
using StreamDeck.Media;
using Shortcut = StreamDeck.Shortcuts.ShortcutDefinition;

namespace StreamDeck.WindowsAgent.Shell;

internal sealed class DesktopCanvas : Control
{
    internal string Page = "Deck";
    internal string ShortcutsLayout = "auto";
    internal Shortcut?[] Slots = new Shortcut?[6];
    internal AgentStatusSnapshot? Snapshot;
    internal Image? Artwork;
    internal Color Accent = Color.FromArgb(126, 175, 227);
    internal Color DisplayBackground = Color.FromArgb(16, 18, 22);
    internal string TileTheme = "control-center";
    internal int SelectedSlot = -1;
    internal bool Clock24Hour = true;
    internal Action<int>? SelectSlot;
    internal Action<int>? UnassignSlot;
    internal Action<int, int>? MoveSlot;
    private readonly List<(int Slot, RectangleF Bounds)> hits = [];
    private int hovered = -1;
    private int dragSlot = -1;
    private Point dragStart;
    private RectangleF viewport;
    internal DesktopCanvas()
    {
        DoubleBuffered = true; ResizeRedraw = true; TabStop = true; AccessibleName = "CYD Display Preview"; AccessibleRole = AccessibleRole.Graphic; AllowDrop = true;
        DragEnter += (_, e) => { if (e.Data?.GetDataPresent(typeof(int)) == true) e.Effect = DragDropEffects.Move; };
        DragDrop += (_, e) => { var target = Hit(PointToClient(new Point(e.X, e.Y))); if (target >= 0 && e.Data?.GetData(typeof(int)) is int source) MoveSlot?.Invoke(source, target); };
    }
    private PointF Logical(Point point) => viewport.Width <= 0 ? PointF.Empty : new((point.X - viewport.X) * 320 / viewport.Width, (point.Y - viewport.Y) * 240 / viewport.Height);
    private int Hit(Point point) { var logical = Logical(point); return hits.FirstOrDefault(hit => hit.Bounds.Contains(logical), (Slot: -1, Bounds: RectangleF.Empty)).Slot; }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (SelectSlot is not null) Focus(); dragSlot = Hit(e.Location); dragStart = e.Location; }
    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e); var hit = Hit(e.Location); if (hovered != hit) { hovered = hit; Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        if (e.Button == MouseButtons.Left && dragSlot >= 0 && (Math.Abs(e.X - dragStart.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - dragStart.Y) > SystemInformation.DragSize.Height)) { DoDragDrop(dragSlot, DragDropEffects.Move); dragSlot = -1; }
    }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = -1; Invalidate(); }
    protected override void OnMouseClick(MouseEventArgs e) {
        base.OnMouseClick(e); var hit = Hit(e.Location); if (hit < 0) return;
        var bounds = hits.First(h => h.Slot == hit).Bounds; var point = Logical(e.Location);
        if (Slots[hit] is not null && point.X > bounds.Right - 20 && point.Y < bounds.Top + 20) UnassignSlot?.Invoke(hit); else SelectSlot?.Invoke(hit);
    }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode is Keys.Right or Keys.Down or Keys.Left or Keys.Up) { var index = Math.Clamp(SelectedSlot + (e.KeyCode is Keys.Right or Keys.Down ? 1 : -1), 0, Page == "Home" ? 3 : 5); SelectSlot?.Invoke(index); e.Handled = true; } if (e.KeyCode == Keys.Delete && SelectedSlot >= 0) { UnassignSlot?.Invoke(SelectedSlot); e.Handled = true; } }
    private static GraphicsPath Rounded(RectangleF bounds, float radius) {
        var p = new GraphicsPath(); var d = radius * 2; p.AddArc(bounds.X, bounds.Y, d, d, 180, 90); p.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90); p.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90); p.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
    }
    private static void Box(Graphics g, RectangleF rect, Color color, float radius = 12) { using var path = Rounded(rect, radius); using var brush = new SolidBrush(color); g.FillPath(brush, path); }
    private static void DrawText(Graphics g, string value, RectangleF bounds, float size, Color color, bool bold = false, bool center = false) {
        using var font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); using var brush = new SolidBrush(color); using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, Alignment = center ? StringAlignment.Center : StringAlignment.Near, LineAlignment = StringAlignment.Center }; g.DrawString(value, font, brush, bounds, format);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(BackColor);
        float scale = Math.Min((Width - 16) / 320f, (Height - 16) / 240f); if (scale <= 0) return;
        viewport = new RectangleF((Width - 320 * scale) / 2, (Height - 240 * scale) / 2, 320 * scale, 240 * scale);
        g.TranslateTransform(viewport.X, viewport.Y); g.ScaleTransform(scale, scale);
        Box(g, new RectangleF(-4, -4, 328, 248), Color.FromArgb(5, 7, 10), 16); Box(g, new RectangleF(0, 0, 320, 240), DisplayBackground, 12); hits.Clear();
        if (Page == "Home") {
            Box(g, new RectangleF(8, 8, 148, 84), Color.FromArgb(28, 32, 40));
            if (Artwork is not null) { var saved = g.Save(); using var clip = Rounded(new RectangleF(8, 8, 148, 84), 12); g.SetClip(clip); g.DrawImage(Artwork, new Rectangle(8, 8, 148, 84)); using var scrim = new SolidBrush(Color.FromArgb(165, 0, 0, 0)); g.FillRectangle(scrim, 8, 8, 148, 84); g.Restore(saved); }
            DrawText(g, Snapshot?.CurrentMedia?.Title ?? "Ready to Play", new RectangleF(16, 16, 130, 24), 17, Color.White, true); DrawText(g, Snapshot?.CurrentMedia?.Artist ?? "No media session", new RectangleF(16, 42, 130, 20), 12, Color.FromArgb(225, 228, 235));
            DrawText(g, Snapshot?.CurrentMedia?.PlaybackState == MediaPlaybackState.Playing ? "Ⅱ" : "▶", new RectangleF(112, 62, 32, 24), 18, Color.White, true, true);
            Box(g, new RectangleF(164, 8, 148, 84), Color.FromArgb(28, 32, 40));
            DrawText(g, DateTime.Now.ToString(Clock24Hour ? "HH:mm" : "h:mm"), new RectangleF(172, 20, 132, 44), 40, Color.White, true, true); DrawText(g, DateTime.Now.ToString("ddd, MMM d", System.Globalization.CultureInfo.InvariantCulture), new RectangleF(172, 62, 132, 18), 14, Color.FromArgb(161, 161, 170), false, true);
            for (var i = 0; i < 4; ++i) Tile(g, i, new RectangleF(8 + i % 2 * 156, 100 + i / 2 * 50, 148, 44), true);
        } else if (Page == "Deck") {
            var count = ShortcutsLayout switch { "hero-1" => 1, "split-2" => 2, "quad-4" => 4, "grid-6" => 6, _ => Slots.Select((s, i) => s is null ? 0 : i + 1).Max() switch { <= 1 => 1, 2 => 2, <= 4 => 4, _ => 6 } };
            for (var i = 0; i < count; ++i) {
                var columns = count == 1 ? 1 : count == 6 ? 3 : 2; var rows = count <= 2 ? 1 : 2;
                var w = (296 - (columns - 1) * 8f) / columns; var h = (180 - (rows - 1) * 8f) / rows;
                Tile(g, i, new RectangleF(12 + i % columns * (w + 8), 12 + i / columns * (h + 8), w, h), false);
            }
        } else if (Page == "Media") {
            Box(g, new RectangleF(12, 12, 128, 128), Color.FromArgb(28, 32, 40));
            if (Artwork is not null) { var saved = g.Save(); using var clip = Rounded(new RectangleF(12, 12, 128, 128), 12); g.SetClip(clip); g.DrawImage(Artwork, new Rectangle(12, 12, 128, 128)); g.Restore(saved); } else DrawText(g, "♫", new RectangleF(12, 12, 128, 128), 42, Accent, false, true);
            DrawText(g, Snapshot?.CurrentMedia?.Title ?? "Ready to Play", new RectangleF(152, 12, 156, 28), 20, Color.White, true); DrawText(g, Snapshot?.CurrentMedia?.Artist ?? "No media session", new RectangleF(152, 42, 156, 24), 14, Color.LightGray);
            DrawText(g, "◀     " + (Snapshot?.CurrentMedia?.PlaybackState == MediaPlaybackState.Playing ? "Ⅱ" : "▶") + "     ▶", new RectangleF(152, 112, 156, 38), 20, Color.White, true, true);
            Box(g, new RectangleF(76, 178, 174, 6), Color.FromArgb(50, 54, 62), 3); Box(g, new RectangleF(76, 178, Math.Max(6, 174 * (Snapshot?.CurrentAudio?.VolumePercent ?? 0) / 100f), 6), Accent, 3); DrawText(g, (Snapshot?.CurrentAudio?.VolumePercent ?? 0) + "%", new RectangleF(264, 164, 44, 30), 14, Color.White, false, true); DrawText(g, "♪", new RectangleF(12, 164, 48, 30), 20, Color.White, false, true);
        } else {
            var stats = Snapshot?.SystemTelemetry; var values = new[] { stats is null ? "—" : $"{stats.CpuPercent:0}%", stats is null ? "—" : $"{stats.RamPercent:0}%", stats is null || !stats.GpuAvailable ? "—" : $"{stats.GpuPercent:0}%", stats is null || !stats.TempAvailable ? "—" : $"{stats.TempCelsius:0}°" };
            for (var i = 0; i < 4; ++i) { var box = new RectangleF(12 + i % 2 * 152, 12 + i / 2 * 94, 144, 86); Box(g, box, Color.FromArgb(28, 32, 40)); DrawText(g, new[] { "CPU", "RAM", "GPU", "TEMP" }[i], new RectangleF(box.X + 10, box.Y + 6, 124, 20), 12, Color.LightGray); DrawText(g, values[i], new RectangleF(box.X + 10, box.Y + 28, 124, 48), 28, Color.White, true); }
        }
        Box(g, new RectangleF(8, 202, 304, 34), Color.FromArgb(30, 34, 42), 17);
        var titles = new[] { DateTime.Now.ToString(Clock24Hour ? "HH:mm" : "h:mm"), "Deck", "Media", "PC", "⚙" }; var widths = new[] { 58, 54, 58, 38, 30 }; int x = 29;
        for (var i = 0; i < titles.Length; ++i) { bool selected = new[] { "Home", "Deck", "Media", "PC", "Info" }[i] == Page; if (selected) Box(g, new RectangleF(x, 205, widths[i], 28), Accent, 14); DrawText(g, titles[i], new RectangleF(x, 205, widths[i], 28), 14, Color.White, selected, true); x += widths[i] + 6; }
        g.ResetTransform();
    }
    private void Tile(Graphics g, int index, RectangleF bounds, bool compact)
    {
        hits.Add((index, bounds)); var shortcut = Slots[index];
        var color = shortcut?.Color is { } configured ? ColorTranslator.FromHtml(configured) : TileTheme == "accent-tint" ? Color.FromArgb(Accent.R / 4 + 15, Accent.G / 4 + 15, Accent.B / 4 + 15) : Color.FromArgb(30, 34, 42);
        Box(g, bounds, color); var textColor = color.GetBrightness() > .57 ? Color.FromArgb(16, 18, 22) : Color.FromArgb(244, 246, 248);
        if (SelectedSlot == index || hovered == index) { using var path = Rounded(bounds, 12); using var pen = new Pen(Accent, SelectedSlot == index ? 2 : 1); g.DrawPath(pen, path); }
        bool icon = shortcut is not null && shortcut.DisplayMode != "label"; bool label = shortcut is null || shortcut.DisplayMode != "icon";
        if (icon) { var x = compact && label ? bounds.X + 12 : bounds.X + (bounds.Width - 24) / 2; var y = compact || !label ? bounds.Y + (bounds.Height - 24) / 2 : bounds.Y + (bounds.Height - 50) / 2; ShortcutIconCatalog.Draw(g, shortcut!.Icon, shortcut.IconData, Rectangle.Round(new RectangleF(x, y, 24, 24)), textColor); }
        if (label) { var box = compact ? new RectangleF(bounds.X + (icon ? 42 : 8), bounds.Y + 5, bounds.Width - (icon ? 50 : 16), 34) : new RectangleF(bounds.X + 6, icon ? bounds.Y + bounds.Height / 2 + 4 : bounds.Y + (bounds.Height - 38) / 2, bounds.Width - 12, 38); DrawText(g, shortcut?.Name ?? "Assign Shortcut", box, 14, textColor, false, !compact); }
        if (hovered == index && shortcut is not null) { Box(g, new RectangleF(bounds.Right - 20, bounds.Top + 2, 18, 18), Color.FromArgb(50, 54, 62), 9); DrawText(g, "×", new RectangleF(bounds.Right - 20, bounds.Top + 2, 18, 18), 16, Color.White, false, true); }
    }
}
