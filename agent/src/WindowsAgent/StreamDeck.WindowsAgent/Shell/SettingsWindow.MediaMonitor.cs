using System.Drawing.Drawing2D;
using System.Diagnostics;
using Microsoft.Win32;
using SkiaSharp;
using StreamDeck.Media;
using StreamDeck.Protocol;

namespace StreamDeck.WindowsAgent.Shell;

public sealed partial class SettingsWindow
{
    private string? artworkLoadingKey;
    private async Task LoadDesktopArtworkAsync(AgentStatusSnapshot snapshot, CancellationToken token)
    {
        var media = snapshot.CurrentMedia; var key = media is null ? "none" : $"{media.SourceAppUserModelId}|{media.Title}|{media.AlbumTitle}|{media.Thumbnail is not null}";
        if (key == desktopArtworkKey || key == artworkLoadingKey) return;
        artworkLoadingKey = key;
        try {
            Bitmap? image = null;
            if (media?.Thumbnail is not null) {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
                await using var stream = await media.Thumbnail.OpenReadAsync(timeout.Token);
                if (stream is not null) {
                    using var bytes = new MemoryStream(); var buffer = new byte[8192]; int read;
                    while ((read = await stream.ReadAsync(buffer, timeout.Token)) > 0) { if (bytes.Length + read > 4 * 1024 * 1024) throw new InvalidDataException(); bytes.Write(buffer, 0, read); }
                    var data = bytes.ToArray(); image = await Task.Run(() => {
                        using var codec = SKCodec.Create(new SKMemoryStream(data)); if (codec is null || codec.Info.Width > 4096 || codec.Info.Height > 4096) return null;
                        using var bitmap = SKBitmap.Decode(codec); if (bitmap is null) return null;
                        using var encoded = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100); using var input = new MemoryStream(encoded.ToArray()); using var decoded = Image.FromStream(input); return new Bitmap(decoded);
                    }, timeout.Token);
                }
            }
            if (token.IsCancellationRequested || IsDisposed) { image?.Dispose(); return; }
            var previous = desktopArtwork; desktopArtwork = image; desktopArtworkKey = key; previous?.Dispose(); desktopRefresh?.Invoke();
        } catch (Exception error) when (error is IOException or OperationCanceledException or ArgumentException or System.Runtime.InteropServices.COMException) { if (!token.IsCancellationRequested) desktopArtworkKey = key; }
        finally { if (artworkLoadingKey == key) artworkLoadingKey = null; }
    }
    private sealed class ArtworkView : Control
    {
        internal Image? Artwork;
        internal ArtworkView() { DoubleBuffered = true; ResizeRedraw = true; AccessibleName = "Album artwork"; }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); var side = Math.Min(Width, Height) - 4; if (side <= 0) return; var box = new Rectangle((Width - side) / 2, (Height - side) / 2, side, side); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = CreateRoundedRectPath(box, 16); var saved = e.Graphics.Save(); e.Graphics.SetClip(path);
            if (Artwork is not null) { var scale = Math.Max((float)side / Artwork.Width, (float)side / Artwork.Height); e.Graphics.DrawImage(Artwork, box.X + (side - Artwork.Width * scale) / 2, box.Y + (side - Artwork.Height * scale) / 2, Artwork.Width * scale, Artwork.Height * scale); }
            else { using var gradient = new LinearGradientBrush(box, Color.FromArgb(39, 61, 88), Color.FromArgb(24, 31, 46), 45); e.Graphics.FillRectangle(gradient, box); using var noteFont = new Font("Segoe UI", 38); TextRenderer.DrawText(e.Graphics, "♫", noteFont, box, Color.FromArgb(180, 207, 239), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
            e.Graphics.Restore(saved);
        }
    }
    private Button TransportButton(string symbol, string name, Func<Task> action, bool primary = false)
    {
        var button = CompactButton("", action); button.AccessibleName = name; desktopTips.SetToolTip(button, name);
        button.Width = primary ? 62 : 48; button.Height = primary ? 54 : 44; button.FlatAppearance.BorderSize = 0; button.BackColor = Surface; button.Tag = symbol;
        button.Paint += (_, e) => {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; var bounds = Rectangle.Inflate(button.ClientRectangle, -3, -3); using var fill = new SolidBrush(primary ? Accent : SurfaceAlt); e.Graphics.FillEllipse(fill, bounds);
            var center = new Rectangle((button.Width - 24) / 2, (button.Height - 24) / 2, 24, 24); ShortcutIconCatalog.Draw(e.Graphics, button.Tag?.ToString(), null, center, primary ? Color.White : TextPrimary);
            if (button.Focused) ControlPaint.DrawFocusRectangle(e.Graphics, button.ClientRectangle);
        }; return button;
    }
    private Control CreateDesktopMediaPage()
    {
        var page = CreatePage("Media"); var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }; grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 336));
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 }; left.RowStyles.Add(new RowStyle(SizeType.Absolute, 320)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 164)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var now = DesktopCard("Now Playing"); var player = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }; player.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156)); player.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var artwork = new ArtworkView { Dock = DockStyle.Top, Height = 156, BackColor = Surface }; player.Controls.Add(artwork, 0, 0);
        var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12, 0, 0, 0) }; foreach (var height in new[] { 62, 32, 28, 46, 64 }) details.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        var title = new Label { Dock = DockStyle.Fill, Text = "Ready to Play", AutoEllipsis = true, Font = new Font(Font.FontFamily, 17, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
        var artist = new Label { Dock = DockStyle.Fill, ForeColor = TextMuted, AutoEllipsis = true, Font = new Font(Font.FontFamily, 11) }; var status = new Label { Dock = DockStyle.Fill, ForeColor = TextMuted, AutoEllipsis = true };
        var progress = new ProgressBar { Dock = DockStyle.Top, Height = 6, Maximum = 1000, AccessibleName = "Track progress" }; var elapsed = new Label { Dock = DockStyle.Bottom, Height = 24, ForeColor = TextMuted, AutoEllipsis = true };
        var timeline = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) }; timeline.Controls.Add(progress); timeline.Controls.Add(elapsed);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 2, 0, 0) }; var previous = TransportButton("prev", "Previous track", async () => await runtime.ExecuteHostMediaCommandAsync(MediaCommand.Previous)); var play = TransportButton("play", "Play or pause", async () => await runtime.ExecuteHostMediaCommandAsync(MediaCommand.PlayPause), true); var next = TransportButton("next", "Next track", async () => await runtime.ExecuteHostMediaCommandAsync(MediaCommand.Next)); controls.Controls.Add(previous); controls.Controls.Add(play); controls.Controls.Add(next);
        details.Controls.Add(title, 0, 0); details.Controls.Add(artist, 0, 1); details.Controls.Add(status, 0, 2); details.Controls.Add(timeline, 0, 3); details.Controls.Add(controls, 0, 4); player.Controls.Add(details, 1, 0); now.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); now.Controls.Add(player, 0, now.RowCount++); left.Controls.Add(now, 0, 0);
        var audio = DesktopCard("Audio & Volume Control"); var audioRow = new TableLayoutPanel { ColumnCount = 3, Dock = DockStyle.Fill }; audioRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40)); audioRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); audioRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        var speaker = new Panel { Dock = DockStyle.Fill }; speaker.Paint += (_, e) => ShortcutIconCatalog.Draw(e.Graphics, "volume", null, new Rectangle(4, 8, 24, 24), TextPrimary);
        var volume = new TrackBar { AutoSize = false, Dock = DockStyle.Fill, Minimum = 0, Maximum = 100, TickStyle = TickStyle.None, AccessibleName = "System volume" }; var mute = CompactButton("Mute", async () => await runtime.ToggleHostMuteAsync()); mute.Dock = DockStyle.Fill;
        var volumeLabel = new Label { Text = "System volume", Dock = DockStyle.Fill, ForeColor = TextMuted }; bool updatingVolume = false; bool dragging = false;
        int? pendingVolume = null; bool sendingVolume = false;
        var volumeTimer = new System.Windows.Forms.Timer { Interval = 60 };
        volumeTimer.Tick += async (_, _) => {
            if (sendingVolume || pendingVolume is null) return;
            var value = pendingVolume.Value; pendingVolume = null; sendingVolume = true;
            try { await runtime.SetHostVolumeAsync(value); }
            catch (Exception error) { AgentDiagnosticLog.Write($"Volume change failed: {error.GetType().Name}"); if (!volumeLabel.IsDisposed) volumeLabel.Text = "Audio unavailable"; }
            finally { sendingVolume = false; }
        };
        volume.MouseDown += (_, _) => dragging = true; volume.MouseUp += (_, _) => dragging = false;
        volume.ValueChanged += (_, _) => { volumeLabel.Text = $"System volume · {volume.Value}%"; if (!updatingVolume) { pendingVolume = volume.Value; volumeTimer.Start(); } };
        page.Disposed += (_, _) => volumeTimer.Dispose();
        audioRow.Controls.Add(speaker, 0, 0); audioRow.Controls.Add(volume, 1, 0); audioRow.Controls.Add(mute, 2, 0); CompactField(audio, "Output volume", audioRow); audio.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); audio.Controls.Add(volumeLabel, 0, audio.RowCount++); FinishCard(audio); left.Controls.Add(audio, 0, 1);
        var previewCard = DesktopCard("CYD Media Page Preview"); var preview = new DesktopCanvas { Dock = DockStyle.Fill, Page = "Media", TabStop = false, BackColor = Surface }; previewCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 264)); previewCard.Controls.Add(preview, 0, previewCard.RowCount++); FinishCard(previewCard);
        MediaSnapshot? anchorMedia = null; long anchorTime = 0;
        void RefreshTimeline() {
            var media = state.Snapshot().CurrentMedia;
            if (!ReferenceEquals(media, anchorMedia)) { anchorMedia = media; anchorTime = Stopwatch.GetTimestamp(); }
            var position = media?.Timeline.Position ?? TimeSpan.Zero; var duration = media?.Timeline.EndTime ?? TimeSpan.Zero;
            if (media?.PlaybackState == MediaPlaybackState.Playing) position += Stopwatch.GetElapsedTime(anchorTime);
            if (duration > TimeSpan.Zero) position = position > duration ? duration : position;
            progress.Value = duration.TotalSeconds > 0 ? (int)Math.Clamp(position.TotalSeconds / duration.TotalSeconds * 1000, 0, 1000) : 0;
            elapsed.Text = duration.TotalSeconds > 0 ? $"{position:m\\:ss}                                      {duration:m\\:ss}" : "Live / timeline unavailable";
        }
        desktopRefresh = () => { var snapshot = state.Snapshot(); var media = snapshot.CurrentMedia; title.Text = media?.Title ?? "Ready to Play"; artist.Text = media?.Artist ?? "No active media session"; status.Text = media?.PlaybackState == MediaPlaybackState.Playing ? "● Playing" : media is null ? "No session" : "Ⅱ Paused"; play.Tag = media?.PlaybackState == MediaPlaybackState.Playing ? "pause" : "play"; play.Invalidate(); previous.Enabled = play.Enabled = next.Enabled = media is not null; updatingVolume = true; if (!dragging && pendingVolume is null && !sendingVolume) volume.Value = Math.Clamp(snapshot.CurrentAudio?.VolumePercent ?? 0, 0, 100); updatingVolume = false; mute.Text = snapshot.CurrentAudio?.IsMuted == true ? "Unmute" : "Mute"; volume.Enabled = mute.Enabled = snapshot.CurrentAudio is not null; artwork.Artwork = desktopArtwork; artwork.Invalidate(); preview.Artwork = desktopArtwork; preview.Snapshot = snapshot; preview.Invalidate(); RefreshTimeline(); _ = LoadDesktopArtworkAsync(snapshot, desktopViewLifetime!.Token); };
        var timer = new System.Windows.Forms.Timer { Interval = 100 }; timer.Tick += (_, _) => RefreshTimeline(); page.Disposed += (_, _) => timer.Dispose(); timer.Start();
        grid.Controls.Add(left, 0, 0); grid.Controls.Add(previewCard, 1, 0); AddPageBodyControl(page, grid); desktopRefresh(); return page;
    }
    private sealed class MetricView : Control
    {
        internal string Title = ""; internal string Detail = ""; internal double? Value; internal string Unit = "%"; internal IReadOnlyList<double> History = []; internal Color AccentColor;
        internal MetricView() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnResize(EventArgs e) { base.OnResize(e); if (Width <= 1 || Height <= 1) return; using var path = CreateRoundedRectPath(new Rectangle(0, 0, Width - 1, Height - 1), 14); var old = Region; Region = new Region(path); old?.Dispose(); }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            TextRenderer.DrawText(g, Title, Font, new Rectangle(18, 18, Width - 36, 26), TextMuted, TextFormatFlags.SingleLine);
            using var number = new Font(Font.FontFamily, 26, FontStyle.Bold); TextRenderer.DrawText(g, Value.HasValue ? $"{Value:0}{Unit}" : "—", number, new Rectangle(16, 48, Width - 32, 52), TextPrimary, TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, Detail, Font, new Rectangle(18, 105, Width - 36, 30), TextMuted, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            var bar = new Rectangle(18, Math.Max(140, Height - 106), Width - 36, 5); if (bar.Width <= 0) return;
            using var track = new SolidBrush(Border); g.FillRectangle(track, bar); if (Value.HasValue) { using var gradient = new LinearGradientBrush(bar, AccentColor, Color.FromArgb(Math.Min(255, AccentColor.R + 36), Math.Min(255, AccentColor.G + 36), Math.Min(255, AccentColor.B + 36)), 0f); g.FillRectangle(gradient, bar.X, bar.Y, (float)(bar.Width * Math.Clamp(Unit == "°C" ? Value.Value / 110 : Value.Value / 100, 0, 1)), bar.Height); }
            if (Value.HasValue && History.Count >= 2) { var bounds = new Rectangle(18, Math.Max(bar.Bottom + 12, Height - 82), Width - 36, 64); var min = Math.Max(0, History.Min() - 5); var max = Math.Max(min + 10, History.Max() + 5); var points = History.Select((v, i) => new PointF(bounds.X + i * bounds.Width / (float)(History.Count - 1), bounds.Bottom - (float)Math.Clamp((v - min) / (max - min), 0, 1) * bounds.Height)).ToArray(); using var area = new GraphicsPath(); area.AddLines(points); area.AddLine(points[^1], new PointF(bounds.Right, bounds.Bottom)); area.AddLine(new PointF(bounds.Right, bounds.Bottom), new PointF(bounds.Left, bounds.Bottom)); area.CloseFigure(); using var fill = new LinearGradientBrush(bounds, Color.FromArgb(54, AccentColor), Color.FromArgb(0, AccentColor), 90f); g.FillPath(fill, area); using var pen = new Pen(AccentColor, 2); g.DrawLines(pen, points); }
        }
    }
    private Control CreateDesktopMonitorPage()
    {
        var page = CreatePage("Monitor"); var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 }; grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 336));
        var metrics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 }; for (var i = 0; i < 2; ++i) { metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); }
        var cpuName = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null)?.ToString()?.Trim() ?? "Windows processor";
        var views = new List<MetricView>(); var colors = new[] { Accent, AppleGreen, ApplePurple, AppleAmber };
        for (var i = 0; i < 4; ++i) { var view = new MetricView { Title = new[] { "CPU", "RAM", "GPU", "TEMP" }[i], Dock = DockStyle.Fill, Margin = new Padding(6), BackColor = Surface, ForeColor = TextPrimary, Font = Font, AccentColor = colors[i], AccessibleName = new[] { "CPU", "RAM", "GPU", "TEMP" }[i] }; views.Add(view); metrics.Controls.Add(view, i % 2, i / 2); }
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 }; right.RowStyles.Add(new RowStyle(SizeType.Absolute, 308)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var summary = DesktopCard("Live Telemetry Summary"); var values = new Dictionary<string, Label>();
        foreach (var name in new[] { "Now Playing", "Artist", "Uptime", "Firmware", "Free Heap", "Samples" }) {
            var row = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill }; row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.Controls.Add(new Label { Text = name, Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, ForeColor = TextMuted }, 0, 0);
            var value = new Label { Dock = DockStyle.Fill, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleRight, AccessibleName = name + " value" }; row.Controls.Add(value, 1, 0); values[name] = value; summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); summary.Controls.Add(row, 0, summary.RowCount++);
        }
        FinishCard(summary); right.Controls.Add(summary, 0, 0);
        var previewCard = DesktopCard("CYD Monitor Preview"); var preview = new DesktopCanvas { Dock = DockStyle.Fill, Page = "PC", TabStop = false, BackColor = Surface }; previewCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); previewCard.Controls.Add(preview, 0, previewCard.RowCount++); right.Controls.Add(previewCard, 0, 1);
        desktopRefresh = () => { var snapshot = state.Snapshot(); var stats = snapshot.SystemTelemetry;
            views[0].Value = stats?.CpuPercent; views[0].Detail = cpuName; views[0].History = stats?.CpuHistory ?? [];
            views[1].Value = stats?.RamPercent; views[1].Detail = stats is null ? "Waiting for samples" : $"{stats.RamUsedBytes / 1073741824.0:0.0} / {stats.RamTotalBytes / 1073741824.0:0.0} GB"; views[1].History = stats?.RamHistory ?? [];
            views[2].Value = stats?.GpuAvailable == true ? stats.GpuPercent : null; views[2].Detail = stats?.GpuSource ?? (stats?.GpuAvailable == true ? "Windows GPU engine" : "GPU counter unavailable"); views[2].History = stats?.GpuHistory ?? [];
            views[3].Value = stats?.TempAvailable == true ? stats.TempCelsius : null; views[3].Unit = "°C"; views[3].Detail = stats?.TemperatureSource ?? (stats?.TempAvailable == true ? "CPU temperature" : "Temperature sensor unavailable"); views[3].History = stats?.TempHistory ?? [];
            foreach (var view in views) { view.Invalidate(); desktopTips.SetToolTip(view, view.Title == "TEMP" && view.Value is null ? view.Detail + ". Run LibreHardwareMonitor or OpenHardwareMonitor with WMI enabled; CPU sensors may need administrator access." : view.Detail + ". Last 30 samples; chart range adjusts to the readings."); } var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            values["Now Playing"].Text = snapshot.CurrentMedia?.Title ?? "No session"; values["Artist"].Text = snapshot.CurrentMedia?.Artist ?? "—"; values["Uptime"].Text = $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m"; values["Firmware"].Text = snapshot.DeviceInfo?.FirmwareVersion ?? "Unavailable"; values["Free Heap"].Text = snapshot.DeviceInfo?.Heap is { } heap ? $"{heap.FreeBytes / 1024} KB" : "Unavailable"; values["Samples"].Text = stats is null ? "Waiting" : $"{stats.CpuHistory.Count} / 30";
            foreach (var value in values.Values) desktopTips.SetToolTip(value, value.Text); preview.Snapshot = snapshot; preview.Invalidate();
        };
        grid.Controls.Add(metrics, 0, 0); grid.Controls.Add(right, 1, 0); AddPageBodyControl(page, grid); desktopRefresh(); return page;
    }
}
