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
        var button = new CircleTransportButton(symbol, name, action, primary);
        desktopTips.SetToolTip(button, name);
        return button;
    }
    private Control CreateDesktopMediaPage()
    {
        var page = CreatePage("Media");
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 336));

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 320));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 164));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var now = DesktopCard("Odtwarzacz multimediów (Now Playing)");
        var player = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        player.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156));
        player.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var artwork = new ArtworkView { Dock = DockStyle.Top, Height = 156, BackColor = Surface };
        player.Controls.Add(artwork, 0, 0);

        var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12, 0, 0, 0) };
        foreach (var height in new[] { 62, 32, 28, 46, 64 }) details.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        var title = new Label { Dock = DockStyle.Fill, Text = "Gotowy do odtwarzania", AutoEllipsis = true, Font = new Font(Font.FontFamily, 16, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
        var artist = new Label { Dock = DockStyle.Fill, ForeColor = TextMuted, AutoEllipsis = true, Font = new Font(Font.FontFamily, 11) };
        var status = new Label { Dock = DockStyle.Fill, ForeColor = TextMuted, AutoEllipsis = true };
        var progress = new DarkProgressBar { Dock = DockStyle.Top, Height = 6, Maximum = 1000, AccessibleName = "Track progress" };
        var elapsed = new Label { Dock = DockStyle.Bottom, Height = 24, ForeColor = TextMuted, AutoEllipsis = true };
        var timeline = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) };
        timeline.Controls.Add(progress);
        timeline.Controls.Add(elapsed);

        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 4, 0, 0) };
        var previous = TransportButton("prev", "Poprzedni utwór", async () => await runtime.ExecuteHostMediaCommandAsync(MediaCommand.Previous));
        var play = TransportButton("play", "Odtwórz / Wstrzymaj", async () => await runtime.ExecuteHostMediaCommandAsync(MediaCommand.PlayPause), true);
        var next = TransportButton("next", "Następny utwór", async () => await runtime.ExecuteHostMediaCommandAsync(MediaCommand.Next));
        controls.Controls.Add(previous);
        controls.Controls.Add(play);
        controls.Controls.Add(next);

        details.Controls.Add(title, 0, 0);
        details.Controls.Add(artist, 0, 1);
        details.Controls.Add(status, 0, 2);
        details.Controls.Add(timeline, 0, 3);
        details.Controls.Add(controls, 0, 4);
        player.Controls.Add(details, 1, 0);
        now.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        now.Controls.Add(player, 0, now.RowCount++);
        left.Controls.Add(now, 0, 0);

        var audio = DesktopCard("Sterowanie głośnością audio");
        var audioRow = new TableLayoutPanel { ColumnCount = 3, Dock = DockStyle.Fill };
        audioRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        audioRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        audioRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        var speaker = new Panel { Dock = DockStyle.Fill };
        speaker.Paint += (_, e) => ShortcutIconCatalog.Draw(e.Graphics, "volume", null, new Rectangle(4, 8, 24, 24), TextPrimary);

        var volume = new DarkSlider { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100, AccessibleName = "System volume" };
        var mute = CompactButton("Wycisz", async () => await runtime.ToggleHostMuteAsync());
        mute.AccessibleName = "Mute";
        mute.Dock = DockStyle.Fill;
        var volumeLabel = new Label { Text = "Głośność systemowa", Dock = DockStyle.Fill, ForeColor = TextMuted, AutoEllipsis = true };
        bool updatingVolume = false;
        bool dragging = false;
        int? pendingVolume = null;
        bool sendingVolume = false;
        var volumeTimer = new System.Windows.Forms.Timer { Interval = 60 };
        volumeTimer.Tick += async (_, _) => {
            if (sendingVolume || pendingVolume is null) return;
            var value = pendingVolume.Value;
            pendingVolume = null;
            sendingVolume = true;
            try { await runtime.SetHostVolumeAsync(value); }
            catch (Exception error) { AgentDiagnosticLog.Write($"Volume change failed: {error.GetType().Name}"); if (!volumeLabel.IsDisposed) volumeLabel.Text = "Dźwięk niedostępny"; }
            finally { sendingVolume = false; }
        };
        volume.MouseDown += (_, _) => dragging = true;
        volume.MouseUp += (_, _) => dragging = false;
        volume.ValueChanged += (_, _) => {
            var dev = state.Snapshot().CurrentAudio?.DeviceName ?? "Domyślne wyjście audio";
            volumeLabel.Text = $"Głośność systemowa: {volume.Value}% · {dev}";
            if (!updatingVolume) { pendingVolume = volume.Value; volumeTimer.Start(); }
        };
        page.Disposed += (_, _) => volumeTimer.Dispose();

        audioRow.Controls.Add(speaker, 0, 0);
        audioRow.Controls.Add(volume, 1, 0);
        audioRow.Controls.Add(mute, 2, 0);
        CompactField(audio, "Poziom głośności", audioRow);
        audio.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        audio.Controls.Add(volumeLabel, 0, audio.RowCount++);
        FinishCard(audio);
        left.Controls.Add(audio, 0, 1);

        var previewCard = DesktopCard("CYD Media Page Preview");
        var preview = new DesktopCanvas { Dock = DockStyle.Fill, Page = "Media", TabStop = false, BackColor = Surface };
        var initialApp = pendingAppearance ?? LoadAppearanceConfiguration();
        preview.Accent = ColorTranslator.FromHtml(initialApp.Accent);
        preview.DisplayBackground = ColorTranslator.FromHtml(initialApp.Background);
        preview.TileTheme = initialApp.TileColorScheme;
        preview.Clock24Hour = (pendingDesktopAppearance ?? LoadDesktopAppearanceConfiguration()).Clock24Hour;
        previewCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 264));
        previewCard.Controls.Add(preview, 0, previewCard.RowCount++);
        FinishCard(previewCard);

        MediaSnapshot? anchorMedia = null; long anchorTime = 0;
        void RefreshTimeline() {
            var media = state.Snapshot().CurrentMedia;
            if (!ReferenceEquals(media, anchorMedia)) { anchorMedia = media; anchorTime = Stopwatch.GetTimestamp(); }
            var position = media?.Timeline.Position ?? TimeSpan.Zero;
            var duration = media?.Timeline.EndTime ?? TimeSpan.Zero;
            if (media?.PlaybackState == MediaPlaybackState.Playing) position += Stopwatch.GetElapsedTime(anchorTime);
            if (duration > TimeSpan.Zero) position = position > duration ? duration : position;
            progress.Value = duration.TotalSeconds > 0 ? (int)Math.Clamp(position.TotalSeconds / duration.TotalSeconds * 1000, 0, 1000) : 0;
            elapsed.Text = duration.TotalSeconds > 0 ? $"{position:m\\:ss}                                      {duration:m\\:ss}" : "Odtwarzanie na żywo / czas niedostępny";
        }

        desktopRefresh = () => {
            var snapshot = state.Snapshot();
            var media = snapshot.CurrentMedia;
            title.Text = media?.Title ?? "Gotowy do odtwarzania";
            artist.Text = media?.Artist ?? "Brak aktywnej sesji multimediów";
            status.Text = media?.PlaybackState == MediaPlaybackState.Playing ? "● Odtwarzanie" : media is null ? "Brak sesji" : "Ⅱ Wstrzymano";
            desktopTips.SetToolTip(title, title.Text);
            desktopTips.SetToolTip(artist, artist.Text);

            if (play is CircleTransportButton playBtn) {
                playBtn.IconSymbol = media?.PlaybackState == MediaPlaybackState.Playing ? "pause" : "play";
                playBtn.Invalidate();
            }
            previous.Enabled = play.Enabled = next.Enabled = media is not null;
            updatingVolume = true;
            if (!dragging && pendingVolume is null && !sendingVolume)
                volume.Value = Math.Clamp(snapshot.CurrentAudio?.VolumePercent ?? 0, 0, 100);
            updatingVolume = false;

            var dev = snapshot.CurrentAudio?.DeviceName ?? "Domyślne wyjście audio";
            volumeLabel.Text = $"Głośność systemowa: {volume.Value}% · {dev}";
            mute.Text = snapshot.CurrentAudio?.IsMuted == true ? "Wyciszono (Włącz)" : "Wycisz";
            volume.Enabled = mute.Enabled = snapshot.CurrentAudio is not null;

            artwork.Artwork = desktopArtwork;
            artwork.Invalidate();
            var appConfig = pendingAppearance ?? LoadAppearanceConfiguration();
            preview.Accent = ColorTranslator.FromHtml(appConfig.Accent);
            preview.DisplayBackground = ColorTranslator.FromHtml(appConfig.Background);
            preview.TileTheme = appConfig.TileColorScheme;
            preview.Clock24Hour = (pendingDesktopAppearance ?? LoadDesktopAppearanceConfiguration()).Clock24Hour;
            preview.Artwork = desktopArtwork;
            preview.Snapshot = snapshot;
            preview.Invalidate();
            progress.Invalidate();
            RefreshTimeline();
            _ = LoadDesktopArtworkAsync(snapshot, desktopViewLifetime!.Token);
        };

        var timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (_, _) => RefreshTimeline();
        page.Disposed += (_, _) => timer.Dispose();
        timer.Start();

        grid.Controls.Add(left, 0, 0);
        grid.Controls.Add(previewCard, 1, 0);
        AddPageBodyControl(page, grid);
        desktopRefresh();
        return page;
    }

    private sealed class MetricView : Control
    {
        internal string Title = "";
        internal string Detail = "";
        internal double? Value;
        internal string Unit = "%";
        internal IReadOnlyList<double> History = [];
        internal Color AccentColor;

        internal MetricView() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            if (Width <= 1 || Height <= 1) return;
            using var path = CreateRoundedRectPath(new Rectangle(0, 0, Width - 1, Height - 1), 14);
            var old = Region;
            Region = new Region(path);
            old?.Dispose();
        }

        private Color GetThresholdColor()
        {
            if (!Value.HasValue) return Border;
            if (Unit == "%")
            {
                if (Value.Value >= 85) return Danger;
                if (Value.Value >= 70) return AppleAmber;
                return Title == "RAM" ? AppleGreen : Accent;
            }
            if (Unit == "°C")
            {
                if (Value.Value >= 80) return Danger;
                if (Value.Value >= 65) return AppleAmber;
                return AppleGreen;
            }
            return AccentColor;
        }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var effectiveColor = GetThresholdColor();

            // Header title
            TextRenderer.DrawText(g, Title, Font, new Rectangle(18, 16, Width - 36, 24), TextMuted, TextFormatFlags.SingleLine);

            // Large metric value
            using var number = new Font(Font.FontFamily, 26, FontStyle.Bold);
            TextRenderer.DrawText(g, Value.HasValue ? $"{Value:0}{Unit}" : "—", number, new Rectangle(16, 42, Width - 32, 48), TextPrimary, TextFormatFlags.SingleLine);

            // Detail subtitle
            TextRenderer.DrawText(g, Detail, Font, new Rectangle(18, 96, Width - 36, 26), TextMuted, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            // Progress bar
            var bar = new Rectangle(18, Math.Max(128, Height - 114), Width - 36, 5);
            if (bar.Width <= 0) return;
            using (var track = new SolidBrush(Border)) g.FillRectangle(track, bar);
            if (Value.HasValue) {
                using var gradient = new LinearGradientBrush(bar, effectiveColor, Color.FromArgb(Math.Min(255, effectiveColor.R + 36), Math.Min(255, effectiveColor.G + 36), Math.Min(255, effectiveColor.B + 36)), 0f);
                g.FillRectangle(gradient, bar.X, bar.Y, (float)(bar.Width * Math.Clamp(Unit == "°C" ? Value.Value / 110 : Value.Value / 100, 0, 1)), bar.Height);
            }

            // History chart with scale indicators
            if (Value.HasValue && History.Count >= 2) {
                var chartHeight = 44;
                var bounds = new Rectangle(18, Math.Max(bar.Bottom + 8, Height - 80), Width - 36, chartHeight);
                var min = Math.Max(0, History.Min() - 5);
                var max = Math.Max(min + 10, History.Max() + 5);
                var points = History.Select((v, i) => new PointF(bounds.X + i * bounds.Width / (float)(History.Count - 1), bounds.Bottom - (float)Math.Clamp((v - min) / (max - min), 0, 1) * bounds.Height)).ToArray();

                using var area = new GraphicsPath();
                area.AddLines(points);
                area.AddLine(points[^1], new PointF(bounds.Right, bounds.Bottom));
                area.AddLine(new PointF(bounds.Right, bounds.Bottom), new PointF(bounds.Left, bounds.Bottom));
                area.CloseFigure();

                using var fill = new LinearGradientBrush(bounds, Color.FromArgb(54, effectiveColor), Color.FromArgb(0, effectiveColor), 90f);
                g.FillPath(fill, area);
                using var pen = new Pen(effectiveColor, 2);
                g.DrawLines(pen, points);

                // Small axis and timeframe labels positioned outside the wave line
                using var smallFont = new Font(Font.FontFamily, 7.5f);
                using var mutedBrush = new SolidBrush(Color.FromArgb(160, TextMuted));
                var labelY = bounds.Bottom + 2;
                g.DrawString(Unit == "°C" ? "0°C" : "0%", smallFont, mutedBrush, bounds.Left, labelY);
                var timeframe = "30s";
                var tSize = g.MeasureString(timeframe, smallFont);
                g.DrawString(timeframe, smallFont, mutedBrush, bounds.Right - tSize.Width, labelY);
            }
        }
    }

    private static readonly Lazy<string> CachedCpuName = new(() =>
        Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null)?.ToString()?.Trim() ?? "Procesor Windows");

    private Control CreateDesktopMonitorPage()
    {
        var page = CreatePage("Monitor");
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 336));

        var metrics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        for (var i = 0; i < 2; ++i) {
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        }

        var cpuName = CachedCpuName.Value;
        var views = new List<MetricView>();
        var colors = new[] { Accent, AppleGreen, ApplePurple, AppleAmber };
        for (var i = 0; i < 4; ++i) {
            var view = new MetricView {
                Title = new[] { "CPU", "RAM", "GPU", "TEMP" }[i],
                Dock = DockStyle.Fill,
                Margin = new Padding(6),
                BackColor = Surface,
                ForeColor = TextPrimary,
                Font = Font,
                AccentColor = colors[i],
                AccessibleName = new[] { "CPU", "RAM", "GPU", "TEMP" }[i]
            };
            views.Add(view);
            metrics.Controls.Add(view, i % 2, i / 2);
        }

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 330));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var summary = DesktopCard("Podsumowanie telemetrii");
        var values = new Dictionary<string, Label>();

        void AddTelemetrySection(string title) {
            var header = new Label { Text = title, Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold), ForeColor = Color.FromArgb(170, 185, 205) };
            summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            summary.Controls.Add(header, 0, summary.RowCount++);
        }

        void AddTelemetryRow(string name, string labelText) {
            var row = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.Controls.Add(new Label { Text = labelText, Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, ForeColor = TextMuted }, 0, 0);
            var value = new Label { Dock = DockStyle.Fill, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleRight, AccessibleName = name + " value" };
            row.Controls.Add(value, 1, 0);
            values[name] = value;
            summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            summary.Controls.Add(row, 0, summary.RowCount++);
        }

        AddTelemetrySection("Komputer PC");
        AddTelemetryRow("Now Playing", "Utwór");
        AddTelemetryRow("Artist", "Wykonawca");
        AddTelemetryRow("Uptime", "Czas pracy PC");

        AddTelemetrySection("Urządzenie CYD (ESP32)");
        AddTelemetryRow("Firmware", "Wersja softu");
        AddTelemetryRow("Free Heap", "Wolna pamięć");
        AddTelemetryRow("Samples", "Próbki bufora");

        FinishCard(summary);
        right.Controls.Add(summary, 0, 0);

        var previewCard = DesktopCard("CYD Monitor Preview");
        var preview = new DesktopCanvas { Dock = DockStyle.Fill, Page = "PC", TabStop = false, BackColor = Surface };
        var initialApp = pendingAppearance ?? LoadAppearanceConfiguration();
        preview.Accent = ColorTranslator.FromHtml(initialApp.Accent);
        preview.DisplayBackground = ColorTranslator.FromHtml(initialApp.Background);
        preview.TileTheme = initialApp.TileColorScheme;
        preview.Clock24Hour = (pendingDesktopAppearance ?? LoadDesktopAppearanceConfiguration()).Clock24Hour;
        previewCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewCard.Controls.Add(preview, 0, previewCard.RowCount++);
        right.Controls.Add(previewCard, 0, 1);

        desktopRefresh = () => {
            var snapshot = state.Snapshot();
            var stats = snapshot.SystemTelemetry;
            views[0].Value = stats?.CpuPercent;
            views[0].Detail = cpuName;
            views[0].History = stats?.CpuHistory ?? [];

            views[1].Value = stats?.RamPercent;
            views[1].Detail = stats is null ? "Oczekiwanie na próbki" : $"{stats.RamUsedBytes / 1073741824.0:0.0} / {stats.RamTotalBytes / 1073741824.0:0.0} GB";
            views[1].History = stats?.RamHistory ?? [];

            views[2].Value = stats?.GpuAvailable == true ? stats.GpuPercent : null;
            views[2].Detail = stats?.GpuSource ?? (stats?.GpuAvailable == true ? "Silnik Windows GPU" : "Licznik GPU niedostępny");
            views[2].History = stats?.GpuHistory ?? [];

            views[3].Value = stats?.TempAvailable == true ? stats.TempCelsius : null;
            views[3].Unit = "°C";
            views[3].Detail = stats?.TemperatureSource ?? (stats?.TempAvailable == true ? "Temperatura CPU" : "Brak czujnika WMI");
            views[3].History = stats?.TempHistory ?? [];

            foreach (var view in views) {
                view.Invalidate();
                if (view.Title == "RAM") {
                    desktopTips.SetToolTip(view, $"Pamięć RAM: {view.Detail} (Norma: <70%, Obciążenie: 70-85%, Krytyczne: >85%). Ostatnie 30 próbek.");
                } else if (view.Title == "TEMP") {
                    desktopTips.SetToolTip(view, view.Value is null
                        ? "Temperatura CPU niedostępna. Uruchom LibreHardwareMonitor lub OpenHardwareMonitor jako Administrator z włączonym WMI Provider."
                        : $"{view.Detail}: {view.Value:0}°C. Ostatnie 30 próbek.");
                } else {
                    desktopTips.SetToolTip(view, $"{view.Detail}. Ostatnie 30 próbek; interwał 1 sekunda.");
                }
            }

            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            values["Now Playing"].Text = snapshot.CurrentMedia?.Title ?? "Brak sesji";
            values["Artist"].Text = snapshot.CurrentMedia?.Artist ?? "—";
            values["Uptime"].Text = $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m";
            values["Firmware"].Text = snapshot.DeviceInfo?.FirmwareVersion ?? "Niedostępne";
            values["Free Heap"].Text = snapshot.DeviceInfo?.Heap is { } heap ? $"{heap.FreeBytes / 1024} KB" : "Niedostępne";
            values["Samples"].Text = stats is null ? "Oczekiwanie" : $"{stats.CpuHistory.Count} / 30";

            foreach (var value in values.Values) desktopTips.SetToolTip(value, value.Text);
            var appConfig = pendingAppearance ?? LoadAppearanceConfiguration();
            preview.Accent = ColorTranslator.FromHtml(appConfig.Accent);
            preview.DisplayBackground = ColorTranslator.FromHtml(appConfig.Background);
            preview.TileTheme = appConfig.TileColorScheme;
            preview.Clock24Hour = (pendingDesktopAppearance ?? LoadDesktopAppearanceConfiguration()).Clock24Hour;
            preview.Snapshot = snapshot;
            preview.Invalidate();
        };

        grid.Controls.Add(metrics, 0, 0);
        grid.Controls.Add(right, 1, 0);
        AddPageBodyControl(page, grid);
        desktopRefresh();
        return page;
    }
}
