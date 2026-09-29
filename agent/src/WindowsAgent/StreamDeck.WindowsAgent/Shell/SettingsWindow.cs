using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection;
using StreamDeck.Host;
using StreamDeck.Configuration;
using StreamDeck.Protocol;
using StreamDeck.Shortcuts;
using StreamDeck.WindowsAgent.Runtime;
using StreamDeck.WindowsAgent.Updates;
using ShortcutDefinition = StreamDeck.Shortcuts.ShortcutDefinition;

namespace StreamDeck.WindowsAgent.Shell;

public sealed class SettingsWindow : Form
{
    private static readonly Color DarkBackground = Color.FromArgb(13, 15, 20);
    private static readonly Color LightBackground = Color.FromArgb(246, 247, 249);
    private static readonly Color DarkSurface = Color.FromArgb(25, 28, 35);
    private static readonly Color LightSurface = Color.FromArgb(255, 255, 255);
    private static readonly Color DarkSurfaceAlt = Color.FromArgb(39, 43, 52);
    private static readonly Color LightSurfaceAlt = Color.FromArgb(236, 238, 242);
    private static readonly Color DarkBorder = Color.FromArgb(55, 61, 73);
    private static readonly Color LightBorder = Color.FromArgb(210, 214, 220);
    private static readonly Color DarkTextPrimary = Color.FromArgb(244, 246, 248);
    private static readonly Color LightTextPrimary = Color.FromArgb(29, 32, 37);
    private static readonly Color DarkTextMuted = Color.FromArgb(156, 164, 176);
    private static readonly Color LightTextMuted = Color.FromArgb(93, 101, 112);
    private static readonly Color Accent = Color.FromArgb(212, 59, 72);
    private static readonly Padding WindowContentPadding = new(34, 30, 34, 30);
    private const int NavigationWidth = 260;
    private const int NavigationButtonWidth = 204;
    private const int NavigationButtonHeight = 42;
    private const int PageTitleHeight = 72;
    private const int ActionButtonHeight = 36;
    private const int ActionButtonMinimumWidth = 128;
    private const int ActionButtonHorizontalMargin = 10;
    private const int ActionButtonVerticalMargin = 10;
    private const int AppearanceMinimumSliderTrackWidth = 150;
    private const int AppearanceMaximumSliderTrackWidth = 300;
    private const int ColorSwatchSize = 26;
    private const int ColorPickerRowHeight = 40;
    private const int ListVerticalScrollbarAllowance = 24;
    private const string PageBodyTag = "settings-page-body";
    private const string PageFooterTag = "settings-page-footer";
    private const string DeviceColorPreviewTag = "device-color-preview";
    private const string DeviceColorPickerTag = "device-color-picker";
    internal const string ShortcutsSourceOfTruthText = "Agent-side shortcut catalog. Firmware does not preload shortcuts; the device displays up to the first 6 shortcuts.";
    internal const string EmptyShortcutsText = "No shortcuts configured on this PC. Click Add to create the first Agent-side shortcut; there is no device preload to import.";
    internal const string PendingShortcutImportsText = "Device-reported shortcuts needing configuration. These drafts are inactive and are not executable or synced until you configure and save a valid Agent-side action.";
    internal const string DeviceColorPreviewTagForTests = DeviceColorPreviewTag;
    internal const string DeviceColorPickerTagForTests = DeviceColorPickerTag;
    private static bool clientLightTheme;
    private static Color Background => clientLightTheme ? LightBackground : DarkBackground;
    private static Color Surface => clientLightTheme ? LightSurface : DarkSurface;
    private static Color SurfaceAlt => clientLightTheme ? LightSurfaceAlt : DarkSurfaceAlt;
    private static Color TextPrimary => clientLightTheme ? LightTextPrimary : DarkTextPrimary;
    private static Color TextMuted => clientLightTheme ? LightTextMuted : DarkTextMuted;
    private static Color Border => clientLightTheme ? LightBorder : DarkBorder;
    private static int MinimumSettingsClientWidth => 900 - NavigationWidth - WindowContentPadding.Horizontal;

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? pszSubAppName, string? pszSubIdList);

    private readonly AgentState state;
    private readonly WindowsAgentRuntime runtime;
    private readonly LocalDashboardServer dashboard;
    private readonly WindowsStartupService startup;
    private readonly IPairingApprovalService pairing;
    private readonly AgentConfigurationStore configurationStore;
    private readonly Dictionary<string, Button> navButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Label> infoValueLabels = new(StringComparer.Ordinal);
    private readonly Panel contentPanel;
    private readonly System.Windows.Forms.Timer appearanceSaveTimer = new() { Interval = 300 };
    private readonly System.Windows.Forms.Timer shortcutImportRefreshTimer = new() { Interval = 1000 };
    private string currentPage = "Overview";
    private bool allowClose;
    private AppearanceState? pendingAppearance;
    private Label? appearanceStatusLabel;
    private bool shortcutsConfigurationLoadFailed;
    private string shortcutImportSignature = string.Empty;
    private int liveRefreshQueued;

    public SettingsWindow(
        AgentState state,
        WindowsAgentRuntime runtime,
        LocalDashboardServer dashboard,
        WindowsStartupService startup,
        IPairingApprovalService pairing,
        Icon shellIcon)
    {
        this.state = state;
        this.runtime = runtime;
        this.dashboard = dashboard;
        this.startup = startup;
        this.pairing = pairing;
        configurationStore = AgentConfigurationStoreFactory.CreateForCurrentUser();
        appearanceSaveTimer.Tick += async (_, _) => await FlushAppearanceChangesAsync().ConfigureAwait(true);
        shortcutImportRefreshTimer.Tick += (_, _) => RefreshShortcutImportsIfChanged();
        var initialAppearance = LoadAppearanceConfiguration();
        ApplyClientTheme(initialAppearance.Theme);
        contentPanel = CreateThemedContentPanel(initialAppearance.Theme);

        Text = "PC Dock";
        Icon = shellIcon;
        MinimumSize = new Size(900, 620);
        Size = new Size(1040, 720);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

        Controls.Add(CreateLayout());
        state.Changed += HandleStateChanged;
        pairing.PairingChanged += HandlePairingChanged;
        ShowPage(currentPage);
    }

    public void ShowAndActivateWindow()
    {
        if (!Visible)
        {
            Show();
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
        BringToFront();
    }

    public void CloseForExit()
    {
        allowClose = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        state.Changed -= HandleStateChanged;
        pairing.PairingChanged -= HandlePairingChanged;
        appearanceSaveTimer.Stop();
        appearanceSaveTimer.Dispose();
        shortcutImportRefreshTimer.Stop();
        shortcutImportRefreshTimer.Dispose();
        base.OnFormClosing(e);
    }

    private Control CreateLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, NavigationWidth));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(CreateNavigation(), 0, 0);
        root.Controls.Add(contentPanel, 1, 0);
        return root;
    }

    private Control CreateNavigation()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(24, 26, 24, 22) };
        var title = new Label
        {
            Text = "PC Dock",
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font(Font.FontFamily, 19F, FontStyle.Bold),
            ForeColor = TextPrimary
        };
        var subtitle = new Label
        {
            Text = "Stream Deck Agent",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Regular),
            ForeColor = TextMuted
        };
        panel.Controls.Add(subtitle);
        panel.Controls.Add(title);

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 48, 0, 0),
            BackColor = Surface
        };

        foreach (var page in new[] { "Overview", "Shortcuts", "Appearance", "Device", "Pairing", "Updates", "Logs" })
        {
            var button = CreateNavButton(page);
            navButtons.Add(page, button);
            nav.Controls.Add(button);
        }

        panel.Controls.Add(nav);
        return panel;
    }

    private Button CreateNavButton(string page)
    {
        var button = new Button
        {
            Text = page,
            Width = NavigationButtonWidth,
            Height = NavigationButtonHeight,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 0, 0, 0),
            Margin = new Padding(0, 0, 0, 10),
            BackColor = Surface,
            ForeColor = TextMuted,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Regular),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => ShowPage(page);
        return button;
    }

    private void ShowPage(string page)
    {
        currentPage = page;
        foreach (var (name, button) in navButtons)
        {
            var selected = string.Equals(name, page, StringComparison.Ordinal);
            button.BackColor = selected ? SurfaceAlt : Surface;
            button.ForeColor = selected ? TextPrimary : TextMuted;
            button.Font = new Font(Font.FontFamily, 10F, selected ? FontStyle.Bold : FontStyle.Regular);
            button.FlatAppearance.BorderColor = selected ? Accent : Surface;
            button.FlatAppearance.BorderSize = selected ? 1 : 0;
        }

        foreach (Control control in contentPanel.Controls.Cast<Control>().ToArray())
        {
            control.Dispose();
        }

        contentPanel.Controls.Clear();
        infoValueLabels.Clear();
        contentPanel.Controls.Add(page switch
        {
            "Overview" => CreateOverviewPage(),
            "Shortcuts" => CreateShortcutsPage(),
            "Appearance" => CreateAppearancePage(),
            "Device" => CreateDevicePage(),
            "Pairing" => CreatePairingPage(),
            "Updates" => CreateUpdatesPage(),
            "Logs" => CreateLogsPage(),
            _ => CreateOverviewPage()
        });
        UpdateShortcutImportRefreshTimer(page);
    }

    private Control CreateOverviewPage()
    {
        var snapshot = state.Snapshot();
        var panel = CreatePage("Overview");
        var grid = CreateInfoGrid();
        var deviceInfo = snapshot.DeviceInfo;
        AddSectionHeader(grid, "DEVICE");
        AddInfoRow(grid, "CYD", AgentStatusFormatter.ToDeviceStatus(snapshot));
        AddInfoRow(grid, "Firmware", FormatUnavailable(deviceInfo?.FirmwareVersion));
        AddInfoRow(grid, "Wi-Fi", FormatWifi(deviceInfo?.Wifi));
        AddInfoRow(grid, "SSID", FormatUnavailable(deviceInfo?.Wifi.Ssid));
        AddInfoRow(grid, "RSSI", FormatRssi(deviceInfo?.Wifi.Rssi));
        AddInfoRow(grid, "Device IP", FormatUnavailable(deviceInfo?.Wifi.Ip));
        AddInfoRow(grid, "Agent host", FormatUnavailable(deviceInfo?.AgentHost));
        AddInfoRow(grid, "Uptime", FormatUptime(deviceInfo?.UptimeSeconds));
        AddInfoRow(grid, "WebSocket", AgentStatusFormatter.ToUserStatus(snapshot.HostStatus));
        AddInfoRow(grid, "Last seen", AgentStatusFormatter.FormatLastSeen(snapshot));
        AddSectionHeader(grid, "SOFTWARE");
        AddInfoRow(grid, "Agent", GetAgentVersion());
        AddInfoRow(grid, "Protocol", ProtocolConstants.CurrentVersion.ToString());
        AddSectionHeader(grid, "HOST");
        AddInfoRow(grid, "Mode", snapshot.Mode);
        AddInfoRow(grid, "LAN IP", snapshot.LanAddresses.Count == 0 ? "Unavailable" : string.Join(", ", snapshot.LanAddresses));
        AddInfoRow(grid, "Port", snapshot.HostPort.ToString());
        AddInfoRow(grid, "Start with Windows", startup.IsEnabled() ? "On" : "Off");
        AddPageBodyControl(panel, grid);
        AddPageFooter(panel, CreateActionsBar(("Reconnect device", ReconnectAsync)));
        return panel;
    }

    private Control CreateDevicePage()
    {
        var snapshot = state.Snapshot();
        var deviceInfo = snapshot.DeviceInfo;
        var panel = CreatePage("Device");
        var grid = CreateInfoGrid();
        AddInfoRow(grid, "Device name", snapshot.DeviceName ?? "Unavailable");
        AddInfoRow(grid, "Device ID", snapshot.DeviceId ?? "Unavailable");
        AddInfoRow(grid, "Status", AgentStatusFormatter.ToDeviceStatus(snapshot));
        AddSectionHeader(grid, "REPORTED DEVICE");
        AddInfoRow(grid, "Firmware", FormatUnavailable(deviceInfo?.FirmwareVersion));
        AddInfoRow(grid, "Uptime", FormatUptime(deviceInfo?.UptimeSeconds));
        AddInfoRow(grid, "Wi-Fi", FormatWifi(deviceInfo?.Wifi));
        AddInfoRow(grid, "SSID", FormatUnavailable(deviceInfo?.Wifi.Ssid));
        AddInfoRow(grid, "RSSI", FormatRssi(deviceInfo?.Wifi.Rssi));
        AddInfoRow(grid, "IP", FormatUnavailable(deviceInfo?.Wifi.Ip));
        AddInfoRow(grid, "Agent host", FormatUnavailable(deviceInfo?.AgentHost));
        AddInfoRow(grid, "SD", FormatSdState(deviceInfo?.Sd));
        AddInfoRow(grid, "SD capacity", FormatBytes(deviceInfo?.Sd.CapacityBytes));
        AddInfoRow(grid, "SD free", FormatBytes(deviceInfo?.Sd.FreeBytes));
        AddInfoRow(grid, "Heap free", FormatBytes(deviceInfo?.Heap.FreeBytes));
        AddInfoRow(grid, "Heap min free", FormatBytes(deviceInfo?.Heap.MinFreeBytes));
        AddInfoRow(grid, "Heap largest block", FormatBytes(deviceInfo?.Heap.LargestBlockBytes));
        AddSectionHeader(grid, "CONNECTION");
        AddInfoRow(grid, "Remote IP", FormatRemoteHost(snapshot.RemoteAddress));
        AddInfoRow(grid, "Remote endpoint", snapshot.RemoteAddress ?? "Unavailable");
        AddInfoRow(grid, "WebSocket state", AgentStatusFormatter.ToUserStatus(snapshot.HostStatus));
        AddInfoRow(grid, "Last seen", AgentStatusFormatter.FormatLastSeen(snapshot));
        AddInfoRow(grid, "Agent port", snapshot.HostPort.ToString());
        AddInfoRow(grid, "Dashboard port", snapshot.DashboardPort == 0 ? "Unavailable" : snapshot.DashboardPort.ToString());
        AddPageBodyControl(panel, grid);
        var hasActiveDevice = !string.IsNullOrWhiteSpace(snapshot.DeviceId);
        AddPageFooter(panel, CreateDeviceActionsBar(hasActiveDevice,
            ("Reconnect", ReconnectAsync),
            ("Restart", RestartDeviceAsync),
            ("Factory reset", FactoryResetDeviceAsync),
            ("Forget device", ForgetDeviceAsync)));
        return panel;
    }

    private Control CreatePairingPage()
    {
        var panel = CreatePage("Pairing");
        var status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            ForeColor = TextMuted,
            Text = "Loading pending pairing requests…",
            Padding = new Padding(0, 10, 0, 0)
        };
        var list = CreateThemedListView();
        list.Dock = DockStyle.Fill;
        list.MultiSelect = false;
        ConfigurePairingColumns(list, MinimumSettingsClientWidth);
        AttachPairingColumnResizeHandler(list);

        AddPageBodyControl(panel, list);
        AddPageBodyControl(panel, status);
        AddPageFooter(panel, CreateActionsBar(
            ("Approve", async () => await PairSelectedDeviceAsync(list, status, approve: true).ConfigureAwait(true)),
            ("Reject", async () => await PairSelectedDeviceAsync(list, status, approve: false).ConfigureAwait(true)),
            ("Refresh", async () => await RefreshPairingListAsync(list, status).ConfigureAwait(true))));
        _ = RefreshPairingListAsync(list, status);
        return panel;
    }

    private Control CreateShortcutsPage()
    {
        var panel = CreatePage("Shortcuts");
        var loadResult = LoadShortcutConfigurationState(() => configurationStore.LoadOrDefault());
        shortcutsConfigurationLoadFailed = !loadResult.CanWriteShortcuts;
        var shortcuts = loadResult.Configuration.Shortcuts?.ToList() ?? [];
        var savedIds = shortcuts.Select(shortcut => shortcut.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var listHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface
        };
        var list = CreateThemedListView();
        list.Dock = DockStyle.Fill;
        ConfigureShortcutsColumns(list, MinimumSettingsClientWidth);
        list.SizeChanged += (_, _) => ConfigureShortcutsColumns(list, list.ClientSize.Width);
        var emptyState = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = TextMuted,
            BackColor = Surface,
            Text = CreateShortcutsEmptyStateText(loadResult),
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(20)
        };

        var shortcutCatalogNote = new Label
        {
            Dock = DockStyle.Top,
            Height = loadResult.CanWriteShortcuts ? 42 : 70,
            ForeColor = TextMuted,
            Text = loadResult.CanWriteShortcuts
                ? ShortcutsSourceOfTruthText
                : $"{ShortcutsSourceOfTruthText} Existing Agent config failed to load ({loadResult.ErrorType}); shortcut edits are disabled until you recover the file or explicitly import a full Agent config.",
            TextAlign = ContentAlignment.MiddleLeft
        };

        foreach (var shortcut in shortcuts)
        {
            AddShortcutItem(list, shortcut);
        }

        listHost.Controls.Add(list);
        listHost.Controls.Add(emptyState);
        UpdateShortcutsEmptyState(list, emptyState, loadResult);

        AddPageBodyControl(panel, listHost);
        AddPageBodyControl(panel, shortcutCatalogNote);
        AddPageFooter(panel, CreateActionsBar(
            ("Add", async () => await AddShortcutAsync(list, shortcuts, emptyState, loadResult).ConfigureAwait(true)),
            ("Edit", async () => await EditShortcutAsync(list, shortcuts).ConfigureAwait(true)),
            ("Delete", async () => await DeleteShortcutAsync(list, shortcuts, emptyState, loadResult).ConfigureAwait(true)),
            ("Move up", async () => await MoveShortcutAsync(list, shortcuts, -1).ConfigureAwait(true)),
            ("Move down", async () => await MoveShortcutAsync(list, shortcuts, 1).ConfigureAwait(true)),
            ("Import full config", ImportConfigurationAsync),
            ("Export full config", ExportConfigurationAsync)));
        var pendingImports = runtime.GetPendingShortcutImports(savedIds);
        shortcutImportSignature = CreatePendingShortcutImportSignature(pendingImports);
        if (pendingImports.Count > 0)
        {
            AddPageBodyControl(panel, CreatePendingShortcutImportsPanel(pendingImports, list, shortcuts, emptyState, loadResult));
        }
        return panel;
    }

    private Control CreatePendingShortcutImportsPanel(
        IReadOnlyList<PendingShortcutImport> pendingImports,
        ListView shortcutsList,
        List<ShortcutDefinition> shortcuts,
        Label emptyState,
        ShortcutConfigurationLoadState loadResult)
    {
        var host = new Panel
        {
            Dock = DockStyle.Top,
            Height = 156,
            Padding = new Padding(0, 8, 0, 8)
        };

        var label = new Label
        {
            Dock = DockStyle.Top,
            Height = 36,
            ForeColor = TextMuted,
            Text = PendingShortcutImportsText,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var list = CreateThemedListView();
        list.Dock = DockStyle.Fill;
        list.BorderStyle = BorderStyle.FixedSingle;
        list.Columns.Add("Device shortcut", 220);
        list.Columns.Add("Reported action", 220);
        foreach (var pending in pendingImports)
        {
            list.Items.Add(new ListViewItem([pending.Label, pending.ActionType]) { Tag = pending });
        }

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Background
        };
        var configureButton = new Button
        {
            Text = "Configure selected",
            Width = 150,
            Height = 30
        };
        configureButton.Click += async (_, _) => await ConfigurePendingShortcutImportAsync(list, shortcutsList, shortcuts, emptyState, loadResult).ConfigureAwait(true);
        actions.Controls.Add(configureButton);

        host.Controls.Add(actions);
        host.Controls.Add(list);
        host.Controls.Add(label);
        return host;
    }

    private async Task ConfigurePendingShortcutImportAsync(
        ListView pendingList,
        ListView shortcutsList,
        List<ShortcutDefinition> shortcuts,
        Label emptyState,
        ShortcutConfigurationLoadState loadResult)
    {
        if (!EnsureShortcutsCanWrite())
        {
            return;
        }

        var index = GetSelectedShortcutIndex(pendingList);
        if (index < 0 || pendingList.Items[index].Tag is not PendingShortcutImport pending)
        {
            return;
        }

        using var dialog = new ShortcutEditorDialog(CreateShortcutDraftFromPendingImport(pending));
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (shortcuts.Any(shortcut => string.Equals(shortcut.Id, dialog.Shortcut.Id, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "A shortcut with this ID already exists. Edit the existing shortcut or choose a unique ID.", "Shortcut validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        shortcuts.Add(dialog.Shortcut);
        if (await SaveShortcutsAsync(shortcuts).ConfigureAwait(true))
        {
            AddShortcutItem(shortcutsList, dialog.Shortcut);
            shortcutsList.Items[^1].Selected = true;
            pendingList.Items.RemoveAt(index);
            UpdateShortcutsEmptyState(shortcutsList, emptyState, loadResult);
        }
    }

    internal static ShortcutDefinition CreateShortcutDraftFromPendingImport(PendingShortcutImport pending)
    {
        return new ShortcutDefinition
        {
            Id = pending.Id,
            Name = pending.Label,
            Icon = "shortcut",
            Action = new ShortcutActionDefinition { Type = NormalizePendingActionType(pending.ActionType) }
        };
    }

    private static string NormalizePendingActionType(string actionType)
    {
        return actionType is ShortcutActionTypes.LaunchApplication
            or ShortcutActionTypes.OpenUrl
            or ShortcutActionTypes.Hotkey
            or ShortcutActionTypes.Script
            or ShortcutActionTypes.PowerShell
            or ShortcutActionTypes.PredefinedAdapter
            ? actionType
            : ShortcutActionTypes.LaunchApplication;
    }

    internal static IReadOnlyList<PendingShortcutImport> FilterPendingShortcutImports(
        IReadOnlyList<PendingShortcutImport> pendingImports,
        IEnumerable<ShortcutDefinition> savedShortcuts)
    {
        var savedIds = savedShortcuts
            .Select(shortcut => shortcut.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return pendingImports
            .Where(pending => !savedIds.Contains(pending.Id))
            .Take(6)
            .ToArray();
    }

    internal static bool ShouldRefreshShortcutImports(string previousSignature, IReadOnlyList<PendingShortcutImport> currentImports)
    {
        return !string.Equals(previousSignature, CreatePendingShortcutImportSignature(currentImports), StringComparison.Ordinal);
    }

    internal static string CreatePendingShortcutImportSignature(IReadOnlyList<PendingShortcutImport> pendingImports)
    {
        return string.Join("\n", pendingImports
            .OrderBy(import => import.Id, StringComparer.OrdinalIgnoreCase)
            .Select(import => string.Join("\u001f", import.Id, import.Label, import.ActionType)));
    }

    private void UpdateShortcutImportRefreshTimer(string page)
    {
        if (string.Equals(page, "Shortcuts", StringComparison.Ordinal))
        {
            shortcutImportRefreshTimer.Start();
        }
        else
        {
            shortcutImportRefreshTimer.Stop();
        }
    }

    private void RefreshShortcutImportsIfChanged()
    {
        if (IsDisposed || !string.Equals(currentPage, "Shortcuts", StringComparison.Ordinal))
        {
            return;
        }

        var loadResult = LoadShortcutConfigurationState(configurationStore.LoadOrDefault);
        var savedIds = (loadResult.Configuration.Shortcuts ?? [])
            .Select(shortcut => shortcut.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pendingImports = runtime.GetPendingShortcutImports(savedIds);
        if (!ShouldRefreshShortcutImports(shortcutImportSignature, pendingImports))
        {
            return;
        }

        ShowPage("Shortcuts");
    }

    internal static string CreateShortcutsEmptyStateText(ShortcutConfigurationLoadState loadResult)
    {
        return loadResult.CanWriteShortcuts
            ? EmptyShortcutsText
            : $"Shortcuts unavailable because the existing Agent config failed to load ({loadResult.ErrorType}). Recover the file or use Import full config; the device does not provide shortcut preload data.";
    }

    private static void UpdateShortcutsEmptyState(ListView list, Label emptyState, ShortcutConfigurationLoadState loadResult)
    {
        emptyState.Text = CreateShortcutsEmptyStateText(loadResult);
        emptyState.Visible = list.Items.Count == 0;
        if (emptyState.Visible)
        {
            emptyState.BringToFront();
        }
    }

    private Control CreateAppearancePage()
    {
        var settings = pendingAppearance ?? LoadAppearanceConfiguration();
        pendingAppearance = settings;
        ApplyClientTheme(settings.Theme);
        contentPanel.BackColor = Background;
        var panel = CreatePage("Appearance");
        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = Background,
            Padding = new Padding(0, 4, 0, 22),
            Margin = new Padding(0, 0, 0, 18)
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, GetAppearanceFormLabelWidth(Width - NavigationWidth - WindowContentPadding.Horizontal)));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        form.SizeChanged += (_, _) =>
        {
            if (form.ColumnStyles.Count > 0)
            {
                form.ColumnStyles[0].Width = GetAppearanceFormLabelWidth(form.ClientSize.Width);
            }
        };

        AddComboRow(form, "Theme", ["dark", "light"], settings.Theme, value =>
        {
            settings.Theme = value;
            ApplyClientTheme(value);
        });
        AddColorRow(form, "Accent color", settings.Accent, value => settings.Accent = value);
        AddColorRow(form, "Background color", settings.Background, value => settings.Background = value);
        AddSliderRow(form, "Brightness", 10, 100, settings.Brightness, "%", value => settings.Brightness = value);
        AddComboRow(form, "Button style", ["flat", "rounded", "pill"], settings.ButtonStyle, value => settings.ButtonStyle = value);
        AddPageListRow(form, "Page order / visibility", settings);
        AddSliderRow(form, "Dim timeout", 0, 86400, settings.DimAfterSec, " sec", value => settings.DimAfterSec = value);
        AddSliderRow(form, "Dim level", 1, 100, settings.DimBrightness, "%", value => settings.DimBrightness = value);
        AddSliderRow(form, "Screen-off timeout", 0, 86400, settings.ScreenOffAfterSec, " sec", value => settings.ScreenOffAfterSec = value);
        AddCheckRow(form, "Wake on touch", settings.WakeOnTouch, value => settings.WakeOnTouch = value);

        appearanceStatusLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            ForeColor = TextMuted,
            Text = "Changes save locally and sync to the connected device when available.",
            Padding = new Padding(0, 10, 0, 0)
        };
        AddPageBodyControl(panel, form);
        AddPageBodyControl(panel, appearanceStatusLabel);
        return panel;
    }

    private void AddComboRow(TableLayoutPanel form, string label, string[] values, string current, Action<string> update)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 180,
            BackColor = Surface,
            ForeColor = TextPrimary
        };
        combo.Items.AddRange(values.Cast<object>().ToArray());
        combo.SelectedItem = values.Contains(current, StringComparer.Ordinal) ? current : values[0];
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (combo.SelectedItem is string value)
            {
                update(value);
                QueueAppearanceSave();
            }
        };
        AddControlRow(form, label, combo);
    }

    private void AddColorRow(TableLayoutPanel form, string label, string current, Action<string> update)
    {
        var panel = CreateColorPickerControl(current);
        var swatch = panel.Controls.OfType<Panel>().First();
        var valueLabel = panel.Controls.OfType<Label>().First();
        var choose = panel.Controls.OfType<Button>().First();
        choose.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = swatch.BackColor, FullOpen = true };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var value = ToHexColor(dialog.Color);
            valueLabel.Text = value;
            swatch.BackColor = dialog.Color;
            update(value);
            QueueAppearanceSave();
        };
        AddControlRow(form, label, panel);
    }

    internal static FlowLayoutPanel CreateColorPickerControl(string current)
    {
        var value = IsHexColor(current) ? current.ToUpperInvariant() : ToHexColor(Accent);
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            BackColor = Background,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Height = ColorPickerRowHeight,
            Tag = DeviceColorPickerTag,
            AccessibleName = "Device color picker"
        };
        var swatch = new Panel
        {
            Width = ColorSwatchSize,
            Height = ColorSwatchSize,
            BackColor = ParseHexColor(value, Accent),
            Margin = new Padding(0, 7, 10, 0),
            BorderStyle = BorderStyle.FixedSingle,
            Tag = DeviceColorPreviewTag,
            AccessibleName = $"Current color {value}"
        };
        var hex = new Label
        {
            Text = value,
            AutoSize = false,
            Width = 86,
            Height = ColorPickerRowHeight,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextPrimary,
            Font = new Font(FontFamily.GenericSansSerif, 9.5F, FontStyle.Bold),
            AccessibleName = $"Current color value {value}"
        };
        var choose = CreateNeutralButton("Choose…");
        choose.AccessibleName = $"Choose {value} color";
        panel.Controls.Add(swatch);
        panel.Controls.Add(hex);
        panel.Controls.Add(choose);
        return panel;
    }

    private void AddSliderRow(TableLayoutPanel form, string label, int min, int max, int current, string suffix, Action<int> update)
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            BackColor = Background,
            ColumnCount = 2,
            Margin = new Padding(0, 6, 0, 6)
        };
        var valueLabel = new Label { Width = GetAppearanceSliderValueWidth($"{current}{suffix}"), ForeColor = TextPrimary, Text = $"{current}{suffix}", Padding = new Padding(8, 6, 0, 0), TextAlign = ContentAlignment.MiddleLeft };
        var track = new TrackBar
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = max > 1000 ? 3600 : 10,
            SmallChange = max > 1000 ? 60 : 1,
            LargeChange = max > 1000 ? 300 : 10,
            Value = Math.Clamp(current, min, max),
            Width = GetAppearanceSliderTrackWidth(Width - NavigationWidth - WindowContentPadding.Horizontal)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, track.Width));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, valueLabel.Width));
        panel.SizeChanged += (_, _) =>
        {
            var trackWidth = GetAppearanceSliderTrackWidth(form.ClientSize.Width);
            track.Width = trackWidth;
            panel.ColumnStyles[0].Width = trackWidth;
        };
        track.ValueChanged += (_, _) =>
        {
            valueLabel.Text = $"{track.Value}{suffix}";
            valueLabel.Width = GetAppearanceSliderValueWidth(valueLabel.Text);
            panel.ColumnStyles[1].Width = valueLabel.Width;
            update(track.Value);
            QueueAppearanceSave();
        };
        panel.Controls.Add(track, 0, 0);
        panel.Controls.Add(valueLabel, 1, 0);
        AddControlRow(form, label, panel);
    }

    private void AddCheckRow(TableLayoutPanel form, string label, bool current, Action<bool> update)
    {
        var check = new CheckBox { Checked = current, AutoSize = true, BackColor = Background, ForeColor = TextPrimary };
        check.CheckedChanged += (_, _) =>
        {
            update(check.Checked);
            QueueAppearanceSave();
        };
        AddControlRow(form, label, check);
    }

    private void AddPageListRow(TableLayoutPanel form, string label, AppearanceState settings)
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            BackColor = Background,
            ColumnCount = 2
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

        var list = new CheckedListBox
        {
            Width = 240,
            Height = 118,
            CheckOnClick = true,
            BackColor = Surface,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };

        var moving = false;
        void RefreshPageList(int selectedIndex)
        {
            moving = true;
            list.Items.Clear();
            foreach (var page in settings.PageOrder)
            {
                list.Items.Add(page, settings.PageVisibility.Contains(page, StringComparer.Ordinal));
            }

            if (list.Items.Count > 0)
            {
                list.SelectedIndex = Math.Clamp(selectedIndex, 0, list.Items.Count - 1);
            }

            moving = false;
        }

        void SaveVisiblePages()
        {
            settings.PageVisibility = settings.PageOrder
                .Where(page => string.Equals(page, "info", StringComparison.Ordinal) || list.CheckedItems.Cast<string>().Contains(page, StringComparer.Ordinal))
                .ToArray();
            QueueAppearanceSave();
        }

        list.ItemCheck += (_, args) =>
        {
            if (moving)
            {
                return;
            }

            var page = (string)list.Items[args.Index];
            if (string.Equals(page, "info", StringComparison.Ordinal) && args.NewValue != CheckState.Checked)
            {
                args.NewValue = CheckState.Checked;
                return;
            }

            BeginInvoke((Action)SaveVisiblePages);
        };

        var buttons = new FlowLayoutPanel { AutoSize = true, BackColor = Background, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var up = CreateActionButton("Up");
        var down = CreateActionButton("Down");

        void MoveSelected(int offset)
        {
            var index = list.SelectedIndex;
            var target = index + offset;
            if (index < 0 || target < 0 || target >= settings.PageOrder.Length)
            {
                return;
            }

            (settings.PageOrder[index], settings.PageOrder[target]) = (settings.PageOrder[target], settings.PageOrder[index]);
            settings.PageVisibility = settings.PageOrder
                .Where(page => settings.PageVisibility.Contains(page, StringComparer.Ordinal))
                .ToArray();
            RefreshPageList(target);
            QueueAppearanceSave();
        }

        up.Click += (_, _) => MoveSelected(-1);
        down.Click += (_, _) => MoveSelected(1);
        buttons.Controls.Add(up);
        buttons.Controls.Add(down);
        panel.Controls.Add(list, 0, 0);
        panel.Controls.Add(buttons, 1, 0);
        RefreshPageList(0);
        AddControlRow(form, label, panel);
    }

    private void AddControlRow(TableLayoutPanel form, string label, Control control)
    {
        var name = new Label
        {
            Text = label,
            AutoSize = true,
            ForeColor = TextMuted,
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Regular),
            Padding = new Padding(0, 10, 0, 10)
        };
        control.Margin = new Padding(0, 6, 0, 6);
        form.Controls.Add(name, 0, form.RowCount);
        form.Controls.Add(control, 1, form.RowCount);
        form.RowCount++;
    }

    internal static Color MapClientThemeColor(Color color, string theme)
    {
        if (string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase))
        {
            if (color.ToArgb() == DarkBackground.ToArgb()) return LightBackground;
            if (color.ToArgb() == DarkSurface.ToArgb()) return LightSurface;
            if (color.ToArgb() == DarkSurfaceAlt.ToArgb()) return LightSurfaceAlt;
            if (color.ToArgb() == DarkTextPrimary.ToArgb()) return LightTextPrimary;
            if (color.ToArgb() == DarkTextMuted.ToArgb()) return LightTextMuted;
        }
        else
        {
            if (color.ToArgb() == LightBackground.ToArgb()) return DarkBackground;
            if (color.ToArgb() == LightSurface.ToArgb()) return DarkSurface;
            if (color.ToArgb() == LightSurfaceAlt.ToArgb()) return DarkSurfaceAlt;
            if (color.ToArgb() == LightTextPrimary.ToArgb()) return DarkTextPrimary;
            if (color.ToArgb() == LightTextMuted.ToArgb()) return DarkTextMuted;
        }

        return color;
    }

    internal static Panel CreateThemedContentPanel(string theme)
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = GetClientThemeBackground(theme),
            Padding = WindowContentPadding
        };
    }

    internal static Padding GetClientContentPadding() => WindowContentPadding;

    internal static int GetNavigationWidth() => NavigationWidth;

    internal static Size GetNavigationButtonSize() => new(NavigationButtonWidth, NavigationButtonHeight);

    internal static int GetActionButtonHeight() => ActionButtonHeight;

    internal static int MeasureSettingsActionButtonWidth(string text)
    {
        var measured = TextRenderer.MeasureText(text, SystemFonts.MessageBoxFont).Width + 34;
        return Math.Max(ActionButtonMinimumWidth, measured);
    }

    internal static int CalculateActionBarHeight(int availableWidth, IReadOnlyList<int> buttonWidths)
    {
        if (buttonWidths.Count == 0)
        {
            return 0;
        }

        var width = Math.Max(1, availableWidth);
        var rows = 1;
        var rowWidth = 0;
        foreach (var buttonWidth in buttonWidths)
        {
            var required = buttonWidth + ActionButtonHorizontalMargin;
            if (rowWidth > 0 && rowWidth + required > width)
            {
                rows++;
                rowWidth = 0;
            }

            rowWidth += required;
        }

        return rows * (ActionButtonHeight + ActionButtonVerticalMargin) + 8;
    }

    internal static int GetAppearanceFormLabelWidth(int availableWidth)
    {
        return Math.Clamp(availableWidth / 3, 150, 180);
    }

    internal static int GetAppearanceSliderTrackWidth(int availableWidth)
    {
        var labelWidth = GetAppearanceFormLabelWidth(availableWidth);
        var valueWidth = GetAppearanceSliderValueWidth("86400 sec");
        return Math.Clamp(availableWidth - labelWidth - valueWidth - 64, AppearanceMinimumSliderTrackWidth, AppearanceMaximumSliderTrackWidth);
    }

    internal static int GetAppearanceSliderValueWidth(string value)
    {
        return Math.Max(76, TextRenderer.MeasureText(value, SystemFonts.MessageBoxFont).Width + 16);
    }

    internal static Color GetClientThemeBackground(string theme)
    {
        return string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase) ? LightBackground : DarkBackground;
    }

    internal static bool ShouldPreserveClientThemeBackColor(object? tag)
    {
        return string.Equals(tag as string, DeviceColorPreviewTag, StringComparison.Ordinal);
    }

    internal static void MarkDeviceColorPreview(Control control)
    {
        control.Tag = DeviceColorPreviewTag;
    }

    private void ApplyClientTheme(string theme)
    {
        var useLightTheme = string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase);
        if (clientLightTheme == useLightTheme)
        {
            return;
        }

        clientLightTheme = useLightTheme;
        SuspendLayout();
        try
        {
            ApplyClientThemeColors(this, theme);
        }
        finally
        {
            ResumeLayout(performLayout: true);
        }
    }

    private static void ApplyClientThemeColors(Control control, string theme)
    {
        if (!ShouldPreserveClientThemeBackColor(control.Tag))
        {
            control.BackColor = MapClientThemeColor(control.BackColor, theme);
        }

        control.ForeColor = MapClientThemeColor(control.ForeColor, theme);
        foreach (Control child in control.Controls)
        {
            ApplyClientThemeColors(child, theme);
        }
    }


    private void QueueAppearanceSave()
    {
        pendingAppearance = LoadAppearanceControls();
        if (appearanceStatusLabel is not null)
        {
            appearanceStatusLabel.Text = "Saving…";
        }
        appearanceSaveTimer.Stop();
        appearanceSaveTimer.Start();
    }

    private AppearanceState LoadAppearanceControls()
    {
        return pendingAppearance ?? LoadAppearanceConfiguration();
    }

    private Control CreateUpdatesPage()
    {
        var snapshot = state.Snapshot();
        var panel = CreatePage("Updates");
        var grid = CreateInfoGrid();
        AddInfoRow(grid, "Agent version", GetAgentVersion());
        AddInfoRow(grid, "Firmware version", FormatUnavailable(snapshot.DeviceInfo?.FirmwareVersion));
        AddInfoRow(grid, "Protocol version", ProtocolConstants.CurrentVersion.ToString());
        AddInfoRow(grid, "Available version", "Unavailable");
        AddInfoRow(grid, "Release discovery", "Unavailable");
        AddPageBodyControl(panel, grid);
        AddPageFooter(panel, CreateUpdateActionsBar(snapshot.DeviceInfo?.Wifi.Ip));
        return panel;
    }

    private Control CreateLogsPage()
    {
        var panel = CreatePage("Logs");
        var text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Surface,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.None,
            Font = new Font("Cascadia Mono", 9F, FontStyle.Regular, GraphicsUnit.Point),
            Text = string.Join(Environment.NewLine, AgentDiagnosticLog.ReadRecentLines())
        };
        ApplySupportedScrollTheme(text);
        AddPageBodyControl(panel, text);
        AddPageFooter(panel, CreateActionsBar(
            ("Copy logs", () => Clipboard.SetText(text.Text)),
            ("Open log folder", OpenLogFolder),
            ("Clear view", () =>
            {
                AgentDiagnosticLog.ClearView();
                text.Clear();
            })));
        return panel;
    }

    private Panel CreatePage(string title)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Background };
        var body = CreatePageBodyPanel();
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = PageTitleHeight,
            Font = new Font(Font.FontFamily, 24F, FontStyle.Bold),
            ForeColor = TextPrimary,
            Padding = new Padding(0, 4, 0, 18)
        };
        panel.Controls.Add(body);
        panel.Controls.Add(titleLabel);
        return panel;
    }

    private static Panel CreatePageBodyPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Background,
            Tag = PageBodyTag,
            Padding = new Padding(0, 0, 12, 0)
        };
        ApplySupportedScrollTheme(panel);
        return panel;
    }

    private static void AddPageBodyControl(Control page, Control control)
    {
        FindPageBody(page).Controls.Add(control);
    }

    private static void AddPageFooter(Control page, Control footer)
    {
        footer.Tag = PageFooterTag;
        page.Controls.Add(footer);
        footer.BringToFront();
    }

    private static Panel FindPageBody(Control page)
    {
        return page.Controls.OfType<Panel>().First(control => Equals(control.Tag, PageBodyTag));
    }

    internal static bool PageLayoutHasScrollableBodyAndAccessibleFooterForTests(int width, int height, int contentHeight, IReadOnlyList<int> footerButtonWidths)
    {
        var context = SynchronizationContext.Current;
        try
        {
            using var page = new Panel { Size = new Size(width, height), BackColor = Background };
            using var title = new Label { Dock = DockStyle.Top, Height = PageTitleHeight };
            using var body = CreatePageBodyPanel();
            using var content = new Panel { Dock = DockStyle.Top, Height = contentHeight };
            using var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = CalculateActionBarHeight(width, footerButtonWidths), Tag = PageFooterTag };
            body.Controls.Add(content);
            page.Controls.Add(body);
            page.Controls.Add(title);
            page.Controls.Add(footer);
            page.PerformLayout();
            return body.AutoScroll
                && body.Bounds.Top >= title.Bottom
                && footer.Bounds.Bottom <= page.ClientSize.Height
                && body.Bounds.Bottom <= footer.Top
                && body.DisplayRectangle.Height > body.ClientSize.Height;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }

    private static TableLayoutPanel CreateInfoGrid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = Background,
            Padding = new Padding(0, 4, 0, 22),
            Margin = new Padding(0, 0, 0, 18)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return grid;
    }

    private static ListView CreateThemedListView()
    {
        var list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            BackColor = Surface,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.None,
            OwnerDraw = true,
            HideSelection = false
        };
        list.DrawColumnHeader += (_, args) => DrawListHeader(args);
        list.DrawItem += (_, args) => args.DrawDefault = true;
        list.DrawSubItem += (_, args) => DrawListSubItem(args);
        list.HandleCreated += (_, _) => ApplySupportedScrollTheme(list);
        ApplySupportedScrollTheme(list);
        return list;
    }

    private static void DrawListHeader(DrawListViewColumnHeaderEventArgs args)
    {
        using var background = new SolidBrush(SurfaceAlt);
        using var border = new Pen(Border);
        args.Graphics.FillRectangle(background, args.Bounds);
        args.Graphics.DrawRectangle(border, args.Bounds.X, args.Bounds.Y, args.Bounds.Width - 1, args.Bounds.Height - 1);
        var bounds = Rectangle.Inflate(args.Bounds, -8, 0);
        TextRenderer.DrawText(args.Graphics, args.Header?.Text ?? string.Empty, SystemFonts.MessageBoxFont, bounds, TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.Left);
    }

    private static void DrawListSubItem(DrawListViewSubItemEventArgs args)
    {
        var selected = args.Item?.Selected == true;
        var backColor = selected ? Accent : Surface;
        var foreColor = selected ? Color.White : TextPrimary;
        using var background = new SolidBrush(backColor);
        args.Graphics.FillRectangle(background, args.Bounds);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.Left;
        TextRenderer.DrawText(args.Graphics, args.SubItem?.Text ?? string.Empty, SystemFonts.MessageBoxFont, Rectangle.Inflate(args.Bounds, -8, 0), foreColor, flags);
    }

    private static void ApplySupportedScrollTheme(Control control)
    {
        if (control.IsHandleCreated && Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            _ = SetWindowTheme(control.Handle, clientLightTheme ? "Explorer" : "DarkMode_Explorer", null);
        }

        control.HandleCreated += (_, _) =>
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                _ = SetWindowTheme(control.Handle, clientLightTheme ? "Explorer" : "DarkMode_Explorer", null);
            }
        };
    }

    internal static int[] CalculatePairingColumnWidths(int availableWidth)
    {
        var width = Math.Max(360, availableWidth - ListVerticalScrollbarAllowance);
        var firstSeen = 132;
        var address = Math.Clamp(width / 5, 116, 150);
        var deviceId = Math.Clamp(width / 4, 128, 174);
        var name = Math.Max(96, width - firstSeen - address - deviceId);
        return [name, deviceId, address, firstSeen];
    }

    internal static int[] CalculateShortcutsColumnWidths(int availableWidth)
    {
        var width = Math.Max(360, availableWidth - ListVerticalScrollbarAllowance);
        var id = Math.Clamp(width / 5, 92, 132);
        var action = Math.Clamp(width / 5, 104, 138);
        var name = Math.Clamp(width / 4, 120, 170);
        var target = Math.Max(120, width - name - id - action);
        return [name, id, action, target];
    }

    internal static bool ColumnWidthsFitAvailableWidth(int availableWidth, IReadOnlyList<int> widths)
    {
        return widths.Sum() + ListVerticalScrollbarAllowance <= availableWidth;
    }

    internal static void ConfigurePairingColumns(ListView list, int availableWidth)
    {
        SetListColumns(list, ["Name", "Device ID", "Address", "First seen"], CalculatePairingColumnWidths(availableWidth));
    }

    internal static void AttachPairingColumnResizeHandler(ListView list)
    {
        list.SizeChanged += (_, _) => ConfigurePairingColumns(list, list.ClientSize.Width);
    }

    internal static void ConfigureShortcutsColumns(ListView list, int availableWidth)
    {
        SetListColumns(list, ["Name", "ID", "Action", "Target"], CalculateShortcutsColumnWidths(availableWidth));
    }

    private static void SetListColumns(ListView list, string[] names, int[] widths)
    {
        while (list.Columns.Count < names.Length)
        {
            list.Columns.Add(names[list.Columns.Count], widths[list.Columns.Count]);
        }

        for (var index = 0; index < names.Length; index++)
        {
            var column = list.Columns[index];
            if (!string.Equals(column.Text, names[index], StringComparison.Ordinal))
            {
                column.Text = names[index];
            }

            if (column.Width != widths[index])
            {
                column.Width = widths[index];
            }
        }
    }

    private void AddSectionHeader(TableLayoutPanel grid, string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Accent,
            Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold),
            Padding = new Padding(0, 22, 0, 8)
        };
        grid.Controls.Add(label, 0, grid.RowCount);
        grid.SetColumnSpan(label, 2);
        grid.RowCount++;
    }

    private void AddInfoRow(TableLayoutPanel grid, string label, string value)
    {
        var name = new Label
        {
            Text = label,
            AutoSize = true,
            ForeColor = TextMuted,
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Regular),
            Padding = new Padding(0, 7, 0, 7)
        };
        var content = new Label
        {
            Text = value,
            AutoSize = true,
            ForeColor = TextPrimary,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Regular),
            Padding = new Padding(0, 7, 0, 7)
        };
        grid.Controls.Add(name, 0, grid.RowCount);
        grid.Controls.Add(content, 1, grid.RowCount);
        infoValueLabels[label] = content;
        grid.RowCount++;
    }

    private Control CreateActionsBar(params (string Text, Func<Task> Action)[] actions)
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, ActionButtonHeight + ActionButtonVerticalMargin + 8),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Background,
            Padding = new Padding(0, 0, 0, 8)
        };

        foreach (var action in actions)
        {
            var button = CreateActionButton(action.Text);
            button.Click += async (_, _) => await action.Action();
            bar.Controls.Add(button);
        }

        bar.SizeChanged += (_, _) => UpdateActionsBarHeight(bar);
        bar.ParentChanged += (_, _) =>
        {
            if (bar.Parent is not null)
            {
                bar.Parent.SizeChanged += (_, _) => UpdateActionsBarHeight(bar);
            }

            UpdateActionsBarHeight(bar);
        };
        bar.ControlAdded += (_, _) => UpdateActionsBarHeight(bar);
        UpdateActionsBarHeight(bar);

        return bar;
    }

    private static void UpdateActionsBarHeight(FlowLayoutPanel bar)
    {
        var widths = bar.Controls.Cast<Control>().Select(control => control.Width).ToArray();
        var parentWidth = bar.Parent?.ClientSize.Width ?? bar.ClientSize.Width;
        var height = CalculateActionBarHeight(parentWidth, widths);
        if (height > 0 && bar.Height != height)
        {
            bar.Height = height;
        }
    }

    private Control CreateDeviceActionsBar(bool hasActiveDevice, params (string Text, Func<Task> Action)[] actions)
    {
        var bar = (FlowLayoutPanel)CreateActionsBar(actions);
        foreach (Control control in bar.Controls)
        {
            if (control is Button button && string.Equals(button.Text, "Forget device", StringComparison.Ordinal))
            {
                button.Enabled = hasActiveDevice;
                if (!hasActiveDevice)
                {
                    button.Text = "Forget device unavailable";
                    button.Cursor = Cursors.Default;
                }
            }
        }

        return bar;
    }

    private Control CreateUpdateActionsBar(string? deviceIp)
    {
        var bar = (FlowLayoutPanel)CreateActionsBar(("Install firmware from file (.bin)", InstallFirmwareFromFileAsync));
        if (string.IsNullOrWhiteSpace(deviceIp) && bar.Controls[0] is Button button)
        {
            button.Enabled = false;
            button.Text = "Install firmware unavailable";
            button.Cursor = Cursors.Default;
        }

        return bar;
    }

    private Control CreateActionsBar(params (string Text, Action Action)[] actions)
    {
        return CreateActionsBar(actions.Select(action => (action.Text, (Func<Task>)(() =>
        {
            action.Action();
            return Task.CompletedTask;
        }))).ToArray());
    }

    private Button CreateActionButton(string text)
    {
        var isPrimary = string.Equals(text, "Reconnect", StringComparison.Ordinal)
            || string.Equals(text, "Reconnect device", StringComparison.Ordinal)
            || string.Equals(text, "Approve", StringComparison.Ordinal);
        var isDestructive = text.Contains("Delete", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Reject", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Factory reset", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Forget", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Clear", StringComparison.OrdinalIgnoreCase);
        return CreateStyledActionButton(text, isPrimary || isDestructive);
    }

    private static Button CreateNeutralButton(string text)
    {
        return CreateStyledActionButton(text, useAccent: false);
    }

    private static Button CreateStyledActionButton(string text, bool useAccent)
    {
        var button = new Button
        {
            Text = text,
            Height = ActionButtonHeight,
            AutoSize = true,
            MinimumSize = new Size(MeasureSettingsActionButtonWidth(text), ActionButtonHeight),
            FlatStyle = FlatStyle.Flat,
            BackColor = useAccent ? Accent : SurfaceAlt,
            ForeColor = useAccent ? Color.White : TextPrimary,
            Font = new Font(FontFamily.GenericSansSerif, 9.5F, useAccent ? FontStyle.Bold : FontStyle.Regular),
            Padding = new Padding(12, 0, 12, 0),
            Margin = new Padding(0, ActionButtonVerticalMargin, ActionButtonHorizontalMargin, 0),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = useAccent ? 0 : 1;
        button.FlatAppearance.BorderColor = Border;
        return button;
    }

    private async Task RefreshPairingListAsync(ListView list, Label status)
    {
        try
        {
            var devices = await pairing.ListPendingAsync().ConfigureAwait(true);
            if (list.IsDisposed || status.IsDisposed)
            {
                return;
            }

            list.Items.Clear();
            foreach (var device in devices)
            {
                list.Items.Add(new ListViewItem([
                    FormatUnavailable(device.Name),
                    FormatUnavailable(device.DeviceId),
                    FormatUnavailable(device.RemoteAddress),
                    device.FirstSeenUtc.ToLocalTime().ToString("g")
                ]) { Tag = device.DeviceId });
            }

            status.ForeColor = TextMuted;
            status.Text = devices.Count == 0
                ? "No devices are waiting for pairing approval."
                : $"{devices.Count} device(s) waiting. Select one and approve or reject it.";
        }
        catch (Exception exception)
        {
            AgentDiagnosticLog.Write($"Pairing list refresh failed: {exception.GetType().Name}");
            if (!status.IsDisposed)
            {
                status.ForeColor = Color.LightSalmon;
                status.Text = "Pairing requests could not be loaded.";
            }
        }
    }

    private async Task PairSelectedDeviceAsync(ListView list, Label status, bool approve)
    {
        if (list.SelectedItems.Count == 0 || list.SelectedItems[0].Tag is not string deviceId || string.IsNullOrWhiteSpace(deviceId))
        {
            status.ForeColor = Color.Khaki;
            status.Text = "Select a pending device first.";
            return;
        }

        try
        {
            var changed = approve
                ? await pairing.ApproveAsync(deviceId).ConfigureAwait(true)
                : await pairing.RejectAsync(deviceId).ConfigureAwait(true);
            status.ForeColor = changed ? TextMuted : Color.Khaki;
            status.Text = changed
                ? (approve ? "Pairing approved." : "Pairing rejected.")
                : "That pairing request is no longer pending.";
            await RefreshPairingListAsync(list, status).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            AgentDiagnosticLog.Write($"Pairing {(approve ? "approval" : "rejection")} failed: {exception.GetType().Name}");
            status.ForeColor = Color.LightSalmon;
            status.Text = approve ? "Pairing could not be approved." : "Pairing could not be rejected.";
        }
    }

    private Control CreatePlaceholder(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = 52,
            ForeColor = TextMuted,
            Padding = new Padding(0, 12, 0, 0)
        };
    }

    private async Task ReconnectAsync()
    {
        AgentDiagnosticLog.Write("Reconnect requested from settings window");
        await runtime.ReconnectAsync();
        ShowPage(currentPage);
    }

    private async Task RestartDeviceAsync()
    {
        AgentDiagnosticLog.Write("Device restart requested from settings window");
        var sent = await runtime.SendDeviceRestartAsync();
        MessageBox.Show(
            this,
            sent ? "Restart command sent." : "No connected device is available.",
            "PC Dock",
            MessageBoxButtons.OK,
            sent ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private async Task FactoryResetDeviceAsync()
    {
        var result = MessageBox.Show(
            this,
            "Factory Reset will remove Wi-Fi, pairing and device settings from the CYD. Continue?",
            "Factory Reset",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
        {
            AgentDiagnosticLog.Write("Device factory reset cancelled in settings window");
            return;
        }

        AgentDiagnosticLog.Write("Device factory reset confirmed in settings window");
        var sent = await runtime.SendDeviceFactoryResetAsync();
        MessageBox.Show(
            this,
            sent ? "Factory Reset command sent." : "No connected device is available.",
            "PC Dock",
            MessageBoxButtons.OK,
            sent ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private async Task ForgetDeviceAsync()
    {
        if (string.IsNullOrWhiteSpace(state.Snapshot().DeviceId))
        {
            MessageBox.Show(
                this,
                "No connected device is available to forget.",
                "PC Dock",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var result = MessageBox.Show(
            this,
            "Forget this CYD on this PC? It will need to be approved again before it can control the agent.",
            "Forget device",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
        {
            AgentDiagnosticLog.Write("Forget device cancelled in settings window");
            return;
        }

        try
        {
            var forgotten = await runtime.ForgetConnectedDeviceAsync().ConfigureAwait(true);
            MessageBox.Show(
                this,
                forgotten ? "Device forgotten. Reconnect it to approve pairing again." : "No active pairing record was found for the connected device.",
                "PC Dock",
                MessageBoxButtons.OK,
                forgotten ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            ShowPage(currentPage);
        }
        catch (Exception exception)
        {
            AgentDiagnosticLog.Write($"Forget device failed in settings window: {exception.GetType().Name}");
            MessageBox.Show(
                this,
                "The device could not be forgotten. Try again from Settings.",
                "PC Dock",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task InstallFirmwareFromFileAsync()
    {
        var snapshot = state.Snapshot();
        if (snapshot.DeviceInfo?.Wifi.Ip is not { Length: > 0 } deviceIp || string.IsNullOrWhiteSpace(snapshot.DeviceId))
        {
            MessageBox.Show(
                this,
                "No authenticated CYD with a reported IP address is available for firmware update.",
                "Firmware update",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        using var picker = new OpenFileDialog
        {
            Title = "Select firmware image",
            Filter = "Firmware images (*.bin)|*.bin|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var preflightError = await ValidateFirmwareFilePreflightAsync(picker.FileName).ConfigureAwait(true);
        if (preflightError is not null)
        {
            MessageBox.Show(
                this,
                preflightError,
                "Firmware update",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            "Install this firmware image on the connected CYD? The update requires the device setup password.",
            "Install firmware",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (confirm != DialogResult.Yes)
        {
            AgentDiagnosticLog.Write("Firmware update cancelled before password prompt");
            return;
        }

        using var dialog = new FirmwareUpdateDialog(state, picker.FileName, deviceIp, snapshot.DeviceInfo?.FirmwareVersion);
        dialog.ShowDialog(this);
        await Task.CompletedTask;
        ShowPage(currentPage);
    }

    private static async Task<string?> ValidateFirmwareFilePreflightAsync(string firmwarePath)
    {
        const byte EspFirmwareMagic = 0xE9;

        try
        {
            var file = new FileInfo(firmwarePath);
            if (!file.Exists)
            {
                return "Firmware image could not be opened.";
            }

            if (file.Length <= 0)
            {
                return "Firmware image is empty.";
            }

            if (file.Length > ArduinoOtaFirmwareUpdater.MaxFirmwareBytes)
            {
                return "Firmware image is too large for this device.";
            }

            await using var stream = File.OpenRead(firmwarePath);
            var firstByte = stream.ReadByte();
            if (firstByte != EspFirmwareMagic)
            {
                return "Firmware image is not a valid ESP firmware image.";
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            AgentDiagnosticLog.Write($"Firmware preflight failed in settings window: {exception.GetType().Name}");
            return "Firmware image could not be opened.";
        }
    }

    private void OpenLogFolder()
    {
        Directory.CreateDirectory(AgentDiagnosticLog.GetLogDirectory());
        Process.Start(new ProcessStartInfo
        {
            FileName = AgentDiagnosticLog.GetLogDirectory(),
            UseShellExecute = true
        });
    }

    private AgentConfiguration LoadConfiguration()
    {
        try
        {
            return configurationStore.LoadOrDefault();
        }
        catch (Exception exception)
        {
            AgentDiagnosticLog.Write($"Configuration load failed in settings window: {exception.GetType().Name}");
            return AgentConfiguration.CreateDefault();
        }
    }

    internal static ShortcutConfigurationLoadState LoadShortcutConfigurationState(Func<AgentConfiguration> loadConfiguration)
    {
        try
        {
            return new ShortcutConfigurationLoadState(loadConfiguration(), true, string.Empty);
        }
        catch (Exception exception)
        {
            AgentDiagnosticLog.Write($"Configuration load failed in shortcuts page: {exception.GetType().Name}");
            return new ShortcutConfigurationLoadState(AgentConfiguration.CreateDefault(), false, exception.GetType().Name);
        }
    }

    internal static bool CanWriteShortcutsAfterLoad(ShortcutConfigurationLoadState state) => state.CanWriteShortcuts;

    internal sealed record ShortcutConfigurationLoadState(AgentConfiguration Configuration, bool CanWriteShortcuts, string ErrorType);

    private bool EnsureShortcutsCanWrite()
    {
        if (!shortcutsConfigurationLoadFailed)
        {
            return true;
        }

        MessageBox.Show(
            this,
            "Existing Agent configuration could not be loaded. Shortcut edits are disabled to avoid overwriting it with defaults. Recover the config file or use Import full config first.",
            "Shortcuts unavailable",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        return false;
    }

    private async Task AddShortcutAsync(ListView list, List<ShortcutDefinition> shortcuts, Label emptyState, ShortcutConfigurationLoadState loadResult)
    {
        if (!EnsureShortcutsCanWrite())
        {
            return;
        }

        using var dialog = new ShortcutEditorDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        shortcuts.Add(dialog.Shortcut);
        if (await SaveShortcutsAsync(shortcuts).ConfigureAwait(true))
        {
            AddShortcutItem(list, dialog.Shortcut);
            list.Items[^1].Selected = true;
            UpdateShortcutsEmptyState(list, emptyState, loadResult);
        }
    }

    private async Task EditShortcutAsync(ListView list, List<ShortcutDefinition> shortcuts)
    {
        if (!EnsureShortcutsCanWrite())
        {
            return;
        }

        var index = GetSelectedShortcutIndex(list);
        if (index < 0)
        {
            return;
        }

        using var dialog = new ShortcutEditorDialog(shortcuts[index]);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        shortcuts[index] = dialog.Shortcut;
        if (await SaveShortcutsAsync(shortcuts).ConfigureAwait(true))
        {
            UpdateShortcutItem(list.Items[index], dialog.Shortcut);
        }
    }

    private async Task DeleteShortcutAsync(ListView list, List<ShortcutDefinition> shortcuts, Label emptyState, ShortcutConfigurationLoadState loadResult)
    {
        if (!EnsureShortcutsCanWrite())
        {
            return;
        }

        var index = GetSelectedShortcutIndex(list);
        if (index < 0)
        {
            return;
        }

        var result = MessageBox.Show(this, "Delete selected shortcut?", "PC Dock", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes)
        {
            return;
        }

        shortcuts.RemoveAt(index);
        if (await SaveShortcutsAsync(shortcuts).ConfigureAwait(true))
        {
            list.Items.RemoveAt(index);
            UpdateShortcutsEmptyState(list, emptyState, loadResult);
        }
    }

    private async Task MoveShortcutAsync(ListView list, List<ShortcutDefinition> shortcuts, int offset)
    {
        if (!EnsureShortcutsCanWrite())
        {
            return;
        }

        var index = GetSelectedShortcutIndex(list);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= shortcuts.Count)
        {
            return;
        }

        (shortcuts[index], shortcuts[target]) = (shortcuts[target], shortcuts[index]);
        if (await SaveShortcutsAsync(shortcuts).ConfigureAwait(true))
        {
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (var shortcut in shortcuts)
                {
                    AddShortcutItem(list, shortcut);
                }

                list.Items[target].Selected = true;
            }
            finally
            {
                list.EndUpdate();
            }
        }
    }

    private async Task<bool> SaveShortcutsAsync(IReadOnlyList<ShortcutDefinition> shortcuts)
    {
        if (!EnsureShortcutsCanWrite())
        {
            return false;
        }

        var configuration = LoadConfiguration() with { Shortcuts = shortcuts.ToArray() };
        if (!TrySaveConfiguration(configuration, "Shortcuts updated"))
        {
            return false;
        }

        await runtime.ReconnectAsync();
        return true;
    }

    private async Task ImportConfigurationAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import PC Dock configuration",
            Filter = "PC Dock configuration (*.json)|*.json|JSON files (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var configuration = ConfigurationExportService.Import(await File.ReadAllTextAsync(dialog.FileName));
            if (TrySaveConfiguration(configuration, $"Configuration imported from {Path.GetFileName(dialog.FileName)}"))
            {
                await runtime.ReconnectAsync();
                ShowPage(currentPage);
            }
        }
        catch (AgentConfigurationException exception)
        {
            ShowConfigurationIssues("Import failed", exception.Issues);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ExportConfigurationAsync()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export PC Dock configuration",
            FileName = "pc-dock-config.json",
            Filter = "PC Dock configuration (*.json)|*.json|JSON files (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await File.WriteAllTextAsync(dialog.FileName, ConfigurationExportService.Export(LoadConfiguration()));
        AgentDiagnosticLog.Write($"Configuration exported to {Path.GetFileName(dialog.FileName)}");
    }

    private bool TrySaveConfiguration(AgentConfiguration configuration, string logMessage)
    {
        try
        {
            configurationStore.Save(configuration);
            AgentDiagnosticLog.Write(logMessage);
            return true;
        }
        catch (AgentConfigurationException exception)
        {
            ShowConfigurationIssues("Configuration is invalid", exception.Issues);
            return false;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Configuration save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private async Task FlushAppearanceChangesAsync()
    {
        appearanceSaveTimer.Stop();
        var settings = pendingAppearance;
        if (settings is null)
        {
            return;
        }

        try
        {
            SaveAppearanceConfiguration(settings.EnsureInfoVisible());
        }
        catch (Exception exception)
        {
            AgentDiagnosticLog.Write($"Appearance configuration save failed: {exception.GetType().Name}");
            if (appearanceStatusLabel is not null)
            {
                appearanceStatusLabel.Text = "Settings could not be saved locally.";
                appearanceStatusLabel.ForeColor = Color.LightSalmon;
            }

            return;
        }

        var applied = await TryApplyAppearanceAsync(settings.EnsureInfoVisible()).ConfigureAwait(true);
        if (appearanceStatusLabel is not null)
        {
            appearanceStatusLabel.ForeColor = applied ? TextMuted : Color.Khaki;
            appearanceStatusLabel.Text = applied
                ? "Saved and sent to the connected device."
                : "Saved locally. Connect the device to apply these settings.";
        }
    }

    private AppearanceState LoadAppearanceConfiguration()
    {
        var defaults = AppearanceState.Default;
        try
        {
            return AppearanceState.FromConfiguration(LoadConfiguration().Appearance ?? new AppearanceConfiguration()).EnsureInfoVisible();
        }
        catch (Exception exception)
        {
            AgentDiagnosticLog.Write($"Appearance configuration load failed: {exception.GetType().Name}");
            return defaults;
        }
    }

    private void SaveAppearanceConfiguration(AppearanceState settings)
    {
        configurationStore.Save(LoadConfiguration() with { Appearance = settings.ToConfiguration() });
        AgentDiagnosticLog.Write("Appearance settings updated");
    }

    private async Task<bool> TryApplyAppearanceAsync(AppearanceState settings)
    {
        try
        {
            return await runtime.ApplyDeviceSettingsAsync(settings.ToConfiguration()).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            AgentDiagnosticLog.Write($"Appearance settings device apply failed: {exception.GetType().Name}");
            return false;
        }
    }

    private static Color ParseHexColor(string value, Color fallback)
    {
        return IsHexColor(value) ? ColorTranslator.FromHtml(value) : fallback;
    }

    private static string ToHexColor(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static bool IsHexColor(string value)
    {
        return value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
    }

    private void ShowConfigurationIssues(string title, IReadOnlyList<ConfigurationValidationIssue> issues)
    {
        var message = string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Path}: {issue.Message}"));
        MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static int GetSelectedShortcutIndex(ListView list)
    {
        return list.SelectedIndices.Count == 0 ? -1 : list.SelectedIndices[0];
    }

    private static void AddShortcutItem(ListView list, ShortcutDefinition shortcut)
    {
        list.Items.Add(CreateShortcutItem(shortcut));
    }

    private static ListViewItem CreateShortcutItem(ShortcutDefinition shortcut)
    {
        var target = shortcut.Action.Url
            ?? shortcut.Action.ExecutablePath
            ?? shortcut.Action.ScriptPath
            ?? shortcut.Action.AdapterKey
            ?? string.Join(" + ", shortcut.Action.Keys);
        return new ListViewItem([shortcut.Name, shortcut.Id, shortcut.Action.Type, target]);
    }

    private static void UpdateShortcutItem(ListViewItem item, ShortcutDefinition shortcut)
    {
        var updated = CreateShortcutItem(shortcut);
        for (var i = 0; i < item.SubItems.Count; i++)
        {
            item.SubItems[i].Text = updated.SubItems[i].Text;
        }
    }

    private void HandleStateChanged(object? sender, EventArgs args)
    {
        if (IsDisposed)
        {
            return;
        }

        if (!IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            if (TryQueueLiveRefresh(ref liveRefreshQueued))
            {
                BeginInvoke((Action)(() =>
                {
                    MarkLiveRefreshCompleted(ref liveRefreshQueued);
                    RefreshLiveInfoValues();
                }));
            }

            return;
        }

        MarkLiveRefreshCompleted(ref liveRefreshQueued);
        RefreshLiveInfoValues();
    }

    private void RefreshLiveInfoValues()
    {
        if (IsDisposed)
        {
            return;
        }

        if (ShouldRebuildPageOnStateChanged(currentPage))
        {
            ShowPage(currentPage);
            return;
        }

        foreach (var (label, value) in CreateLiveInfoValues(currentPage, state.Snapshot()))
        {
            if (infoValueLabels.TryGetValue(label, out var valueLabel) && !valueLabel.IsDisposed)
            {
                valueLabel.Text = value;
            }
        }

        if (string.Equals(currentPage, "Device", StringComparison.Ordinal))
        {
            var hasActiveDevice = !string.IsNullOrWhiteSpace(state.Snapshot().DeviceId);
            foreach (var button in FindControls<Button>(contentPanel))
            {
                if (button.Text is "Forget device" or "Forget device unavailable")
                {
                    button.Enabled = hasActiveDevice;
                    button.Text = hasActiveDevice ? "Forget device" : "Forget device unavailable";
                    button.Cursor = hasActiveDevice ? Cursors.Hand : Cursors.Default;
                }
            }
        }
    }

    internal static bool ShouldRebuildPageOnStateChanged(string page)
    {
        return string.Equals(page, "Pairing", StringComparison.Ordinal);
    }

    internal static bool TryQueueLiveRefresh(ref int queued)
    {
        return Interlocked.Exchange(ref queued, 1) == 0;
    }

    internal static void MarkLiveRefreshCompleted(ref int queued)
    {
        Interlocked.Exchange(ref queued, 0);
    }

    internal static IReadOnlyDictionary<string, string> CreateLiveInfoValues(string page, AgentStatusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var deviceInfo = snapshot.DeviceInfo;

        if (string.Equals(page, "Overview", StringComparison.Ordinal))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CYD"] = AgentStatusFormatter.ToDeviceStatus(snapshot),
                ["Firmware"] = FormatUnavailable(deviceInfo?.FirmwareVersion),
                ["Wi-Fi"] = FormatWifi(deviceInfo?.Wifi),
                ["SSID"] = FormatUnavailable(deviceInfo?.Wifi.Ssid),
                ["RSSI"] = FormatRssi(deviceInfo?.Wifi.Rssi),
                ["Device IP"] = FormatUnavailable(deviceInfo?.Wifi.Ip),
                ["Agent host"] = FormatUnavailable(deviceInfo?.AgentHost),
                ["Uptime"] = FormatUptime(deviceInfo?.UptimeSeconds),
                ["WebSocket"] = AgentStatusFormatter.ToUserStatus(snapshot.HostStatus),
                ["Last seen"] = AgentStatusFormatter.FormatLastSeen(snapshot),
                ["Mode"] = snapshot.Mode,
                ["LAN IP"] = snapshot.LanAddresses.Count == 0 ? "Unavailable" : string.Join(", ", snapshot.LanAddresses),
                ["Port"] = snapshot.HostPort.ToString()
            };
        }

        if (string.Equals(page, "Device", StringComparison.Ordinal))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Device name"] = snapshot.DeviceName ?? "Unavailable",
                ["Device ID"] = snapshot.DeviceId ?? "Unavailable",
                ["Status"] = AgentStatusFormatter.ToDeviceStatus(snapshot),
                ["Firmware"] = FormatUnavailable(deviceInfo?.FirmwareVersion),
                ["Uptime"] = FormatUptime(deviceInfo?.UptimeSeconds),
                ["Wi-Fi"] = FormatWifi(deviceInfo?.Wifi),
                ["SSID"] = FormatUnavailable(deviceInfo?.Wifi.Ssid),
                ["RSSI"] = FormatRssi(deviceInfo?.Wifi.Rssi),
                ["IP"] = FormatUnavailable(deviceInfo?.Wifi.Ip),
                ["Agent host"] = FormatUnavailable(deviceInfo?.AgentHost),
                ["SD"] = FormatSdState(deviceInfo?.Sd),
                ["SD capacity"] = FormatBytes(deviceInfo?.Sd.CapacityBytes),
                ["SD free"] = FormatBytes(deviceInfo?.Sd.FreeBytes),
                ["Heap free"] = FormatBytes(deviceInfo?.Heap.FreeBytes),
                ["Heap min free"] = FormatBytes(deviceInfo?.Heap.MinFreeBytes),
                ["Heap largest block"] = FormatBytes(deviceInfo?.Heap.LargestBlockBytes),
                ["Remote IP"] = FormatRemoteHost(snapshot.RemoteAddress),
                ["Remote endpoint"] = snapshot.RemoteAddress ?? "Unavailable",
                ["WebSocket state"] = AgentStatusFormatter.ToUserStatus(snapshot.HostStatus),
                ["Last seen"] = AgentStatusFormatter.FormatLastSeen(snapshot),
                ["Agent port"] = snapshot.HostPort.ToString(),
                ["Dashboard port"] = snapshot.DashboardPort == 0 ? "Unavailable" : snapshot.DashboardPort.ToString()
            };
        }

        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static IEnumerable<TControl> FindControls<TControl>(Control root)
        where TControl : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is TControl match)
            {
                yield return match;
            }

            foreach (var descendant in FindControls<TControl>(child))
            {
                yield return descendant;
            }
        }
    }

    private void HandlePairingChanged(object? sender, EventArgs args)
    {
        if (!string.Equals(currentPage, "Pairing", StringComparison.Ordinal))
        {
            return;
        }

        HandleStateChanged(sender, args);
    }

    private static string FormatRemoteHost(string? remoteAddress)
    {
        if (string.IsNullOrWhiteSpace(remoteAddress))
        {
            return "Unavailable";
        }

        var colon = remoteAddress.LastIndexOf(':');
        return colon > 0 ? remoteAddress[..colon] : remoteAddress;
    }

    private static string FormatUnavailable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Unavailable" : value;
    }

    private static string FormatWifi(DeviceWifiInfo? wifi)
    {
        return wifi is null ? "Unavailable" : (wifi.Connected ? "Connected" : "Disconnected");
    }

    private static string FormatRssi(int? rssi)
    {
        return rssi.HasValue ? $"{rssi.Value} dBm" : "Unavailable";
    }

    private static string FormatSdState(DeviceStorageInfo? storage)
    {
        if (storage is null)
        {
            return "Unavailable";
        }

        if (!storage.Mounted)
        {
            return "Not mounted";
        }

        return storage.Ready ? "Ready" : "Mounted";
    }

    private static string FormatUptime(long? seconds)
    {
        if (!seconds.HasValue)
        {
            return "Unavailable";
        }

        var uptime = TimeSpan.FromSeconds(Math.Max(0, seconds.Value));
        var days = (int)uptime.TotalDays;
        return days > 0
            ? $"{days}d {uptime.Hours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}"
            : $"{uptime.Hours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}";
    }

    private static string FormatBytes(long? bytes)
    {
        if (!bytes.HasValue)
        {
            return "Unavailable";
        }

        var value = Math.Max(0, bytes.Value);
        string[] units = ["B", "KB", "MB", "GB"];
        var scaled = (double)value;
        var unit = 0;
        while (scaled >= 1024 && unit < units.Length - 1)
        {
            scaled /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value} {units[unit]}" : $"{scaled:0.##} {units[unit]}";
    }

    private static string GetAgentVersion()
    {
        return Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "Unavailable";
    }

    private sealed class AppearanceState
    {
        public static AppearanceState Default => new()
        {
            Theme = "dark",
            Accent = "#D43B48",
            Background = "#101216",
            ButtonStyle = "rounded",
            PageOrder = ["media", "pc", "shortcuts", "info"],
            PageVisibility = ["media", "pc", "shortcuts", "info"],
            Brightness = 70,
            DimAfterSec = 300,
            DimBrightness = 20,
            ScreenOffAfterSec = 1800,
            WakeOnTouch = true
        };

        public string Theme { get; set; } = "dark";
        public string Accent { get; set; } = "#D43B48";
        public string Background { get; set; } = "#101216";
        public string ButtonStyle { get; set; } = "rounded";
        public string[] PageOrder { get; set; } = [];
        public string[] PageVisibility { get; set; } = [];
        public int Brightness { get; set; }
        public int DimAfterSec { get; set; }
        public int DimBrightness { get; set; }
        public int ScreenOffAfterSec { get; set; }
        public bool WakeOnTouch { get; set; }

        public static AppearanceState FromConfiguration(AppearanceConfiguration configuration)
        {
            return new AppearanceState
            {
                Theme = configuration.Theme,
                Accent = configuration.Accent,
                Background = configuration.Background,
                ButtonStyle = configuration.ButtonStyle,
                PageOrder = configuration.PageOrder.ToArray(),
                PageVisibility = configuration.PageVisibility.ToArray(),
                Brightness = configuration.Brightness,
                DimAfterSec = configuration.DimAfterSec,
                DimBrightness = configuration.DimBrightness,
                ScreenOffAfterSec = configuration.ScreenOffAfterSec,
                WakeOnTouch = configuration.WakeOnTouch
            };
        }

        public AppearanceState EnsureInfoVisible()
        {
            if (!PageOrder.Contains("info", StringComparer.Ordinal))
            {
                PageOrder = [..PageOrder, "info"];
            }

            if (!PageVisibility.Contains("info", StringComparer.Ordinal))
            {
                PageVisibility = [..PageVisibility, "info"];
            }

            return this;
        }

        public AppearanceConfiguration ToConfiguration()
        {
            return new AppearanceConfiguration
            {
                Theme = Theme,
                Accent = Accent,
                Background = Background,
                ButtonStyle = ButtonStyle,
                PageOrder = PageOrder,
                PageVisibility = PageVisibility,
                Brightness = Brightness,
                DimAfterSec = DimAfterSec,
                DimBrightness = DimBrightness,
                ScreenOffAfterSec = ScreenOffAfterSec,
                WakeOnTouch = WakeOnTouch
            };
        }
    }
}
