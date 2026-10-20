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
    private DarkComboBox? unifiedProfilePicker;
    private bool updatingProfilePicker;
    private string configurationFeedback = "Ready";
    private string? lastActionError;
    private static string T(string key) => UiText.Get("Shell." + key);
    private static string F(string key, params object?[] values) => UiText.Format("Shell." + key, values);

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        MinimumSize = new Size(MinimumWindowWidth, MinimumWindowHeight);
    }

    private static string DisplayPageName(string page) => page switch
    {
        "Shortcuts" => T("NavDeck"),
        "Appearance" => T("NavSettings"),
        "Media" => T("NavMedia"),
        "Monitor" => T("NavMonitor"),
        "Pairing" or "Device" => T("NavDevice"),
        "Updates" => T("NavUpdates"),
        "Logs" => T("NavLogs"),
        _ => page
    };

    private Control CreateUnifiedToolbar(string title) => CreateDesktopToolbar(title);
    private void RefreshUnifiedToolbar() => RefreshDesktopToolbar();

    private bool ResolveUnsavedChanges(Action? afterSave = null)
    {
        if (!configurationDirty) return true;
        using var dialog = new Form { Text = T("UnsavedTitle"), Name = "UnsavedChangesDialog", ClientSize = new Size(480, 160), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = Surface, ForeColor = TextPrimary, Font = Font };
        dialog.Controls.Add(new Label { Text = T("UnsavedQuestion"), Dock = DockStyle.Top, Height = 72, Padding = new Padding(20), AutoSize = false });
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft };
        var cancel = CompactButton(T("Cancel"), () => Task.CompletedTask);
        cancel.DialogResult = DialogResult.Cancel;
        var discard = CompactButton(T("Discard"), () => Task.CompletedTask);
        discard.DialogResult = DialogResult.No;
        var save = CompactButton(T("Save"), () => Task.CompletedTask);
        save.DialogResult = DialogResult.Yes;
        actions.Controls.AddRange([cancel, discard, save]);
        dialog.Controls.Add(actions);
        dialog.CancelButton = cancel;
        var result = dialog.ShowDialog(this);
        if (result == DialogResult.Cancel) return false;
        if (result == DialogResult.Yes)
        {
            // Keep the window open while the async save runs; close on success only.
            _ = SaveThenContinueAsync(afterSave);
            return false;
        }
        configurationDraft = null;
        pendingAppearance = null;
        pendingDesktopAppearance = null;
        configurationDirty = false;
        var desktop = LoadDesktopAppearanceConfiguration();
        var theme = ResolveEffectiveDesktopTheme(desktop.Theme);
        ApplyClientTheme(theme, desktop.Accent, ResolveDesktopWindowBackground(desktop, theme));
        InvalidatePageCache();
        ShowPage(currentPage);
        return true;
    }

    private async Task SaveThenContinueAsync(Action? afterSave)
    {
        if (!await SaveConfigurationDraftAsync() || IsDisposed || Disposing) return;
        if (afterSave is null) Hide();
        else afterSave();
    }

    private async Task RetrySavedConfigurationAsync()
    {
        if (savingConfiguration) return;
        try
        {
            savingConfiguration = true;
            lastActionError = null;
            RefreshUnifiedToolbar();
            await runtime.RetryConfigurationSyncAsync();
        }
        catch (Exception exception)
        {
            configurationFeedback = "Saved · sync failed";
            AgentDiagnosticLog.Write($"Saved configuration retry failed: {exception.GetType().Name}");
        }
        finally { savingConfiguration = false; RefreshUnifiedToolbar(); }
    }

    private void SetUiLanguage(string language)
    {
        UiText.SetLanguage(language);
        foreach (var (page, button) in navButtons) { button.Text = DisplayPageName(page); button.AccessibleName = button.Text; }
        desktopToolsMenu?.Dispose(); desktopToolsMenu = null;
        void Refresh() { if (IsDisposed || Disposing) return; InvalidatePageCache(); ShowPage(currentPage); }
        if (IsHandleCreated) BeginInvoke((Action)Refresh); else Refresh();
    }

    internal async Task<bool> SaveConfigurationDraftAsync()
    {
        if (savingConfiguration) return false;
        AgentConfiguration saved;
        try { saved = configurationStore.LoadOrDefault(); }
        catch (Exception exception)
        {
            AgentDiagnosticLog.Write($"Configuration unavailable: {exception.GetType().Name}");
            if (DialogMessageHookForTests is not null) DialogMessageHookForTests(T("LoadFailedTitle"), T("LoadFailed"));
            else MessageBox.Show(this, T("LoadFailed"), T("LoadFailedTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        var candidate = LoadConfiguration();
        if (pendingAppearance is not null) candidate = candidate with { Appearance = pendingAppearance.EnsureForcedPagesVisible().ToConfiguration() with { BrightnessRevision = Guid.NewGuid().ToString("N") } };
        if (pendingDesktopAppearance is not null) candidate = candidate with { DesktopAppearance = pendingDesktopAppearance.ToConfiguration() };
        candidate = ConfigurationColorNormalizer.Normalize(candidate with { Profiles = GetProfiles(candidate) });
        if (!TrySaveConfiguration(candidate, "Configuration saved locally")) return false;
        configurationDraft = candidate;
        var themeRebuilt = false;
        if (pendingDesktopAppearance is not null)
        {
            var theme = ResolveEffectiveDesktopTheme(pendingDesktopAppearance.Theme);
            var previousLight = clientLightTheme;
            ApplyClientTheme(theme, pendingDesktopAppearance.Accent, ResolveDesktopWindowBackground(pendingDesktopAppearance, theme));
            themeRebuilt = previousLight != clientLightTheme;
        }
        configurationDirty = false;
        lastActionError = null;
        state.MarkConfigurationSaved(candidate.Server?.UsbPort);
        savingConfiguration = true;
        if (themeRebuilt)
        {
            ShowPage(currentPage);
        }
        RefreshUnifiedToolbar();
        var synced = false;
        try
        {
            if (saved.Server != candidate.Server) await runtime.ReconnectAsync();
            synced = await runtime.SyncSavedConfigurationAsync();
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
        var targetLanguage = candidate.DesktopAppearance?.Language ?? "pl"; if (UiText.Language != targetLanguage) SetUiLanguage(targetLanguage);
        return true;
    }

    private static DeckProfileConfiguration[] GetProfiles(AgentConfiguration configuration)
    {
        var profiles = configuration.Profiles.ToList();
        var active = profiles.FindIndex(profile => profile.Name == configuration.ActiveProfile);
        var current = new DeckProfileConfiguration(configuration.ActiveProfile, configuration.Shortcuts ?? [], active >= 0 ? profiles[active].AssignedShortcutIds : null);
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
            profiles.Add(new DeckProfileConfiguration(name, shortcuts, duplicate ? profiles.First(p => p.Name == configuration.ActiveProfile).AssignedShortcutIds : null));
            configurationDraft = configuration with { Profiles = profiles, ActiveProfile = name, Shortcuts = shortcuts };
        }
        configurationDirty = true;
        InvalidatePageCache();
        ShowPage("Shortcuts");
    }

    private static RoundedButton CompactButton(string text, Func<Task> action)
    {
        var button = new RoundedButton { Text = text, UseMnemonic = false, AutoSize = false, Width = 132, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = SurfaceAlt, ForeColor = TextPrimary, Margin = new Padding(UiTokens.Space.Xs), Cursor = Cursors.Hand, AccessibleName = text };
        button.FlatAppearance.BorderColor = Border;
        button.Click += async (_, _) =>
        {
            var window = button.FindForm() as SettingsWindow;
            if (window is not null) window.lastActionError = null;
            try { await action(); }
            catch (Exception exception) {
                AgentDiagnosticLog.Write($"UI action failed: {exception.GetType().Name}");
                if (window is { IsDisposed: false }) { window.lastActionError = T("OperationFailed"); window.RefreshUnifiedToolbar(); }
            }
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
        field.RowStyles.Add(new RowStyle(SizeType.Absolute, string.IsNullOrEmpty(label) ? 0 : 22));
        field.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        field.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, ForeColor = TextMuted, AutoEllipsis = true }, 0, 0);
        control.Dock = DockStyle.Fill;
        if (!string.IsNullOrEmpty(label)) control.AccessibleName = label;
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
            if (ModernColorPickerDialog.ShowColorPicker(this, selectedColor, out var picked))
            {
                selectedColor = picked;
                update(selectedColor);
                QueueAppearanceSave();
            }
            return Task.CompletedTask;
        });
        custom.AccessibleName = "Custom color";
        custom.Size = new Size(28, 25);
        row.Controls.Add(custom);
        return row;
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

}
