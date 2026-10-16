using StreamDeck.Configuration;
using StreamDeck.Shortcuts;
using StreamDeck.Protocol;
using ShortcutDefinition = StreamDeck.Shortcuts.ShortcutDefinition;

namespace StreamDeck.WindowsAgent.Shell;

public sealed partial class SettingsWindow
{
    private AgentConfiguration? configurationDraft;
    private bool configurationDirty;
    private bool savingConfiguration;
    private Button? unifiedSave;
    private Label? unifiedConnection;
    private Label? unifiedFeedback;
    private readonly List<Control> settingsCards = [];
    private readonly Dictionary<TableLayoutPanel, TabControl> settingsCardPages = [];
    private string? connectedUsbPort;
    private string configurationFeedback = "Ready";

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        MinimumSize = new Size(MinimumWindowWidth, MinimumWindowHeight);
    }

    private static string DisplayPageName(string page) => page switch
    {
        "Shortcuts" => "Deck Builder",
        "Appearance" => "Settings",
        _ => page
    };

    private Control CreateUnifiedToolbar(string title)
    {
        var bar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 60, ColumnCount = 5, Padding = new Padding(0, 4, 0, 12) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 174));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152));
        bar.Controls.Add(new Label { Text = DisplayPageName(title), Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 17, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 0, 0);
        var configuration = LoadConfiguration();
        var profiles = GetProfiles(configuration);
        var profilePicker = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = SurfaceAlt, ForeColor = TextPrimary, AccessibleName = "Active profile", Margin = new Padding(3, 8, 3, 3) };
        profilePicker.Items.AddRange(profiles.Select(profile => profile.Name).Cast<object>().ToArray());
        profilePicker.SelectedItem = profiles.FirstOrDefault(profile => profile.Name == configuration.ActiveProfile)?.Name ?? profiles[0].Name;
        profilePicker.SelectedIndexChanged += (_, _) =>
        {
            var next = profilePicker.SelectedItem?.ToString();
            if (next is null) return;
            var current = LoadConfiguration();
            var savedProfiles = GetProfiles(current);
            configurationDraft = current with { Profiles = savedProfiles, ActiveProfile = next, Shortcuts = savedProfiles.First(profile => profile.Name == next).Shortcuts.ToArray() };
            configurationDirty = true;
            ShowPage(currentPage);
        };
        bar.Controls.Add(profilePicker, 1, 0);
        var profileWidth = TextRenderer.MeasureText(profilePicker.Text, profilePicker.Font, Size.Empty, TextFormatFlags.NoPadding).Width + 36;
        bar.ColumnStyles[1].Width = Math.Clamp(profileWidth, 84, 200);
        profilePicker.DropDownWidth = Math.Clamp(profiles.Max(profile => TextRenderer.MeasureText(profile.Name, profilePicker.Font).Width) + 30, 120, 400);
        unifiedConnection = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true, AccessibleName = "Device connection" };
        bar.Controls.Add(unifiedConnection, 2, 0);
        var refresh = CompactButton("↻", async () => await ReconnectAsync());
        refresh.AccessibleName = "Refresh device connection";
        refresh.Dock = DockStyle.Fill;
        bar.Controls.Add(refresh, 3, 0);
        unifiedSave = CompactButton("Save", async () => await SaveConfigurationDraftAsync());
        unifiedSave.Dock = DockStyle.Fill;
        unifiedSave.BackColor = Accent;
        unifiedSave.ForeColor = Color.Black;
        bar.Controls.Add(unifiedSave, 4, 0);
        connectedUsbPort = StreamDeck.WindowsAgent.Runtime.UsbSerialPortDiscovery.FindConnectedCh340Ports().FirstOrDefault();
        RefreshUnifiedToolbar();
        return bar;
    }

    private void RefreshUnifiedToolbar()
    {
        if (unifiedFeedback is { IsDisposed: false }) unifiedFeedback.Text = savingConfiguration ? "Saving…" : configurationDirty ? "Unsaved changes" : configurationFeedback;
        var snapshot = state.Snapshot();
        var connected = snapshot.HostStatus == "connected";
        if (unifiedSave is { IsDisposed: false })
        {
            unifiedSave.Text = connected ? "Save & Sync" : "Save";
            unifiedSave.Enabled = configurationDirty && !savingConfiguration;
            if (unifiedSave.Parent is TableLayoutPanel toolbar)
                toolbar.ColumnStyles[4].Width = Math.Max(80, TextRenderer.MeasureText(unifiedSave.Text, unifiedSave.Font, Size.Empty, TextFormatFlags.NoPadding).Width + 24);
        }
        if (unifiedConnection is { IsDisposed: false })
        {
            var remote = snapshot.RemoteAddress == "USB" ? connectedUsbPort ?? "USB" : "Wi-Fi";
            // Serial discovery is expensive. The port is resolved once per page construction.
            unifiedConnection.Text = connected ? $"● Connected · {remote}" : "○ Disconnected";
            unifiedConnection.ForeColor = connected ? (clientLightTheme ? Color.DarkGreen : AppleGreen) : TextMuted;
        }
    }

    private bool ResolveUnsavedChanges()
    {
        if (!configurationDirty) return true;
        using var dialog = new Form { Text = "Unsaved changes", ClientSize = new Size(420, 130), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = Surface, ForeColor = TextPrimary, Font = Font };
        dialog.Controls.Add(new Label { Text = "Save your changes before closing?", Dock = DockStyle.Top, Height = 60, Padding = new Padding(20), AutoSize = false });
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft };
        var cancel = CompactButton("Cancel", () => Task.CompletedTask);
        cancel.DialogResult = DialogResult.Cancel;
        var discard = CompactButton("Discard", () => Task.CompletedTask);
        discard.DialogResult = DialogResult.No;
        var save = CompactButton("Save", () => Task.CompletedTask);
        save.DialogResult = DialogResult.Yes;
        actions.Controls.AddRange([cancel, discard, save]);
        dialog.Controls.Add(actions);
        dialog.CancelButton = cancel;
        var result = dialog.ShowDialog(this);
        if (result == DialogResult.Cancel) return false;
        if (result == DialogResult.Yes)
        {
            // Keep the window open while the async save runs; close on success only.
            _ = SaveThenHideAsync();
            return false;
        }
        configurationDraft = null;
        pendingAppearance = null;
        pendingDesktopAppearance = null;
        configurationDirty = false;
        var desktop = LoadDesktopAppearanceConfiguration();
        var theme = ResolveEffectiveDesktopTheme(desktop.Theme);
        ApplyClientTheme(theme, desktop.Accent, ResolveDesktopWindowBackground(desktop, theme));
        ShowPage(currentPage);
        return true;
    }

    private async Task SaveThenHideAsync()
    {
        if (await SaveConfigurationDraftAsync()) Hide();
    }

    internal async Task<bool> SaveConfigurationDraftAsync()
    {
        if (savingConfiguration) return false;
        AgentConfiguration saved;
        try { saved = configurationStore.LoadOrDefault(); }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Recover the existing configuration before saving. {exception.GetType().Name}", "Configuration unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        var candidate = LoadConfiguration();
        if (pendingAppearance is not null) candidate = candidate with { Appearance = pendingAppearance.EnsureForcedPagesVisible().ToConfiguration() with { BrightnessRevision = Guid.NewGuid().ToString("N") } };
        if (pendingDesktopAppearance is not null) candidate = candidate with { DesktopAppearance = pendingDesktopAppearance.ToConfiguration() };
        candidate = candidate with { Profiles = GetProfiles(candidate) };
        if (!TrySaveConfiguration(candidate, "Configuration saved locally")) return false;
        configurationDraft = candidate;
        if (pendingDesktopAppearance is not null)
        {
            var theme = ResolveEffectiveDesktopTheme(pendingDesktopAppearance.Theme);
            ApplyClientTheme(theme, pendingDesktopAppearance.Accent, ResolveDesktopWindowBackground(pendingDesktopAppearance, theme));
        }
        configurationDirty = false;
        savingConfiguration = true;
        RefreshUnifiedToolbar();
        var synced = false;
        try
        {
            if (saved.Server != candidate.Server) await runtime.ReconnectAsync();
            var shortcutsSent = await runtime.UpdateShortcutsAsync(candidate.Shortcuts ?? []);
            var settingsSent = await runtime.ApplyDeviceSettingsAsync(candidate.Appearance ?? new());
            synced = shortcutsSent && settingsSent;
        }
        catch (Exception exception)
        {
            AgentDiagnosticLog.Write($"Configuration saved locally; sync failed: {exception.GetType().Name}");
        }
        finally
        {
            savingConfiguration = false;
            configurationFeedback = synced ? "Saved · sent to CYD" : "Saved · sync on reconnect";
            RefreshUnifiedToolbar();
        }
        return true;
    }

    private static DeckProfileConfiguration[] GetProfiles(AgentConfiguration configuration)
    {
        var profiles = configuration.Profiles.ToList();
        var active = profiles.FindIndex(profile => profile.Name == configuration.ActiveProfile);
        var current = new DeckProfileConfiguration(configuration.ActiveProfile, configuration.Shortcuts ?? []);
        if (active >= 0) profiles[active] = current; else profiles.Insert(0, current);
        return profiles.ToArray();
    }

    private void ChangeProfile(bool duplicate, bool delete = false)
    {
        var configuration = LoadConfiguration();
        var profiles = GetProfiles(configuration).ToList();
        if (delete)
        {
            if (profiles.Count <= 1) return;
            profiles.RemoveAll(profile => profile.Name == configuration.ActiveProfile);
            configurationDraft = configuration with { Profiles = profiles, ActiveProfile = profiles[0].Name, Shortcuts = profiles[0].Shortcuts };
        }
        else
        {
            if (profiles.Count >= 32) return;
            var number = 1;
            while (profiles.Any(profile => profile.Name.Equals($"Profile {number}", StringComparison.OrdinalIgnoreCase))) ++number;
            var name = $"Profile {number}";
            var shortcuts = duplicate ? configuration.Shortcuts ?? [] : Array.Empty<ShortcutDefinition>();
            profiles.Add(new DeckProfileConfiguration(name, shortcuts));
            configurationDraft = configuration with { Profiles = profiles, ActiveProfile = name, Shortcuts = shortcuts };
        }
        configurationDirty = true;
        ShowPage("Shortcuts");
    }

    private static Button CompactButton(string text, Func<Task> action)
    {
        var button = new Button { Text = text, AutoSize = false, Width = 108, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = SurfaceAlt, ForeColor = TextPrimary, Margin = new Padding(3), Cursor = Cursors.Hand, AccessibleName = text };
        button.FlatAppearance.BorderColor = Border;
        button.Click += async (_, _) =>
        {
            try { await action(); }
            catch (Exception exception) { AgentDiagnosticLog.Write($"UI action failed: {exception.GetType().Name}"); }
        };
        return button;
    }

    private TableLayoutPanel CompactCard(string title)
    {
        var card = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, Padding = new Padding(14), Margin = new Padding(6), BackColor = Surface };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        card.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 11, FontStyle.Bold), ForeColor = TextPrimary, AutoEllipsis = true }, 0, 0);
        card.Paint += (_, e) => { using var pen = new Pen(Border); e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1); };
        return card;
    }

    private void CompactField(TableLayoutPanel card, string label, Control control, int height = 58)
    {
        var field = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        field.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        field.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        field.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, ForeColor = TextMuted, AutoEllipsis = true }, 0, 0);
        control.Dock = DockStyle.Fill;
        control.AccessibleName = label;
        control.Margin = new Padding(0, 0, 0, 4);
        field.Controls.Add(control, 0, 1);
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        card.Controls.Add(field, 0, card.RowCount++);
    }

    private ComboBox CompactChoice(string[] values, string current, Action<string> update)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = SurfaceAlt, ForeColor = TextPrimary, FlatStyle = FlatStyle.Flat };
        combo.Items.AddRange(values.Cast<object>().ToArray());
        combo.SelectedItem = values.FirstOrDefault(v => v.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? values[0];
        combo.SelectedIndexChanged += (_, _) => { update((string)combo.SelectedItem!); QueueAppearanceSave(); };
        return combo;
    }

    private Control CompactSegments(string[] values, string current, Action<string> update)
    {
        var segments = new TableLayoutPanel { ColumnCount = values.Length, RowCount = 1, Margin = Padding.Empty };
        foreach (var value in values)
        {
            segments.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / values.Length));
            var option = new RadioButton { Appearance = Appearance.Button, Text = value, Checked = value.Equals(current, StringComparison.OrdinalIgnoreCase), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, FlatStyle = FlatStyle.Flat, ForeColor = TextPrimary, BackColor = SurfaceAlt, Margin = new Padding(0, 0, 2, 0), AccessibleName = value };
            option.FlatAppearance.BorderColor = Border;
            option.FlatAppearance.CheckedBackColor = clientLightTheme ? Color.FromArgb(210, 226, 246) : Color.FromArgb(42, 57, 76);
            option.CheckedChanged += (_, _) => { if (option.Checked) { update(value); QueueAppearanceSave(); } };
            segments.Controls.Add(option);
        }
        return segments;
    }

    private sealed class CompactToggle : CheckBox
    {
        public CompactToggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var scale = DeviceDpi / 96F;
            var track = new Rectangle(1, Math.Max(2, (Height - (int)(22 * scale)) / 2), (int)(38 * scale), (int)(22 * scale));
            using var path = CreateRoundedRectPath(track, track.Height / 2);
            using var fill = new SolidBrush(Checked ? Accent : Border);
            e.Graphics.FillPath(fill, path);
            var diameter = track.Height - 6;
            using var knob = new SolidBrush(Color.White);
            e.Graphics.FillEllipse(knob, Checked ? track.Right - diameter - 3 : track.Left + 3, track.Top + 3, diameter, diameter);
            TextRenderer.DrawText(e.Graphics, $"{Text} · {(Checked ? "On" : "Off")}", Font, new Rectangle(track.Right + 8, 0, Math.Max(0, Width - track.Right - 8), Height), ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, ForeColor, BackColor);
        }
    }

    private Control CompactColor(string current, Action<string> update)
    {
        var row = new FlowLayoutPanel { WrapContents = false, Margin = Padding.Empty };
        var selectedColor = current;
        foreach (var color in new[] { "#7EAFE3", "#0A84FF", "#30D158", "#BF5AF2", "#FF9F0A", "#101216" })
        {
            var swatch = CompactButton("", () => { selectedColor = color; update(color); QueueAppearanceSave(); return Task.CompletedTask; });
            swatch.AccessibleName = $"Color {color}";
            swatch.Size = new Size(25, 25);
            swatch.BackColor = ColorTranslator.FromHtml(color);
            row.Controls.Add(swatch);
        }
        var custom = CompactButton("…", () =>
        {
            using var picker = new ColorDialog { Color = ColorTranslator.FromHtml(selectedColor), FullOpen = true };
            if (picker.ShowDialog(this) == DialogResult.OK) { selectedColor = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}"; update(selectedColor); QueueAppearanceSave(); }
            return Task.CompletedTask;
        });
        custom.AccessibleName = "Custom color";
        custom.Size = new Size(28, 25);
        row.Controls.Add(custom);
        return row;
    }

    private Control CreateCompactSettingsPage()
    {
        var page = CreatePage("Appearance");
        pendingDesktopAppearance ??= LoadDesktopAppearanceConfiguration();
        pendingAppearance ??= LoadAppearanceConfiguration();
        var desktop = pendingDesktopAppearance;
        var display = pendingAppearance;
        var windows = CompactCard("Windows appearance");
        CompactField(windows, "Theme", CompactSegments(["System", "Dark", "Light"], desktop.Theme, value => desktop.Theme = value.ToLowerInvariant()));
        CompactField(windows, "Window surface", CompactChoice(["Minimal", "Acrylic", "Gradient"], desktop.WindowStyle, value => desktop.WindowStyle = value.ToLowerInvariant()));
        CompactField(windows, "Accent", CompactColor(desktop.Accent, value => desktop.Accent = value));
        CompactField(windows, "Configuration", CompactButton("Restore defaults", () => { RestoreDefaultAppearance(); return Task.CompletedTask; }));
        CompactField(windows, "Backup", CompactButton("Export config", ExportConfigurationAsync));
        CompactField(windows, "Restore backup", CompactButton("Import config", ImportConfigurationAsync));
        var cyd = CompactCard("CYD display");
        CompactField(cyd, "Theme", CompactSegments(["Dark", "Light"], display.Theme, value => display.Theme = value.ToLowerInvariant()));
        CompactField(cyd, "Accent", CompactColor(display.Accent, value => display.Accent = value));
        CompactField(cyd, "Background", CompactColor(display.Background, value => display.Background = value));
        CompactField(cyd, "Tiles", CompactChoice(["control-center", "accent-tint", "oled-contrast"], display.TileColorScheme, value => display.TileColorScheme = value));
        CompactField(cyd, "Corners", CompactChoice(["Rounded", "Flat", "Pill"], display.ButtonStyle, value => display.ButtonStyle = value.ToLowerInvariant()));
        CompactField(cyd, "Tile layout", CompactChoice(["auto", "hero-1", "split-2", "quad-4", "grid-6"], display.ShortcutsLayout, value => display.ShortcutsLayout = value));
        CompactField(cyd, "Clock style", CompactChoice(["apple-standby", "watchos-modular", "minimal-oled"], display.StandbyStyle, value => display.StandbyStyle = value));
        var hardware = CompactCard("Hardware & power");
        var server = LoadConfiguration().Server ?? new();
        var ports = new[] { "Automatic" }.Concat(System.IO.Ports.SerialPort.GetPortNames()).Concat(server.UsbPort is null ? [] : new[] { server.UsbPort }).Distinct().ToArray();
        CompactField(hardware, "USB port", CompactChoice(ports, server.UsbPort ?? "Automatic", value =>
        {
            server = server with { UsbPort = value == "Automatic" ? null : value };
            configurationDraft = LoadConfiguration() with { Server = server };
        }));
        var autoConnect = new CompactToggle { Text = "USB", Checked = server.UsbAutoConnect, ForeColor = TextPrimary };
        autoConnect.CheckedChanged += (_, _) => { server = server with { UsbAutoConnect = autoConnect.Checked }; configurationDraft = LoadConfiguration() with { Server = server }; QueueAppearanceSave(); };
        CompactField(hardware, "Connection", autoConnect);
        void Number(string label, int current, int minimum, int maximum, Action<int> update)
        {
            var number = new NumericUpDown { Minimum = minimum, Maximum = maximum, Value = Math.Clamp(current, minimum, maximum), BackColor = SurfaceAlt, ForeColor = TextPrimary };
            number.ValueChanged += (_, _) => { update((int)number.Value); QueueAppearanceSave(); };
            CompactField(hardware, label, number);
        }
        var brightnessRow = new TableLayoutPanel { ColumnCount = 2, RowCount = 1 };
        brightnessRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        brightnessRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        var brightness = new TrackBar { Minimum = 10, Maximum = 100, Value = display.Brightness, TickStyle = TickStyle.None, Dock = DockStyle.Fill };
        var brightnessValue = new Label { Text = $"{display.Brightness}%", ForeColor = TextPrimary, Dock = DockStyle.Fill };
        brightness.ValueChanged += (_, _) => { display.Brightness = brightness.Value; brightnessValue.Text = $"{brightness.Value}%"; QueueAppearanceSave(); };
        brightnessRow.Controls.Add(brightness, 0, 0); brightnessRow.Controls.Add(brightnessValue, 1, 0);
        CompactField(hardware, "Brightness", brightnessRow);
        Number("Dim after (seconds, 0 = never)", display.DimAfterSec, 0, 86400, value => display.DimAfterSec = value);
        Number("Dim brightness (%)", display.DimBrightness, 1, 100, value => display.DimBrightness = value);
        Number("Screen off (seconds, 0 = never)", display.ScreenOffAfterSec, 0, 86400, value => display.ScreenOffAfterSec = value);
        var wake = new CompactToggle { Text = "Touch", Checked = display.WakeOnTouch, ForeColor = TextPrimary };
        wake.CheckedChanged += (_, _) => { display.WakeOnTouch = wake.Checked; QueueAppearanceSave(); };
        CompactField(hardware, "Wake", wake);
        CompactField(hardware, "While PC is connected", new Label { Text = "Display stays awake", ForeColor = TextMuted });
        foreach (var card in new[] { windows, cyd, hardware }) { card.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); card.RowCount++; }
        var host = new Panel { Dock = DockStyle.Fill };
        settingsCards.Clear();
        settingsCards.AddRange([windows, cyd, hardware]);
        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        for (var i = 0; i < 3; ++i) { columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3)); columns.Controls.Add(settingsCards[i], i, 0); }
        var tabs = new TabControl { Dock = DockStyle.Fill, Visible = false };
        foreach (var name in new[] { "Windows", "CYD", "Hardware" }) tabs.TabPages.Add(name);
        host.Controls.Add(columns);
        host.Controls.Add(tabs);
        void Adapt()
        {
            var compact = host.ClientSize.Width < 850 * DeviceDpi / 96 || host.ClientSize.Height < 500 * DeviceDpi / 96;
            columns.Visible = !compact;
            tabs.Visible = compact;
            for (var i = 0; i < settingsCards.Count; ++i)
            {
                var target = compact ? (Control)tabs.TabPages[i] : columns;
                if (settingsCards[i].Parent == target) continue;
                if (compact) target.Controls.Add(settingsCards[i]); else columns.Controls.Add(settingsCards[i], i, 0);
            }
            if (compact)
                foreach (var card in settingsCards.OfType<TableLayoutPanel>()) PaginateSettingsCard(card);
            else
                foreach (var card in settingsCards.OfType<TableLayoutPanel>()) UnpaginateSettingsCard(card);
        }
        void ScheduleAdapt()
        {
            if (!host.IsHandleCreated || host.IsDisposed) return;
            host.BeginInvoke((Action)(() => { if (!host.IsDisposed) Adapt(); }));
        }
        host.SizeChanged += (_, _) => ScheduleAdapt();
        host.HandleCreated += (_, _) => ScheduleAdapt();
        AddPageBodyControl(page, host);
        return page;
    }

    private void PaginateSettingsCard(TableLayoutPanel card)
    {
        if (settingsCardPages.ContainsKey(card)) return;
        var fields = card.Controls.Cast<Control>().Where(control => card.GetRow(control) > 0)
            .OrderBy(card.GetRow).Select(control => (Control: control, Height: card.RowStyles[card.GetRow(control)].Height)).ToArray();
        if (fields.Length <= 4) return;
        var tabs = new TabControl { Dock = DockStyle.Fill, Tag = fields };
        for (var start = 0; start < fields.Length; start += 4)
        {
            var tab = new TabPage(start == 0 ? "General" : "More") { BackColor = Surface };
            var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = Math.Min(4, fields.Length - start) + 1, Padding = new Padding(8) };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var row = 0; row < Math.Min(4, fields.Length - start); ++row)
            {
                var field = fields[start + row];
                form.RowStyles.Add(new RowStyle(SizeType.Absolute, field.Height));
                form.Controls.Add(field.Control, 0, row);
            }
            form.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tab.Controls.Add(form);
            tabs.TabPages.Add(tab);
        }
        card.RowCount = 2;
        while (card.RowStyles.Count > 1) card.RowStyles.RemoveAt(1);
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(tabs, 0, 1);
        settingsCardPages[card] = tabs;
        tabs.Disposed += (_, _) => settingsCardPages.Remove(card);
    }

    private void UnpaginateSettingsCard(TableLayoutPanel card)
    {
        if (!settingsCardPages.TryGetValue(card, out var tabs)) return;
        var fields = ((Control Control, float Height)[])tabs.Tag!;
        card.SuspendLayout();
        card.Controls.Remove(tabs);
        card.RowStyles.Clear();
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        card.RowCount = fields.Length + 2;
        for (var row = 0; row < fields.Length; ++row)
        {
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, fields[row].Height));
            card.Controls.Add(fields[row].Control, 0, row + 1);
        }
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tabs.Dispose();
        card.ResumeLayout(true);
    }

    private Control CreateCompactOverviewPage()
    {
        var page = CreatePage("Overview");
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 188));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.Controls.Add(CreateAppleMediaControlWidget(), 0, 0);
        grid.Controls.Add(CreateAppleSystemMonitorWidget(), 0, 1);
        var summary = CompactCard("Your deck");
        CompactField(summary, $"{LoadConfiguration().Shortcuts?.Count ?? 0} shortcuts · 6 device positions", new Label { Text = "Build your deck in Deck Builder", ForeColor = TextMuted });
        grid.Controls.Add(summary, 0, 2);
        AddPageBodyControl(page, grid);
        return page;
    }

    private Control CreateCompactDevicePage()
    {
        deviceTelemetryVisible = state.Snapshot().HostStatus == "connected" && state.Snapshot().DeviceInfo is not null;
        var page = CreatePage("Device");
        var card = CompactCard("Device telemetry");
        var values = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BackColor = Surface, ForeColor = TextPrimary, BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.None };
        values.Columns.Add("Property", 200);
        values.Columns.Add("Value", 500);
        void Refresh()
        {
            if (values.IsDisposed) return;
            values.BeginUpdate();
            values.Items.Clear();
            var snapshot = state.Snapshot();
            var redundant = new HashSet<string> { "Connection headline", "Connection detail", "Status", "Agent service", "Remote IP", "Remote endpoint", "Agent port", "Dashboard port", "Last seen" };
            foreach (var (label, value) in CreateLiveInfoValues("Device", snapshot))
            {
                if (redundant.Contains(label) || (label == "Telemetry" && snapshot.DeviceInfo is not null && snapshot.HostStatus == "connected")) continue;
                values.Items.Add(new ListViewItem([label, value]));
            }
            values.EndUpdate();
        }
        refreshOverviewWidgets = Refresh;
        Refresh();
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(values, 0, card.RowCount++);
        AddPageBodyControl(page, card);
        AddPageFooter(page, CreateDeviceActionsBar(!string.IsNullOrWhiteSpace(state.Snapshot().DeviceId), state.Snapshot().HostStatus == "connected", ("Restart", RestartDeviceAsync), ("Factory reset", FactoryResetDeviceAsync), ("Forget device", ForgetDeviceAsync)));
        return page;
    }

    private Control CreateCompactDeckPage()
    {
        var page = CreatePage("Shortcuts");
        var load = LoadShortcutConfigurationState(() => configurationDraft ?? configurationStore.LoadOrDefault());
        shortcutsConfigurationLoadFailed = !load.CanWriteShortcuts;
        var shortcuts = load.Configuration.Shortcuts?.ToList() ?? [];
        var pendingImports = runtime.GetPendingShortcutImports(shortcuts.Select(shortcut => shortcut.Id).ToHashSet(StringComparer.OrdinalIgnoreCase));
        shortcutImportSignature = CreatePendingShortcutImportSignature(pendingImports);
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        var canvas = CompactCard("Device preview · 6 positions");
        var list = CreateThemedListView();
        list.AccessibleName = "Shortcut catalog";
        list.MultiSelect = false;
        list.Columns.Add("Position", 74);
        list.Columns.Add("Label", 180);
        list.Columns.Add("Action", 140);
        foreach (var (item, index) in shortcuts.Select((item, index) => (item, index))) list.Items.Add(new ListViewItem([index < 6 ? $"CYD {index + 1}" : "Catalog", item.Name, item.Action.Type]));
        list.Dock = DockStyle.Fill;
        list.SizeChanged += (_, _) => { var available = Math.Max(200, list.ClientSize.Width - 26); list.Columns[0].Width = 74; list.Columns[1].Width = (available - 74) * 2 / 3; list.Columns[2].Width = (available - 74) / 3; };
        var displayAppearance = pendingAppearance ?? LoadAppearanceConfiguration();
        var deviceBackground = ResolveCydPreviewBackground(displayAppearance.Theme, displayAppearance.Background);
        var tileGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Padding = new Padding(4), BackColor = Color.FromArgb(16, 18, 22) };
        for (var i = 0; i < 3; ++i) tileGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
        for (var i = 0; i < 2; ++i) tileGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var tiles = new Button[6];
        var selected = -1;
        void Reorder(int from, int to)
        {
            if (from < 0 || to < 0 || from >= shortcuts.Count || to >= shortcuts.Count) return;
            MoveShortcutInMemory(shortcuts, from, to);
            configurationDraft = LoadConfiguration() with { Shortcuts = shortcuts.ToArray() };
            configurationDirty = true;
            ShowPage("Shortcuts");
            SelectShortcutAt(to);
        }
        for (var i = 0; i < 6; ++i)
        {
            var index = i;
            var assigned = i < shortcuts.Count;
            var tile = CompactButton(assigned ? shortcuts[i].Name : "Empty", () => { if (index < list.Items.Count) { list.SelectedIndices.Clear(); list.Items[index].Selected = true; } return Task.CompletedTask; });
            tile.Dock = DockStyle.Fill;
            tile.Tag = tile.Text;
            tile.Text = string.Empty;
            tile.FlatAppearance.BorderSize = 0;
            tile.TextAlign = ContentAlignment.BottomCenter;
            tile.Padding = new Padding(2, 0, 2, 10);
            tile.ForeColor = Color.FromArgb(244, 246, 248);
            tile.BackColor = Color.FromArgb(30, 32, 40);
            tile.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var background = new SolidBrush(deviceBackground);
                e.Graphics.FillRectangle(background, tile.ClientRectangle);
                using var path = CreateRoundedRectPath(new Rectangle(0, 0, tile.Width - 1, tile.Height - 1), 12);
                var tileColor = index < shortcuts.Count && shortcuts[index].Color is { } configured ? ColorTranslator.FromHtml(configured) : ResolveCydTileBackground(displayAppearance.Theme, displayAppearance.TileColorScheme, ParseHexColor(displayAppearance.Accent, DeviceDefaultAccent));
                using var fill = new SolidBrush(tileColor);
                e.Graphics.FillPath(fill, path);
                if (tile.Focused) { using var pen = new Pen(Accent, 2); e.Graphics.DrawPath(pen, path); }
                if (index < shortcuts.Count && shortcuts[index].DisplayMode != ShortcutDisplayModes.Label) ShortcutIconCatalog.Draw(e.Graphics, shortcuts[index].Icon, shortcuts[index].IconData, new Rectangle(tile.Width / 2 - 12, tile.Height / 2 - (shortcuts[index].DisplayMode == ShortcutDisplayModes.Icon ? 12 : 23), 24, 24), tileColor.GetBrightness() > .57f ? Color.FromArgb(16, 18, 22) : Color.FromArgb(244, 246, 248));
                if (index >= shortcuts.Count || shortcuts[index].DisplayMode != ShortcutDisplayModes.Icon) TextRenderer.DrawText(e.Graphics, tile.Tag?.ToString(), tile.Font, new Rectangle(4, tile.Height - 29, tile.Width - 8, 23), (tileColor.GetBrightness() > .57f ? Color.FromArgb(16, 18, 22) : Color.FromArgb(244, 246, 248)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            };
            tile.AllowDrop = true;
            Point start = Point.Empty;
            tile.MouseDown += (_, e) => start = e.Location;
            tile.MouseMove += (_, e) => { if (e.Button == MouseButtons.Left && assigned && (Math.Abs(e.X - start.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - start.Y) > SystemInformation.DragSize.Height)) tile.DoDragDrop(index, DragDropEffects.Move); };
            tile.DragEnter += (_, e) => { if (e.Data?.GetDataPresent(typeof(int)) == true) e.Effect = DragDropEffects.Move; };
            tile.DragDrop += (_, e) => { if (e.Data?.GetData(typeof(int)) is int from) Reorder(from, index); };
            tiles[i] = tile;
            tileGrid.Controls.Add(tile, i % 3, i / 3);
        }
        var layout = displayAppearance.ShortcutsLayout;
        if (layout == "auto") layout = shortcuts.Count switch { 1 => "hero-1", 2 => "split-2", 3 or 4 => "quad-4", _ => "grid-6" };
        tileGrid.Controls.Clear(); tileGrid.ColumnStyles.Clear(); tileGrid.RowStyles.Clear();
        var columns = layout == "hero-1" ? 1 : layout == "grid-6" ? 3 : 2;
        var rows = layout is "hero-1" or "split-2" ? 1 : 2;
        tileGrid.ColumnCount = columns; tileGrid.RowCount = rows;
        for (var c = 0; c < columns; ++c) tileGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columns));
        for (var r = 0; r < rows; ++r) tileGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / rows));
        for (var slot = 0; slot < tiles.Length; ++slot) { tiles[slot].Visible = slot < columns * rows; if (tiles[slot].Visible) tileGrid.Controls.Add(tiles[slot], slot % columns, slot / columns); }
        var preview = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 28), BackColor = Color.FromArgb(16, 18, 22) };
        preview.Controls.Add(tileGrid);
        var dock = new Label { Dock = DockStyle.Bottom, Height = 28, Text = DateTime.Now.ToString("HH:mm") + "    Deck    Media    PC     ⚙", TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White };
        preview.Controls.Add(dock);
        var previewHost = new Panel { Dock = DockStyle.Fill };
        preview.Dock = DockStyle.None;
        previewHost.Controls.Add(preview);
        previewHost.SizeChanged += (_, _) => { var width = Math.Min(previewHost.ClientSize.Width, previewHost.ClientSize.Height * 4 / 3); preview.Bounds = new Rectangle((previewHost.ClientSize.Width - width) / 2, 0, width, width * 3 / 4); };
        var profileActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        profileActions.Controls.Add(CompactButton("+ New", () => { ChangeProfile(false); return Task.CompletedTask; }));
        profileActions.Controls.Add(CompactButton("Duplicate", () => { ChangeProfile(true); return Task.CompletedTask; }));
        profileActions.Controls.Add(CompactButton("Delete profile", () => { ChangeProfile(false, delete: true); return Task.CompletedTask; }));
        foreach (Control control in profileActions.Controls) control.Width = 96;
        canvas.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        canvas.Controls.Add(profileActions, 0, canvas.RowCount++);
        canvas.RowStyles.Add(new RowStyle(SizeType.Absolute, 252));
        canvas.Controls.Add(previewHost, 0, canvas.RowCount++);
        canvas.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        canvas.Controls.Add(list, 0, canvas.RowCount++);
        var inspector = CompactCard("Tile Inspector");
        var label = new TextBox { BackColor = SurfaceAlt, ForeColor = TextPrimary };
        var type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = SurfaceAlt, ForeColor = TextPrimary };
        type.Items.AddRange(["hotkey", "url", "launch", "script", "powershell", "predefined"]);
        var target = new TextBox { BackColor = SurfaceAlt, ForeColor = TextPrimary };
        var icon = new Button { BackColor = SurfaceAlt, ForeColor = TextPrimary, Text = "Choose icon…", TextAlign = ContentAlignment.MiddleRight, FlatStyle = FlatStyle.Flat };
        var mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = SurfaceAlt, ForeColor = TextPrimary };
        mode.Items.AddRange(["both", "label", "icon"]);
        CompactField(inspector, "Label", label);
        CompactField(inspector, "Action type", type);
        CompactField(inspector, "Shortcut / URL / Path / Adapter", target);
        var visuals = new TableLayoutPanel { ColumnCount = 2, RowCount = 1 };
        visuals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); visuals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        icon.Dock = DockStyle.Fill; visuals.Controls.Add(icon, 0, 0);
        var tileColorButton = CompactButton("Color…", () =>
        {
            if (selected < 0 || selected >= shortcuts.Count) return Task.CompletedTask;
            using var picker = new ColorDialog { Color = ParseHexColor(shortcuts[selected].Color ?? displayAppearance.Accent, DeviceDefaultAccent) };
            if (picker.ShowDialog(this) == DialogResult.OK)
            {
                shortcuts[selected] = shortcuts[selected] with { Color = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}" };
                configurationDraft = LoadConfiguration() with { Shortcuts = shortcuts.ToArray() }; configurationDirty = true;
                if (selected < tiles.Length) tiles[selected].Invalidate(); RefreshUnifiedToolbar();
            }
            return Task.CompletedTask;
        });
        tileColorButton.Dock = DockStyle.Fill; visuals.Controls.Add(tileColorButton, 1, 0);
        CompactField(inspector, "Icon / Color", visuals);
        var validation = new Label { ForeColor = TextMuted, AutoEllipsis = true };
        icon.AccessibleName = "Choose shortcut icon";
        icon.Paint += (_, e) => {
            if (selected >= 0 && selected < shortcuts.Count) ShortcutIconCatalog.Draw(e.Graphics, shortcuts[selected].Icon, shortcuts[selected].IconData, new Rectangle(8, (icon.Height - 24) / 2, 24, 24), TextPrimary);
        };
        icon.Click += (_, _) => {
            if (selected < 0 || selected >= shortcuts.Count) return;
            var original = shortcuts[selected];
            using var picker = new ShortcutIconPicker(original.Icon, original.IconData, original.IconUrl);
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            shortcuts[selected] = original with { Icon = picker.IconKey, IconData = picker.IconData, IconUrl = picker.IconUrl };
            configurationDraft = LoadConfiguration() with { Shortcuts = shortcuts.ToArray() }; configurationDirty = true;
            icon.Invalidate(); foreach (var tile in tiles) tile.Invalidate(); validation.Text = "Unsaved changes"; RefreshUnifiedToolbar();
        };
        CompactField(inspector, "Display", mode);
        CompactField(inspector, "Status", validation);
        bool loading = false;
        void Edit()
        {
            if (loading || selected < 0 || selected >= shortcuts.Count) return;
            var original = shortcuts[selected];
            var value = target.Text.Trim();
            var action = type.SelectedItem?.ToString() switch
            {
                "hotkey" => new ShortcutActionDefinition { Type = "hotkey", Keys = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) },
                "url" => new ShortcutActionDefinition { Type = "url", Url = value },
                "launch" => new ShortcutActionDefinition { Type = "launch", ExecutablePath = value, Arguments = original.Action.Arguments },
                "powershell" => new ShortcutActionDefinition { Type = "powershell", ScriptPath = value, Arguments = original.Action.Arguments },
                "script" => new ShortcutActionDefinition { Type = "script", ScriptPath = value, Arguments = original.Action.Arguments },
                _ => new ShortcutActionDefinition { Type = "predefined", AdapterKey = value }
            };
            var candidate = original with { Name = label.Text, DisplayMode = mode.SelectedItem?.ToString() ?? "both", Action = action };
            shortcuts[selected] = candidate;
            configurationDraft = LoadConfiguration() with { Shortcuts = shortcuts.ToArray() };
            configurationDirty = true;
            validation.Text = ShortcutEditorDialog.TryValidateShortcutDraft(candidate, out var error) ? "Unsaved changes" : error;
            list.Items[selected].SubItems[1].Text = candidate.Name;
            if (selected < tiles.Length) { tiles[selected].Tag = candidate.Name; tiles[selected].Invalidate(); }
            RefreshUnifiedToolbar();
        }
        label.TextChanged += (_, _) => Edit(); target.TextChanged += (_, _) => Edit(); type.SelectedIndexChanged += (_, _) => Edit(); mode.SelectedIndexChanged += (_, _) => Edit();
        list.SelectedIndexChanged += (_, _) =>
        {
            selected = GetSelectedShortcutIndex(list);
            if (selected < 0 || selected >= shortcuts.Count) return;
            loading = true;
            var item = shortcuts[selected];
            label.Text = item.Name; icon.Invalidate(); type.SelectedItem = item.Action.Type; mode.SelectedItem = item.DisplayMode;
            target.Text = item.Action.Url ?? item.Action.ExecutablePath ?? item.Action.ScriptPath ?? item.Action.AdapterKey ?? string.Join("+", item.Action.Keys);
            loading = false;
            foreach (var tile in tiles) tile.Invalidate();
            validation.Text = selected < 6 ? $"Device position {selected + 1}" : "Catalog only · move to positions 1–6 to assign";
        };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        actions.Controls.Add(CompactButton("Add", async () => { using var dialog = new ShortcutEditorDialog(); if (dialog.ShowDialog(this) == DialogResult.OK) { shortcuts.Add(dialog.Shortcut); await SaveShortcutsAsync(shortcuts); ShowPage("Shortcuts"); } }));
        actions.Controls.Add(CompactButton("Delete", async () => { if (selected >= 0) { shortcuts.RemoveAt(selected); await SaveShortcutsAsync(shortcuts); ShowPage("Shortcuts"); } }));
        actions.Controls.Add(CompactButton("Advanced…", async () => await EditShortcutAsync(list, shortcuts)));
        actions.Controls.Add(CompactButton("Move up", () => { Reorder(selected, selected - 1); return Task.CompletedTask; }));
        actions.Controls.Add(CompactButton("Move down", () => { Reorder(selected, selected + 1); return Task.CompletedTask; }));
        inspector.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        if (pendingImports.Count > 0)
            actions.Controls.Add(CompactButton($"Import ({pendingImports.Count})", async () =>
            {
                using var dialog = new ShortcutEditorDialog(CreateShortcutDraftFromPendingImport(pendingImports[0]));
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                shortcuts.Add(dialog.Shortcut);
                await SaveShortcutsAsync(shortcuts);
                ShowPage("Shortcuts");
            }));
        inspector.Controls.Add(actions, 0, inspector.RowCount++);
        canvas.Enabled = inspector.Enabled = load.CanWriteShortcuts;
        split.Controls.Add(canvas, 0, 0); split.Controls.Add(inspector, 1, 0);
        AddPageBodyControl(page, split);
        if (list.Items.Count > 0) list.Items[0].Selected = true;
        return page;
    }
}
