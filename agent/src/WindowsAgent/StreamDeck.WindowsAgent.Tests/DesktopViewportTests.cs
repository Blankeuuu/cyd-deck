using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using StreamDeck.Audio;
using StreamDeck.Configuration;
using StreamDeck.Host;
using StreamDeck.Media;
using StreamDeck.Protocol;
using StreamDeck.Shortcuts;
using StreamDeck.SystemMonitoring;
using StreamDeck.WindowsAgent;
using StreamDeck.WindowsAgent.Runtime;
using StreamDeck.WindowsAgent.Shell;
using StreamDeck.WindowsAgent.Tray;
using Shortcut = StreamDeck.Shortcuts.ShortcutDefinition;

internal static class DesktopViewportTests
{
    internal static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { RunOnDesktopThread(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Desktop viewport review failed", failure);
    }
    private static IEnumerable<Control> All(Control root) { foreach (Control child in root.Controls) { yield return child; foreach (var descendant in All(child)) yield return descendant; } }
    private static Control ActivePage(Control window) => All(window).OfType<Panel>().Where(p => Equals(p.Tag, "settings-page-body")).Select(p => p.Parent!).Single(p => p.Visible);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void RunOnDesktopThread()
    {
        Directory.CreateDirectory("artifacts/ui-review");
        if (File.Exists("artifacts/ui-review/desktop-pairing.json")) File.Delete("artifacts/ui-review/desktop-pairing.json");
        var fixture = new AgentConfigurationStore(Path.GetFullPath("artifacts/ui-review/desktop-fixture.json"));
        var shortcuts = Enumerable.Range(0, 24).Select(i => new Shortcut { Id = "desktop-" + i, Name = i switch { 0 => "Spotify", 1 => "Microphone", 2 => "Lock", _ => "Shortcut " + (i + 1) }, Icon = i % 3 == 0 ? "play" : i % 3 == 1 ? "mic" : "lock", DisplayMode = "both", Action = new() { Type = "hotkey", Keys = ["CTRL", "SHIFT", "M"] } }).ToArray();
        fixture.Save(AgentConfiguration.CreateDefault() with { Shortcuts = shortcuts });
        var state = new AgentState(); state.MarkDeviceConnected("test-cyd", "CYD", "USB"); state.UpdateHostAudio(new AudioSnapshot(76, false));
        using var cover = new Bitmap(128, 128); using (var graphics = Graphics.FromImage(cover)) { graphics.Clear(Color.FromArgb(48, 90, 130)); using var brush = new SolidBrush(Color.FromArgb(160, 180, 220)); graphics.FillEllipse(brush, 28, 28, 72, 72); }
        using var imageStream = new MemoryStream(); cover.Save(imageStream, System.Drawing.Imaging.ImageFormat.Png);
        state.UpdateHostMedia(new MediaSnapshot("Spotify", "GADU GADU — a longer title that must stay on one line", "Test Artist", "Test Album", MediaPlaybackState.Playing, new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(4), TimeSpan.FromSeconds(87), TimeSpan.Zero, TimeSpan.FromMinutes(4)), new Thumbnail(imageStream.ToArray())));
        for (var i = 0; i < 34; ++i) state.UpdateSystemMetrics(new SystemMetrics { TimestampUtc = DateTimeOffset.UtcNow, CpuUsagePercent = 20 + i % 10, RamUsedBytes = 23UL * 1024 * 1024 * 1024, RamTotalBytes = 32UL * 1024 * 1024 * 1024, GpuUsagePercent = 18 + i % 5, CpuTemperatureCelsius = 54 + i % 7, Uptime = TimeSpan.FromDays(16) });
        Check(state.Snapshot().SystemTelemetry?.CpuHistory.Count == 30, "Telemetry holds 30 samples");
        var pairing = new PairingApprovalService(Path.GetFullPath("artifacts/ui-review/desktop-pairing.json"));
        var runtime = new WindowsAgentRuntime(state, pairing, new AgentOptions(true), listenPortOverride: 0);
        using var icon = AppIconFactory.CreateIcon(); using var window = new SettingsWindow(state, runtime, new WindowsStartupService(new AgentOptions(true)), pairing, icon, fixture);
        window.ShowInTaskbar = false; window.StartPosition = FormStartPosition.Manual; window.Location = new Point(-2500, -2500); window.Show();
        void Navigate(string name) {
            var btn = All(window).OfType<Button>().FirstOrDefault(b =>
                b.AccessibleName == name || b.Text == name ||
                (name is "Deck Builder" or "Shortcuts" && (b.Text is "Deck Builder" || b.AccessibleName is "Deck Builder")) ||
                (name is "Settings" or "Appearance" && (b.Text is "Settings" or "Ustawienia" || b.AccessibleName is "Settings" or "Ustawienia")) ||
                (name is "Media Controls" or "Media" && (b.Text is "Media Controls" or "Multimedia" or "Media" || b.AccessibleName is "Media Controls" or "Multimedia" or "Media")) ||
                (name is "System Monitor" or "Monitor" && (b.Text is "System Monitor" or "Monitor systemu" or "Monitor" || b.AccessibleName is "System Monitor" or "Monitor systemu" or "Monitor")) ||
                (name is "Urządzenia" or "Device" or "Pairing" && (b.Text is "Urządzenia" or "Urządzenie" or "Device" || b.AccessibleName is "Urządzenia" or "Urządzenie" or "Device")));
            if (btn is not null) { btn.PerformClick(); Application.DoEvents(); }
            else { window.ShowPage(name switch { "Deck Builder" => "Shortcuts", "Settings" => "Appearance", "Media Controls" => "Media", "System Monitor" => "Monitor", "Urządzenia" => "Pairing", _ => name }); Application.DoEvents(); }
        }
        foreach (var size in new[] { new Size(1200, 780), new Size(1280, 800) }) {
            window.Size = size;
            foreach (var page in new[] { "Deck Builder", "Settings", "Media Controls", "System Monitor", "Urządzenia" }) {
                Navigate(page); for (var i = 0; i < 5; ++i) { Application.DoEvents(); Thread.Sleep(20); }
                var activePage = ActivePage(window);
                var body = All(activePage).OfType<Panel>().Single(p => Equals(p.Tag, "settings-page-body")); Check(!body.AutoScroll && !body.VerticalScroll.Visible && !body.HorizontalScroll.Visible, page + " viewport has no scroll");
                var connection = All(activePage).OfType<Label>().Single(c => c.AccessibleName == "Device connection"); Check(connection.Width >= TextRenderer.MeasureText(connection.Text, connection.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width, "Connected Ready status fully fits");
                var toolbar = All(activePage).Single(c => c.AccessibleName == "Unified Toolbar"); Check(toolbar.Height <= 120, "One compact toolbar");
                foreach (var control in All(body).Where(c => c.Visible && c is TextBox or ComboBox or NumericUpDown or TrackBar or Button)) {
                    var parent = control.Parent; bool inLibrary = false; while (parent is not null && parent != body) { if (parent.AccessibleName is "Shortcut Library Scroll" or "Lista skrótów" or "Device List Scroll" or "Lista urządzeń" || parent.Name is "DeckLibraryScroll" or "DeviceListScroll") inLibrary = true; parent = parent.Parent; }
                    if (inLibrary) continue;
                    Check(control.Parent!.ClientRectangle.Contains(control.Bounds), page + " control is not clipped by parent: " + control.AccessibleName + " " + control.Bounds + " parent " + control.Parent.ClientRectangle);
                    var bounds = new Rectangle(body.PointToClient(control.PointToScreen(Point.Empty)), control.Size);
                    Check(control.Width > 0 && control.Height > 0 && body.ClientRectangle.Contains(bounds), page + " control stays in viewport: " + control.AccessibleName + " " + bounds);
                }
                if (page == "Settings") { var cards = All(body).Where(c => c.AccessibleName is "Desktop Appearance" or "Display Appearance" or "Hardware & Connection").ToArray(); Check(cards.Length == 3 && cards.Select(c => c.Top).Distinct().Count() == 1, "Three settings columns"); }
                if (page == "Urządzenia") { var cards = All(body).Where(c => c.AccessibleName is "Connection status & guide" or "Pending devices" or "Trusted devices").ToArray(); Check(cards.Length == 3, "Three pairing cards"); }
                if (page == "System Monitor") foreach (var label in All(body).OfType<Label>().Where(l => l.Text is "Now Playing" or "Uptime")) Check(label.Width >= TextRenderer.MeasureText(label.Text, label.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width, "Telemetry label does not wrap: " + label.Text);
                using var render = new Bitmap(window.Width, window.Height); window.DrawToBitmap(render, new Rectangle(Point.Empty, window.Size)); render.Save($"artifacts/ui-review/desktop-{page.Replace(' ', '-')}-{size.Width}.png");
            }
        }
        // Warm navigation benchmark across the 5 main views
        var mainPageKeys = new[] { "Shortcuts", "Media", "Monitor", "Appearance", "Pairing" };
        var samplesByPage = mainPageKeys.ToDictionary(k => k, _ => new List<double>(), StringComparer.Ordinal);
        for (var round = 0; round < 7; ++round) {
            foreach (var key in mainPageKeys) {
                var sw = Stopwatch.StartNew();
                window.ShowPage(key);
                sw.Stop();
                samplesByPage[key].Add(sw.Elapsed.TotalMilliseconds);
                Application.DoEvents();
            }
        }
        foreach (var (key, times) in samplesByPage) {
            times.Sort();
            var p50 = times[times.Count / 2];
            var p95 = times[(int)Math.Min(times.Count - 1, Math.Ceiling(times.Count * 0.95) - 1)];
            Console.WriteLine($"PERF ShowPage({key}): warm p50={p50:F2}ms, p95={p95:F2}ms");
            Check(p50 < 50.0, $"Warm ShowPage({key}) p50 ({p50:F2} ms) must stay below 50 ms budget");
        }

        Navigate("Media Controls");
        var mediaPage = ActivePage(window);
        var volume = All(mediaPage).Single(t => t.AccessibleName == "System volume");
        if (volume is TrackBar tb) tb.Value = 32;
        else if (volume is SettingsWindow.DarkSlider ds) ds.Value = 32;
        for (var i = 0; i < 12; ++i) { Application.DoEvents(); Thread.Sleep(20); }
        Check(state.Snapshot().CurrentAudio?.VolumePercent == 32, "Volume slider updates the shared audio state");
        All(mediaPage).OfType<Button>().Single(b => b.AccessibleName == "Mute" || b.Text is "Mute" or "Wycisz").PerformClick(); Application.DoEvents(); Check(state.Snapshot().CurrentAudio?.IsMuted == true, "Mute updates the shared audio state");
        Navigate("Deck Builder");
        var deckPage = ActivePage(window);
        var preview = All(deckPage).OfType<DesktopCanvas>().Single(); var library = All(deckPage).OfType<FlowLayoutPanel>().Single(c => c.AccessibleName is "Shortcut Library Scroll" or "Lista skrótów" || c.Name == "DeckLibraryScroll"); Check(library.VerticalScroll.Visible, "Large library uses internal scroll");
        preview.UnassignSlot!(0); Application.DoEvents(); Check(preview.Slots[0] is null, "Unassign clears slot"); Check(fixture.LoadOrDefault().Shortcuts!.Count == 24, "Unassign never deletes saved library");
        All(deckPage).OfType<Button>().Single(b => b.AccessibleName is "Assign Spotify" or "Przypisz Spotify").PerformClick(); Application.DoEvents(); Check(preview.Slots[0]?.Id == "desktop-0", "Assign reuses selected empty slot");
        var defaultColor = All(deckPage).OfType<Button>().Single(b => b.AccessibleName == "Default" || b.Text is "Default" or "Domyślny");
        Check(defaultColor.Width >= TextRenderer.MeasureText(defaultColor.Text, defaultColor.Font, Size.Empty, TextFormatFlags.NoPadding).Width + 16, "Default caption fits");
        All(deckPage).OfType<Button>().Single(b => b.AccessibleName == "Cobalt").PerformClick(); defaultColor.PerformClick();
        var titleInput = All(deckPage).OfType<TextBox>().Single(t => t.AccessibleName is "Title" or "Tytuł" or "Title / Label" or "Tytuł / Etykieta" || t.Name == "DeckActionName"); var iconButton = All(deckPage).OfType<Button>().Single(b => b.AccessibleName is "Icon" or "Ikona");
        Check(titleInput.Parent!.Height == iconButton.Height && iconButton.Height == 32, "Title and Icon aligned at 32 pixels");
        var name = All(deckPage).OfType<TextBox>().Single(c => c.AccessibleName is "Title" or "Tytuł" or "Title / Label" or "Tytuł / Etykieta" || c.Name == "DeckActionName"); name.Text = "Draft title";
        Navigate("Settings"); Navigate("Deck Builder"); Check(All(ActivePage(window)).OfType<TextBox>().Single(c => c.AccessibleName is "Title" or "Tytuł" or "Title / Label" or "Tytuł / Etykieta" || c.Name == "DeckActionName").Text == "Draft title", "Draft survives navigation"); Check(fixture.LoadOrDefault().Shortcuts![0].Name == "Spotify", "Draft does not write file");
        All(ActivePage(window)).OfType<RadioButton>().Single(b => b.Text is "Home Screen (4)" or "Ekran główny (4)").PerformClick(); Application.DoEvents(); Check(All(ActivePage(window)).OfType<DesktopCanvas>().Single().Page == "Home", "Home Screen preview switch");
        Check(window.SaveConfigurationDraftAsync().GetAwaiter().GetResult(), "Offline save completes"); Check(fixture.LoadOrDefault().Shortcuts![0].Name == "Draft title", "Save persists draft");
        var saved = fixture.LoadOrDefault(); Check(saved.Profiles[0].AssignedShortcutIds?.Count == 6, "Assigned slots persist per Deck Profile");
        var device = WindowsAgentRuntime.BuildDeviceShortcuts(ShortcutCatalog.Create(new ShortcutConfiguration { Shortcuts = shortcuts }).DeviceConfiguration, [null, "desktop-1", null, "desktop-0", null, null]); Check(device.Shortcuts[0].Id == "__empty_slot_0" && device.Shortcuts[1].Id == "desktop-1", "Empty slot keeps following positions stable");
        Check(WindowsAgentRuntime.ResolveAssignedLayout(new AppearanceConfiguration(), [null, null, null, null, null, "desktop-0"]).ShortcutsLayout == "grid-6", "Auto layout preserves high assigned position");
        Navigate("Settings"); All(ActivePage(window)).OfType<RadioButton>().Single(b => b.Text is "Light" or "Jasny").PerformClick();
        Check(window.SaveConfigurationDraftAsync().GetAwaiter().GetResult(), "Light theme save");
        foreach (var viewName in new[] { "Deck Builder", "Settings", "Media Controls", "System Monitor", "Urządzenia" }) { Navigate(viewName); Application.DoEvents(); using var render = new Bitmap(window.Width, window.Height); window.DrawToBitmap(render, new Rectangle(Point.Empty, window.Size)); render.Save($"artifacts/ui-review/desktop-{viewName.Replace(' ', '-')}-light.png"); }

        // Pairing fixture captures: empty, pending, trusted
        Navigate("Urządzenia");
        for (var i = 0; i < 6; ++i) { Application.DoEvents(); Thread.Sleep(20); }
        using (var renderEmpty = new Bitmap(window.Width, window.Height)) { window.DrawToBitmap(renderEmpty, new Rectangle(Point.Empty, window.Size)); renderEmpty.Save("artifacts/ui-review/desktop-pairing-empty.png"); }

        // Pending fixture: add pending device
        pairing.AuthorizeAsync(new HelloMessage("cyd-esp32-2432s028", "CYD Studio (ESP32)", "esp32", []), null).AsTask().GetAwaiter().GetResult();
        for (var i = 0; i < 10; ++i) { Application.DoEvents(); Thread.Sleep(20); }
        using (var renderPending = new Bitmap(window.Width, window.Height)) { window.DrawToBitmap(renderPending, new Rectangle(Point.Empty, window.Size)); renderPending.Save("artifacts/ui-review/desktop-pairing-pending.png"); }

        // Approve fixture: click Zatwierdź via UI button
        var approveBtn = All(ActivePage(window)).OfType<Button>().Single(b => b.AccessibleName == "Zatwierdź cyd-esp32-2432s028");
        approveBtn.PerformClick();
        for (var i = 0; i < 10; ++i) { Application.DoEvents(); Thread.Sleep(20); }
        using (var renderTrusted = new Bitmap(window.Width, window.Height)) { window.DrawToBitmap(renderTrusted, new Rectangle(Point.Empty, window.Size)); renderTrusted.Save("artifacts/ui-review/desktop-pairing-trusted.png"); }

        window.CloseForExit(); SynchronizationContext.SetSynchronizationContext(null); runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Console.WriteLine("PASS Desktop viewport: five views, two sizes, internal library scroll, draft, slots, telemetry, pairing fixtures, renders");
    }
    private sealed class Thumbnail(byte[] bytes) : IMediaThumbnailReference { public ValueTask<Stream?> OpenReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<Stream?>(new MemoryStream(bytes)); }
}
