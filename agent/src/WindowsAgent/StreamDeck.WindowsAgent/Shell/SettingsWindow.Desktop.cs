using System.Drawing.Drawing2D;
using StreamDeck.Configuration;
using StreamDeck.Media;
using StreamDeck.Protocol;
using StreamDeck.Shortcuts;
using StreamDeck.WindowsAgent.Runtime;
using Shortcut = StreamDeck.Shortcuts.ShortcutDefinition;

namespace StreamDeck.WindowsAgent.Shell;

public sealed partial class SettingsWindow
{
    private Action? desktopRefresh;
    private CancellationTokenSource? desktopViewLifetime;
    private Bitmap? desktopArtwork;
    private string? desktopArtworkKey;
    private readonly ToolTip desktopTips = new();
    private ContextMenuStrip? desktopToolsMenu;
    private static int Dip(Control control, int value) => (int)Math.Round(value * control.DeviceDpi / 96f);
    private void MarkDraft() { configurationDirty = true; RefreshUnifiedToolbar(); }
    private Control CreateDesktopNavigation()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 20, 12, 16), BackColor = Surface };
        var brand = new Label { Text = "CYD-DECK", Dock = DockStyle.Top, Height = 56, Font = new Font(Font.FontFamily, 17, FontStyle.Bold), ForeColor = TextPrimary };
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 8, 0, 0), Margin = Padding.Empty };
        foreach (var name in new[] { "Shortcuts", "Media", "Monitor", "Appearance", "Pairing" }) {
            var button = CreateNavButton(name); button.AccessibleName = DisplayPageName(name); navButtons[name] = button; nav.Controls.Add(button);
        }
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 74, Margin = Padding.Empty };
        var tools = CompactButton(T("Tools"), () => {
            if (desktopToolsMenu is null || desktopToolsMenu.IsDisposed) {
                desktopToolsMenu = new ContextMenuStrip { BackColor = Surface, ForeColor = TextPrimary };
                foreach (var name in new[] { "Device", "Pairing", "Updates", "Logs" }) desktopToolsMenu.Items.Add(DisplayPageName(name), null, (_, _) => ShowPage(name));
            }
            desktopToolsMenu.BackColor = Surface; desktopToolsMenu.ForeColor = TextPrimary;
            desktopToolsMenu.Show(footer, new Point(0, 0)); return Task.CompletedTask;
        });
        tools.AccessibleName = "Device & Tools";
        tools.Dock = DockStyle.Top; tools.Width = 180; tools.Margin = Padding.Empty;
        footer.Controls.Add(new Label { Text = "Windows Agent · " + typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3), Dock = DockStyle.Bottom, Height = 26, ForeColor = TextMuted, Font = new Font(Font.FontFamily, 10) }); footer.Controls.Add(tools);
        panel.Controls.Add(nav); panel.Controls.Add(brand); panel.Controls.Add(footer);
        return panel;
    }
    private Control CreateDesktopToolbar(string page)
    {
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 104, ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 4, 0, 8), AccessibleName = "Unified Toolbar", Name = "UnifiedToolbar" };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        toolbar.Controls.Add(new Label { Text = DisplayPageName(page), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Font = UiTokens.Type.Create(UiTokens.Type.Title, FontStyle.Bold) }, 0, 0);
        var configuration = LoadConfiguration(); var profiles = GetProfiles(configuration);
        var profile = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, FlowDirection = FlowDirection.LeftToRight };
        profile.Controls.Add(new Label { Text = T("Profile"), AutoSize = true, ForeColor = TextMuted, Padding = new Padding(0, 8, 0, 0) });
        var picker = new DarkComboBox { Width = 138, AccessibleName = "Deck Profile", Name = "DeckProfile", Margin = new Padding(4, 2, 4, 0) };
        picker.Items.AddRange(profiles.Select(p => p.Name).Cast<object>().ToArray()); picker.SelectedItem = configuration.ActiveProfile;
        picker.SelectedIndexChanged += (_, _) => {
            if (updatingProfilePicker || picker.SelectedItem is not string target) return;
            RequestProfileSwitch(target, picker);
        };
        unifiedProfilePicker = picker;
        profile.Controls.Add(picker);
        foreach (var item in new[] { ("+", "AddProfile", false, false), ("⧉", "DuplicateProfile", true, false), ("−", "DeleteProfile", false, true) }) {
            var button = CompactButton(item.Item1, () => {
                var activeName = LoadConfiguration().ActiveProfile;
                if (!item.Item4 || MessageBox.Show(this, F("DeleteProfileQuestion", activeName), T("DeleteProfile"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                    ChangeProfile(item.Item3, item.Item4);
                return Task.CompletedTask;
            });
            button.Width = 32; button.Height = 32; button.Margin = new Padding(1, 2, 1, 0);
            button.AccessibleName = T(item.Item2); button.Name = item.Item2;
            button.Enabled = !item.Item4 || profiles.Length > 1;
            if (item.Item4) button.ForeColor = Danger;
            desktopTips.SetToolTip(button, button.AccessibleName); profile.Controls.Add(button);
        }
        toolbar.Controls.Add(profile, 1, 0);
        var stateRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        stateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        stateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        stateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        stateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 192));
        unifiedConnection = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, AccessibleName = "Device connection", Name = "DeviceConnection" };
        stateRow.Controls.Add(unifiedConnection, 0, 0);
        var refresh = CompactButton("↻", ReconnectAsync); refresh.Dock = DockStyle.Fill; refresh.AccessibleName = T("Reconnect"); refresh.Name = "ReconnectDevice"; desktopTips.SetToolTip(refresh, refresh.AccessibleName); stateRow.Controls.Add(refresh, 1, 0);
        unifiedFeedback = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, AccessibleName = "Configuration state", Name = "ConfigurationState", Font = UiTokens.Type.Create(UiTokens.Type.Caption), Padding = new Padding(8, 0, 4, 0) };
        stateRow.Controls.Add(unifiedFeedback, 2, 0);
        unifiedSave = CompactButton(T("SaveSync"), async () => {
            if (configurationDirty) await SaveConfigurationDraftAsync();
            else await RetrySavedConfigurationAsync();
        });
        unifiedSave.Dock = DockStyle.Fill; unifiedSave.AccessibleName = "Save & Sync"; unifiedSave.Name = "SaveAndSync";
        stateRow.Controls.Add(unifiedSave, 3, 0);
        toolbar.Controls.Add(stateRow, 0, 1); toolbar.SetColumnSpan(stateRow, 2);
        RefreshDesktopToolbar();
        return toolbar;
    }

    private void RequestProfileSwitch(string targetName, ComboBox picker)
    {
        var previous = LoadConfiguration().ActiveProfile;
        if (targetName == previous) return;
        void RestorePicker() {
            if (picker.IsDisposed) return;
            updatingProfilePicker = true; picker.SelectedItem = previous; updatingProfilePicker = false;
        }
        void Switch() {
            if (IsDisposed || Disposing) return;
            var current = LoadConfiguration(); var all = GetProfiles(current);
            var next = all.FirstOrDefault(p => p.Name == targetName);
            if (next is null) { RestorePicker(); return; }
            configurationDraft = current with { Profiles = all, ActiveProfile = next.Name, Shortcuts = next.Shortcuts };
            MarkDraft(); InvalidatePageCache(); ShowPage(currentPage);
        }
        if (savingConfiguration) { RestorePicker(); return; }
        if (configurationDirty && !ResolveUnsavedChanges(Switch)) { RestorePicker(); return; }
        Switch();
    }
    private void RefreshDesktopToolbar()
    {
        var snapshot = state.Snapshot(); var connection = snapshot.Connection;
        var connected = connection?.IsConnected == true;
        if (unifiedProfilePicker is { IsDisposed: false }) {
            var configuration = LoadConfiguration();
            if (!Equals(unifiedProfilePicker.SelectedItem, configuration.ActiveProfile)) {
                updatingProfilePicker = true; unifiedProfilePicker.SelectedItem = configuration.ActiveProfile; updatingProfilePicker = false;
            }
            unifiedProfilePicker.Enabled = !savingConfiguration;
        }
        var phase = snapshot.Synchronization?.Status;
        var failure = phase is ConfigurationSyncStatus.Failed or ConfigurationSyncStatus.Disconnected or ConfigurationSyncStatus.TimedOut or ConfigurationSyncStatus.Superseded;
        var sending = savingConfiguration || phase == ConfigurationSyncStatus.Sending;
        var caption = lastActionError ?? (sending ? T("Sending") : configurationDirty ? T("Dirty") : failure ? T("SyncFailed") : phase == ConfigurationSyncStatus.SentUnconfirmed ? T("Sent") : phase == ConfigurationSyncStatus.SavedOffline ? T("Queued") : T("NoChanges"));
        var help = configurationDirty ? T("DirtyHelp") : failure ? T("RetryHelp") : phase == ConfigurationSyncStatus.SentUnconfirmed ? T("SentHelp") : phase == ConfigurationSyncStatus.SavedOffline ? T("QueuedHelp") : T("NoChangesHelp");
        if (unifiedFeedback is { IsDisposed: false }) {
            unifiedFeedback.Text = caption;
            unifiedFeedback.ForeColor = lastActionError is not null || failure ? Danger : configurationDirty ? UiTokens.Warning : TextMuted;
            desktopTips.SetToolTip(unifiedFeedback, lastActionError ?? help);
        }
        if (unifiedConnection is { IsDisposed: false }) {
            var transport = connection?.Transport == DeviceTransport.Usb ? connection.ActiveUsbPort ?? "USB" : connection?.RemoteAddress ?? "Wi-Fi";
            unifiedConnection.Text = connected ? "● " + transport + " · " + T("Connected") : "● " + T("Offline");
            unifiedConnection.ForeColor = connected ? (clientLightTheme ? Color.DarkGreen : UiTokens.Success) : TextMuted;
            desktopTips.SetToolTip(unifiedConnection, string.Join(Environment.NewLine, new[] { connection?.DeviceId, T("ActivePort") + ": " + (connection?.ActiveUsbPort ?? "—"), T("SavedPort") + ": " + (connection?.SavedUsbPort ?? T("Automatic")), T("RuntimeOverride") + ": " + (connection?.CliUsbPortOverride ?? "—") }));
        }
        if (unifiedSave is { IsDisposed: false }) {
            unifiedSave.Enabled = !sending && (configurationDirty || connected);
            unifiedSave.Text = sending ? T("Sending") : configurationDirty ? T("SaveSync") : T("RetrySync");
            unifiedSave.BackColor = unifiedSave.Enabled ? Accent : SurfaceAlt;
            unifiedSave.ForeColor = unifiedSave.Enabled ? UiTokens.OnAccent(Accent) : TextMuted;
            desktopTips.SetToolTip(unifiedSave, configurationDirty ? T("DirtyHelp") : T("RetryHelp"));
        }
    }
    private static Color ReadableOn(Color color)
    {
        static double Channel(byte value) { var c = value / 255.0; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        var light = .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        return light > .179 ? Color.FromArgb(16, 18, 22) : Color.White;
    }
    private static void FitDesktopRows(Control root)
    {
        if (root is TableLayoutPanel table) {
            if (table.RowStyles.Count == 0 && table.RowCount <= 1) { table.RowCount = 1; table.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); }
            if (table.ColumnStyles.Count == 0 && table.ColumnCount <= 1) { table.ColumnCount = 1; table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); }
        }
        foreach (Control child in root.Controls) FitDesktopRows(child);
    }

    private TableLayoutPanel DesktopCard(string title, string? accessibleName = null)
    {
        var card = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, Padding = new Padding(16), Margin = new Padding(6), BackColor = Surface };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); card.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        card.Controls.Add(new Label { Text = title, UseMnemonic = false, Dock = DockStyle.Fill, AutoEllipsis = true, Font = new Font(Font.FontFamily, 11, FontStyle.Bold), ForeColor = TextPrimary }, 0, 0);
        card.AccessibleName = accessibleName ?? title;
        void Round() { if (card.Width <= 0 || card.Height <= 0) return; using var path = CreateRoundedRectPath(new Rectangle(0, 0, card.Width - 1, card.Height - 1), 14); var old = card.Region; card.Region = new Region(path); old?.Dispose(); }
        card.SizeChanged += (_, _) => Round();
        card.Paint += (_, e) => { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var path = CreateRoundedRectPath(new Rectangle(0, 0, card.Width - 1, card.Height - 1), 14); using var border = new Pen(Border); e.Graphics.DrawPath(border, path); };
        return card;
    }
    private static void FinishCard(TableLayoutPanel card) { card.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); card.RowCount++; }
    private Control Segments((string Label, string Value)[] options, string current, Action<string> changed)
    {
        var panel = new SegmentedGroup { ColumnCount = options.Length, RowCount = 1 };
        foreach (var option in options) {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / options.Length));
            var button = new RoundedSegment { Appearance = Appearance.Button, Text = option.Label, AccessibleName = option.Label, Checked = option.Value == current, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, FlatStyle = FlatStyle.Flat, Margin = new Padding(1, 0, 1, 0), ForeColor = TextPrimary, BackColor = SurfaceAlt, Font = new Font(Font.FontFamily, 9.5f) };
            button.FlatAppearance.BorderColor = Border; button.FlatAppearance.CheckedBackColor = clientLightTheme ? Color.FromArgb(216, 232, 252) : Color.FromArgb(42, 57, 76);
            button.CheckedChanged += (_, _) => { if (button.Checked) { changed(option.Value); MarkDraft(); } }; panel.Controls.Add(button);
        }
        return panel;
    }
    private Control Swatches((string Name, string Color)[] choices, string current, Action<string> update)
    {
        var panel = new FlowLayoutPanel { WrapContents = false, Margin = Padding.Empty, Tag = current }; var value = current;
        foreach (var choice in choices) {
            var button = CompactButton("", () => {
                value = choice.Color;
                panel.Tag = value;
                update(value);
                MarkDraft();
                panel.Invalidate(true);
                return Task.CompletedTask;
            });
            button.Size = new Size(26, 26); button.Margin = new Padding(2, 1, 2, 1); button.ShowOutline = false; button.FlatAppearance.BorderSize = 0; button.AccessibleName = choice.Name; button.BackColor = Surface;
            button.Paint += (_, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var color = ColorTranslator.FromHtml(choice.Color);
                using var fill = new SolidBrush(color);
                var circle = Rectangle.Inflate(button.ClientRectangle, -3, -3);
                e.Graphics.FillEllipse(fill, circle);

                if (string.Equals(panel.Tag as string, choice.Color, StringComparison.OrdinalIgnoreCase))
                {
                    using var outerRing = new Pen(clientLightTheme ? Color.Black : Color.White, 2f);
                    e.Graphics.DrawEllipse(outerRing, circle);
                    var luminance = (color.R * 0.299 + color.G * 0.587 + color.B * 0.114) / 255.0;
                    var checkColor = luminance > 0.55 ? Color.Black : Color.White;
                    using var checkFont = new Font(button.Font.FontFamily, 8.5f, FontStyle.Bold);
                    TextRenderer.DrawText(e.Graphics, "✓", checkFont, circle, checkColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            };
            desktopTips.SetToolTip(button, $"{choice.Name} ({choice.Color})"); panel.Controls.Add(button);
        }
        var custom = CompactButton(T("CustomColor"), () => {
            if (ModernColorPickerDialog.ShowColorPicker(this, value, out var picked)) {
                value = picked;
                panel.Tag = value;
                update(value);
                MarkDraft();
                panel.Invalidate(true);
            }
            return Task.CompletedTask;
        });
        custom.AutoSize = false;
        custom.Size = new Size(76, 26);
        custom.Margin = new Padding(4, 1, 2, 1);
        custom.AccessibleName = "Custom color";
        custom.Paint += (_, e) => {
            var isCustom = !choices.Any(c => string.Equals(c.Color, panel.Tag as string, StringComparison.OrdinalIgnoreCase));
            if (isCustom && panel.Tag is string hex && hex.Length >= 4)
            {
                try
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    var customColor = ColorTranslator.FromHtml(hex);
                    using var pipBrush = new SolidBrush(customColor);
                    var pipRect = new Rectangle(custom.Width - 18, (custom.Height - 10) / 2, 10, 10);
                    e.Graphics.FillEllipse(pipBrush, pipRect);
                    using var pipRing = new Pen(Color.White, 1f);
                    e.Graphics.DrawEllipse(pipRing, pipRect);
                }
                catch { }
            }
        };
        desktopTips.SetToolTip(custom, "Wybierz własny kolor z nowoczesnego próbnika");
        panel.Controls.Add(custom);
        return panel;
    }
    private Control ToggleRow(string label, bool value, Action<bool> change)
    {
        var row = new TableLayoutPanel { ColumnCount = 2 }; row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        row.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        var toggle = new CompactToggle { Dock = DockStyle.Fill, Checked = value, AccessibleName = label, ForeColor = TextPrimary, Text = "" }; toggle.CheckedChanged += (_, _) => { change(toggle.Checked); MarkDraft(); }; row.Controls.Add(toggle, 1, 0); return row;
    }
    private Control CreateDesktopSettingsPage()
    {
        var page = CreatePage("Appearance"); var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        for (var i = 0; i < 3; ++i) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        pendingDesktopAppearance ??= LoadDesktopAppearanceConfiguration(); pendingAppearance ??= LoadAppearanceConfiguration(); var desktop = pendingDesktopAppearance; var display = pendingAppearance;

        var windows = DesktopCard("Wygląd aplikacji (PC)", "Desktop Appearance");
        CompactField(windows, "Motyw systemu", Segments([("Dark", "dark"), ("Light", "light"), ("System", "system")], desktop.Theme, v => desktop.Theme = v));
        CompactField(windows, "Akcent interfejsu", Swatches([("Blue", "#0A84FF"), ("Indigo", "#6366F1"), ("Cyan", "#0891B2"), ("Emerald", "#059669")], desktop.Accent, v => {
            desktop.Accent = v;
            try { Accent = ColorTranslator.FromHtml(v); } catch { }
            Invalidate(true);
        }));
        CompactField(windows, "Kopia zapasowa", CompactButton("Eksportuj konfigurację…", ExportConfigurationAsync));
        CompactField(windows, "Przywracanie", CompactButton("Importuj konfigurację…", ImportConfigurationAsync));
        FinishCard(windows);

        var cyd = DesktopCard("Wygląd urządzenia (CYD)", "Display Appearance");
        CompactField(cyd, "Kolor tła CYD", Segments([("OLED Black", "black"), ("Tinted", "tinted"), ("Custom", "custom")], display.Background == "#000000" ? "black" : (display.Background == "#101216" ? "tinted" : "custom"), v => {
            if (v == "custom") {
                if (ModernColorPickerDialog.ShowColorPicker(this, display.Background, out var picked)) display.Background = picked;
            } else display.Background = v == "black" ? "#000000" : "#101216";
        }));
        CompactField(cyd, "Tile Theme", Swatches([("Graphite", "#64748B"), ("Ocean", "#0891B2"), ("Cobalt", "#0A84FF"), ("Ember", "#D97706")], display.Accent, v => { display.Accent = v; display.TileColorScheme = "accent-tint"; }));
        CompactField(cyd, "Shortcuts Layout", Segments([("Auto", "auto"), ("1", "hero-1"), ("2", "split-2"), ("4", "quad-4"), ("6", "grid-6")], display.ShortcutsLayout, v => display.ShortcutsLayout = v));
        desktopTips.SetToolTip(cyd.Controls[^1], "Układ kafelków na CYD: Auto dopasowuje siatkę od 1 do 6 kafelków");
        CompactField(cyd, "", ToggleRow("Format zegara 24h", desktop.Clock24Hour, v => desktop.Clock24Hour = v), 40);
        CompactField(cyd, "StandBy Mode", Segments([("iOS StandBy", "apple-standby"), ("Minimal", "minimal-oled")], display.StandbyStyle, v => display.StandbyStyle = v));

        var brightnessRow = new TableLayoutPanel { ColumnCount = 2, Margin = Padding.Empty }; brightnessRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); brightnessRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        var slider = new DarkSlider { Dock = DockStyle.Fill, Minimum = 10, Maximum = 100, Value = Math.Clamp(display.Brightness, 10, 100), AccessibleName = "Brightness", Margin = Padding.Empty };
        var percent = new Label { Text = display.Brightness + "%", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = TextPrimary, Margin = Padding.Empty };
        slider.ValueChanged += (_, _) => { display.Brightness = slider.Value; percent.Text = slider.Value + "%"; MarkDraft(); };
        brightnessRow.Controls.Add(slider, 0, 0); brightnessRow.Controls.Add(percent, 1, 0); CompactField(cyd, "Brightness", brightnessRow);

        var local = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = TextMuted, Text = "Nieraportowane (CYD nie przesyła pozycji fizycznego pokrętła)", AccessibleName = "Local Brightness Override" };
        desktopTips.SetToolTip(local, "Gdy zmienisz jasność pokrętłem lub przyciskami fizycznymi na CYD, zostanie ona zachowana do kolejnego zapisu.");
        CompactField(cyd, "Local Brightness Override", local, 44);

        CompactField(cyd, "Corners", Segments([("Rounded", "rounded"), ("Flat", "flat"), ("Pill", "pill")], display.ButtonStyle, v => display.ButtonStyle = v));
        FinishCard(cyd);

        var hardware = DesktopCard("Połączenie i sprzęt", "Hardware & Connection"); var server = LoadConfiguration().Server ?? new();
        var ports = new DarkComboBox { Dock = DockStyle.Fill, AccessibleName = "COM Port", Margin = Padding.Empty };
        bool refreshingPorts = false;
        void RefreshPorts() { refreshingPorts = true; var selected = ports.SelectedItem?.ToString() ?? server.UsbPort ?? "Automatic"; ports.Items.Clear(); ports.Items.AddRange(new[] { "Automatic" }.Concat(System.IO.Ports.SerialPort.GetPortNames()).Concat(server.UsbPort is null ? [] : new[] { server.UsbPort }).Distinct().Cast<object>().ToArray()); ports.SelectedItem = ports.Items.Contains(selected) ? selected : "Automatic"; refreshingPorts = false; }
        RefreshPorts();
        ports.SelectedIndexChanged += (_, _) => {
            if (refreshingPorts) return;
            var chosen = ports.SelectedItem?.ToString() is "Automatic" ? null : ports.SelectedItem?.ToString();
            server = server with { UsbPort = chosen };
            configurationDraft = (configurationDraft ?? LoadConfiguration()) with { Server = server };
            MarkDraft();
        };
        desktopTips.SetToolTip(ports, "Wybierz port COM urządzenia. Kliknij ↪ aby zapisać port i połączyć ponownie.");
        var portRow = new TableLayoutPanel { ColumnCount = 3, Margin = Padding.Empty }; portRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); portRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32)); portRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32)); portRow.Controls.Add(ports, 0, 0);
        var refresh = CompactButton("↻", () => { RefreshPorts(); return Task.CompletedTask; }); refresh.Dock = DockStyle.Fill; refresh.Margin = Padding.Empty; refresh.AccessibleName = "Refresh COM ports"; desktopTips.SetToolTip(refresh, "Odśwież listę portów COM"); portRow.Controls.Add(refresh, 1, 0);
        var reconnect = CompactButton("↪", async () => await ReconnectWithSelectedPortAsync(ports.SelectedItem?.ToString())); reconnect.Dock = DockStyle.Fill; reconnect.Margin = Padding.Empty; reconnect.AccessibleName = "Połącz z wybranym portem"; desktopTips.SetToolTip(reconnect, "Zastosuj wybrany port i połącz ponownie z urządzeniem"); portRow.Controls.Add(reconnect, 2, 0); CompactField(hardware, "COM Port", portRow);

        var standby = new DarkNumberPicker(display.DimAfterSec);
        standby.ValueChanged += (_, _) => { display.DimAfterSec = standby.Value; MarkDraft(); };
        CompactField(hardware, "Standby Timeout (s)", standby);

        var powerNote = new Label { Text = "Ekran aktywny przy podłączonym PC (USB keep-awake)", AutoEllipsis = true, ForeColor = TextMuted, Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 8.5f) };
        CompactField(hardware, "Display Power", powerNote, 46);

        CompactField(hardware, "", ToggleRow("Automatyczne łączenie przy starcie", server.UsbAutoConnect, v => { server = server with { UsbAutoConnect = v }; configurationDraft = (configurationDraft ?? LoadConfiguration()) with { Server = server }; }), 44);
        CompactField(hardware, "", ToggleRow("Wybudzanie dotykiem ekranu", display.WakeOnTouch, v => display.WakeOnTouch = v), 44);

        CompactField(hardware, "Narzędzia", CompactButton("Urządzenia i parowanie…", () => { ShowPage("Pairing"); return Task.CompletedTask; }));
        CompactField(hardware, "", CompactButton("Przywróć domyślne", () => { if (MessageBox.Show(this, "Czy na pewno chcesz przywrócić domyślne ustawienia wyglądu i sprzętu w szkicu roboczym?", "Przywracanie domyślnych", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK) { pendingAppearance = AppearanceState.Default; pendingDesktopAppearance = DesktopAppearanceState.Default; configurationDraft = (configurationDraft ?? LoadConfiguration()) with { Server = new() }; MarkDraft(); InvalidatePageCache(); ShowPage("Appearance"); } return Task.CompletedTask; }));
        FinishCard(hardware);

        grid.Controls.Add(windows, 0, 0); grid.Controls.Add(cyd, 1, 0); grid.Controls.Add(hardware, 2, 0); AddPageBodyControl(page, grid); return page;
    }
}
