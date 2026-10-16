using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Reflection;
using Microsoft.Win32;
using StreamDeck.Host;
using StreamDeck.Configuration;
using StreamDeck.Media;
using StreamDeck.Protocol;
using StreamDeck.Shortcuts;
using StreamDeck.WindowsAgent.Runtime;
using StreamDeck.WindowsAgent.Updates;
using ShortcutDefinition = StreamDeck.Shortcuts.ShortcutDefinition;

namespace StreamDeck.WindowsAgent.Shell;

public sealed class SettingsWindow : Form
{
    internal const string AppDisplayName = "CYD-DECK";
    private static readonly Color DarkBackground = Color.FromArgb(13, 15, 20);
    private static readonly Color LightBackground = Color.FromArgb(246, 247, 249);
    private static readonly Color DarkSurface = Color.FromArgb(22, 24, 29);
    private static readonly Color LightSurface = Color.FromArgb(255, 255, 255);
    private static readonly Color DarkSurfaceAlt = Color.FromArgb(30, 32, 40);
    private static readonly Color LightSurfaceAlt = Color.FromArgb(236, 238, 242);
    private static readonly Color DarkBorder = Color.FromArgb(46, 50, 62);
    private static readonly Color LightBorder = Color.FromArgb(210, 214, 220);
    private static readonly Color DarkTextPrimary = Color.FromArgb(244, 246, 248);
    private static readonly Color LightTextPrimary = Color.FromArgb(29, 32, 37);
    private static readonly Color DarkTextMuted = Color.FromArgb(156, 164, 176);
    private static readonly Color LightTextMuted = Color.FromArgb(93, 101, 112);
    private static readonly Color Accent = Color.FromArgb(82, 153, 224);
    private static readonly Color Danger = Color.FromArgb(255, 69, 58);
    private static readonly Color AppleGreen = Color.FromArgb(48, 209, 88);
    private static readonly Color AppleAmber = Color.FromArgb(255, 159, 10);
    private static readonly Color ApplePurple = Color.FromArgb(191, 90, 242);
    private static readonly Color AppleCyan = Color.FromArgb(100, 210, 255);
    private static readonly Color DeviceDefaultAccent = Color.FromArgb(126, 175, 227);
    private static readonly Color DeviceDefaultBackground = Color.FromArgb(16, 18, 22);
    private static readonly Padding WindowContentPadding = new(34, 30, 34, 30);
    private const int NavigationWidth = 252;
    private const int NavigationButtonWidth = 212;
    private const int NavigationButtonHeight = 42;
    private const int PageTitleHeight = 68;
    private const int ActionButtonHeight = 36;
    private const int ActionButtonMinimumWidth = 128;
    private const int ActionButtonHorizontalMargin = 10;
    private const int ActionButtonVerticalMargin = 10;
    private const int MinimumWindowWidth = 900;
    private const int MinimumWindowHeight = 620;
    private const int AppearanceMinimumSliderTrackWidth = 150;
    private const int AppearanceMaximumSliderTrackWidth = 300;
    private const int ColorSwatchSize = 26;
    private const int ColorPickerRowHeight = 40;
    private const int ListVerticalScrollbarAllowance = 24;
    private const int ShortcutsListScrollbarAllowance = 48;
    private const string PageBodyTag = "settings-page-body";
    private const string PageFooterTag = "settings-page-footer";
    private const string DeviceColorPreviewTag = "device-color-preview";
    private const string DeviceColorPickerTag = "device-color-picker";
    private static readonly string[] DefaultAppearancePageOrder = ["media", "shortcuts", "pc", "info"];
    private static readonly HashSet<string> ForcedVisibleAppearancePages = new(["info"], StringComparer.Ordinal);
    internal const string ShortcutsSourceOfTruthText = "Select a shortcut to edit, test or move it. The first six appear on CYD.";
    internal const string EmptyShortcutsText = "Add your first shortcut to open an app, website or keyboard shortcut from CYD.";
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
    private static int MinimumSettingsClientWidth => MinimumWindowWidth - NavigationWidth - WindowContentPadding.Horizontal;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out Rectangle rectangle);

    private sealed class ThemedHeaderWindow(ListView owner) : NativeWindow
    {
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg is not (0x000F or 0x0317 or 0x0318) || !GetClientRect(Handle, out var bounds)) return;
            var start = owner.Columns.Cast<ColumnHeader>().Sum(column => column.Width);
            if (start >= bounds.Width) return;
            using var graphics = message.Msg == 0x000F || message.WParam == IntPtr.Zero
                ? Graphics.FromHwnd(Handle) : Graphics.FromHdc(message.WParam);
            using var brush = new SolidBrush(SurfaceAlt);
            graphics.FillRectangle(brush, start, 0, bounds.Width - start, bounds.Height);
        }
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? pszSubAppName, string? pszSubIdList);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateTitleBarTheme();
    }

    private void UpdateTitleBarTheme()
    {
        if (!IsHandleCreated) return;
        var dark = clientLightTheme ? 0 : 1;
        _ = DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, [MarshalAs(UnmanagedType.Bool)] bool bShow);

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
    private DesktopAppearanceState? pendingDesktopAppearance;
    private Label? appearanceStatusLabel;
    private Panel? sidebarConnectionPill;
    private Label? sidebarConnectionLabel;
    private Action? refreshOverviewWidgets;
    private bool shortcutsConfigurationLoadFailed;
    private string shortcutImportSignature = string.Empty;
    private int liveRefreshQueued;
    private bool deviceTelemetryVisible;
    private string? updateDeviceIp;
    private Action? refreshPairingLists;

    public SettingsWindow(
        AgentState state,
        WindowsAgentRuntime runtime,
        LocalDashboardServer dashboard,
        WindowsStartupService startup,
        IPairingApprovalService pairing,
        Icon shellIcon,
        AgentConfigurationStore? configurationOverride = null)
    {
        this.state = state;
        this.runtime = runtime;
        this.dashboard = dashboard;
        this.startup = startup;
        this.pairing = pairing;
        configurationStore = configurationOverride ?? AgentConfigurationStoreFactory.CreateForCurrentUser();
        appearanceSaveTimer.Tick += async (_, _) => await FlushAppearanceChangesAsync().ConfigureAwait(true);
        shortcutImportRefreshTimer.Tick += (_, _) => RefreshShortcutImportsIfChanged();
        var initialDesktopAppearance = LoadDesktopAppearanceConfiguration();
        var effectiveDesktopTheme = ResolveEffectiveDesktopTheme(initialDesktopAppearance.Theme);
        ApplyClientTheme(effectiveDesktopTheme, initialDesktopAppearance.Accent, null);
        contentPanel = CreateThemedContentPanel(effectiveDesktopTheme);

        Text = AppDisplayName;
        Icon = shellIcon;
        DoubleBuffered = true;
        MinimumSize = new Size(MinimumWindowWidth, MinimumWindowHeight);
        Size = new Size(1080, 740);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        AccessibleName = "CYD-DECK settings";
        AccessibleDescription = "Configure the local Windows Agent, connected CYD device, appearance and Agent-side shortcuts.";

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

        // STARTUPINFO can hide the native HWND even though WinForms Visible is true.
        // Explicit Open/activate must restore that native visibility as well.
        _ = ShowWindow(Handle, 5); // SW_SHOW preserves an existing maximized window.
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
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(20, 24, 20, 18)
        };
        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(Border, 1f);
            e.Graphics.DrawLine(pen, panel.Width - 1, 0, panel.Width - 1, panel.Height);
        };

        var brand = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Surface };
        var title = new Label
        {
            Text = AppDisplayName,
            Dock = DockStyle.Top,
            Height = 32,
            Font = new Font(Font.FontFamily, 17F, FontStyle.Bold),
            ForeColor = TextPrimary
        };
        var subtitle = new Label
        {
            Text = $"Control Center  •  {DateTime.Now:HH:mm}",
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font(Font.FontFamily, 8.75F, FontStyle.Regular),
            ForeColor = TextMuted
        };
        brand.Controls.Add(subtitle);
        brand.Controls.Add(title);

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 14, 0, 0),
            BackColor = Surface
        };

        var workspaceHeader = new Label
        {
            Text = "WORKSPACE",
            Font = new Font(Font.FontFamily, 7.5F, FontStyle.Bold),
            ForeColor = TextMuted,
            Width = NavigationButtonWidth,
            Height = 20,
            Margin = new Padding(8, 0, 0, 4),
            TextAlign = ContentAlignment.MiddleLeft
        };
        nav.Controls.Add(workspaceHeader);

        foreach (var page in new[] { "Overview", "Shortcuts", "Appearance", "Device" })
        {
            var button = CreateNavButton(page);
            navButtons.Add(page, button);
            nav.Controls.Add(button);
        }

        var sectionLabel = new Label
        {
            Text = "MAINTENANCE",
            Font = new Font(Font.FontFamily, 7.5F, FontStyle.Bold),
            ForeColor = TextMuted,
            Width = NavigationButtonWidth,
            Height = 22,
            Margin = new Padding(8, 12, 0, 4),
            TextAlign = ContentAlignment.MiddleLeft
        };
        nav.Controls.Add(sectionLabel);

        foreach (var page in new[] { "Pairing", "Updates", "Logs" })
        {
            var button = CreateNavButton(page);
            navButtons.Add(page, button);
            nav.Controls.Add(button);
        }

        sidebarConnectionPill = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            BackColor = SurfaceAlt,
            Padding = new Padding(28, 0, 10, 0),
            Tag = DeviceColorPreviewTag
        };
        sidebarConnectionLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = FormatSidebarConnectionStatus(state.Snapshot()),
            Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold),
            ForeColor = TextPrimary,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            AccessibleName = "Sidebar connection status"
        };
        sidebarConnectionPill.Controls.Add(sidebarConnectionLabel);
        sidebarConnectionPill.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var snap = state.Snapshot();
            var isConnected = string.Equals(snap.HostStatus, "connected", StringComparison.OrdinalIgnoreCase);
            var isPending = string.Equals(snap.HostStatus, "pairing pending", StringComparison.OrdinalIgnoreCase);
            var dotColor = isConnected ? AppleGreen : (isPending ? AppleAmber : TextMuted);
            var pillBg = clientLightTheme ? Color.FromArgb(234, 237, 243) : Color.FromArgb(28, 31, 39);
            var pillBorder = isConnected ? Color.FromArgb(90, AppleGreen) : Border;

            using var path = CreateRoundedRectPath(new Rectangle(0, 0, sidebarConnectionPill.Width - 1, sidebarConnectionPill.Height - 1), 10);
            using var bgBrush = new SolidBrush(pillBg);
            using var borderPen = new Pen(pillBorder, 1f);
            e.Graphics.FillPath(bgBrush, path);
            e.Graphics.DrawPath(borderPen, path);

            using var glowBrush = new SolidBrush(Color.FromArgb(55, dotColor));
            using var dotBrush = new SolidBrush(dotColor);
            e.Graphics.FillEllipse(glowBrush, 9, 13, 14, 14);
            e.Graphics.FillEllipse(dotBrush, 12, 16, 8, 8);
        };

        panel.Controls.Add(nav);
        panel.Controls.Add(sidebarConnectionPill);
        panel.Controls.Add(brand);
        return panel;
    }

    internal static string FormatSidebarConnectionStatus(AgentStatusSnapshot snapshot)
    {
        var clock = DateTime.Now.ToString("HH:mm");
        if (string.Equals(snapshot.HostStatus, "connected", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(snapshot.RemoteAddress, "USB", StringComparison.OrdinalIgnoreCase))
            {
                var ch340 = UsbSerialPortDiscovery.FindConnectedCh340Ports().FirstOrDefault();
                return !string.IsNullOrWhiteSpace(ch340) ? $"Connected {ch340} • {clock}" : $"Connected USB • {clock}";
            }

            var host = FormatRemoteHost(snapshot.RemoteAddress);
            return host != "Unavailable" ? $"Connected {host} • {clock}" : $"Connected Wi-Fi • {clock}";
        }

        if (string.Equals(snapshot.HostStatus, "pairing pending", StringComparison.OrdinalIgnoreCase))
        {
            return $"Pairing Pending • {clock}";
        }

        return $"Listening :{snapshot.HostPort} • {clock}";
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
            Padding = new Padding(38, 0, 12, 0),
            Margin = new Padding(0, 0, 0, 6),
            BackColor = Surface,
            ForeColor = TextMuted,
            Font = new Font(Font.FontFamily, 9.75F, FontStyle.Regular),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => ShowPage(page);
        button.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var selected = string.Equals(currentPage, page, StringComparison.Ordinal);
            var bg = selected
                ? (clientLightTheme ? Color.FromArgb(226, 234, 246) : Color.FromArgb(34, 40, 52))
                : button.BackColor;
            using var clearBrush = new SolidBrush(Surface);
            e.Graphics.FillRectangle(clearBrush, button.ClientRectangle);

            var pillRect = new Rectangle(1, 2, button.Width - 3, button.Height - 5);
            using var pillPath = CreateRoundedRectPath(pillRect, 9);
            using var pillBrush = new SolidBrush(bg);
            e.Graphics.FillPath(pillBrush, pillPath);

            if (selected)
            {
                using var borderPen = new Pen(Color.FromArgb(110, Accent), 1f);
                e.Graphics.DrawPath(borderPen, pillPath);
                using var indicatorBrush = new SolidBrush(Accent);
                using var indicatorPath = CreateRoundedRectPath(new Rectangle(5, 11, 3, button.Height - 22), 2);
                e.Graphics.FillPath(indicatorBrush, indicatorPath);
            }

            var iconColor = selected ? Accent : TextMuted;
            DrawNavigationVectorIcon(e.Graphics, page, new Rectangle(15, (button.Height - 16) / 2, 16, 16), iconColor);

            var textColor = selected ? TextPrimary : TextMuted;
            var textRect = new Rectangle(40, 0, button.Width - 48, button.Height);
            TextRenderer.DrawText(
                e.Graphics,
                page,
                button.Font,
                textRect,
                textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
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
            button.Font = new Font(Font.FontFamily, 9.75F, selected ? FontStyle.Bold : FontStyle.Regular);
            button.FlatAppearance.BorderColor = selected ? Accent : Surface;
            button.FlatAppearance.BorderSize = 0;
            button.Invalidate();
        }

        UpdateSidebarConnectionPill();

        foreach (Control control in contentPanel.Controls.Cast<Control>().ToArray())
        {
            control.Dispose();
        }

        contentPanel.Controls.Clear();
        infoValueLabels.Clear();
        refreshOverviewWidgets = null;
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

    private void UpdateSidebarConnectionPill()
    {
        if (sidebarConnectionLabel is not null && !sidebarConnectionLabel.IsDisposed)
        {
            sidebarConnectionLabel.Text = FormatSidebarConnectionStatus(state.Snapshot());
            sidebarConnectionLabel.ForeColor = TextPrimary;
        }

        if (sidebarConnectionPill is not null && !sidebarConnectionPill.IsDisposed)
        {
            sidebarConnectionPill.Invalidate();
        }
    }

    private Control CreateOverviewPage()
    {
        var snapshot = state.Snapshot();
        var panel = CreatePage("Overview");
        var grid = CreateInfoGrid();
        var deviceInfo = snapshot.HostStatus == "connected" ? snapshot.DeviceInfo : null;

        AddConnectionSummary(grid, snapshot);

        refreshOverviewWidgets = null;
        var mediaWidget = CreateAppleMediaControlWidget();
        mediaWidget.Margin = new Padding(0, 0, 0, 14);
        grid.Controls.Add(mediaWidget, 0, grid.RowCount++);
        grid.SetColumnSpan(mediaWidget, 2);

        var systemWidget = CreateAppleSystemMonitorWidget();
        systemWidget.Margin = new Padding(0, 0, 0, 14);
        grid.Controls.Add(systemWidget, 0, grid.RowCount++);
        grid.SetColumnSpan(systemWidget, 2);

        var loadResult = LoadShortcutConfigurationState(() => configurationStore.LoadOrDefault());
        var shortcuts = loadResult.Configuration.Shortcuts?.ToList() ?? [];
        var preview = CreateCydHardwarePreviewPanel(shortcuts, null);
        preview.Margin = new Padding(0, 0, 0, 14);
        grid.Controls.Add(preview, 0, grid.RowCount++);
        grid.SetColumnSpan(preview, 2);

        AddSectionHeader(grid, "DEVICE & CONNECTION");
        AddInfoRow(grid, "CYD", AgentStatusFormatter.ToDeviceStatus(snapshot));
        AddInfoRow(grid, "Device name", snapshot.DeviceName ?? "Not paired");
        AddInfoRow(grid, "Device ID", snapshot.DeviceId ?? "None");
        AddInfoRow(grid, "Firmware", FormatUnavailable(deviceInfo?.FirmwareVersion));
        AddInfoRow(grid, "Transport", snapshot.RemoteAddress == "USB" ? "USB" : snapshot.HostStatus == "connected" ? "Network" : "Disconnected");
        AddInfoRow(grid, "Device IP", FormatUnavailable(deviceInfo?.Wifi?.Ip));
        AddInfoRow(grid, "Audio", deviceInfo?.Audio is not null ? $"Volume {deviceInfo.Audio.VolumePercent}%{(deviceInfo.Audio.Muted ? " (Muted)" : "")}" : "Unavailable");
        AddInfoRow(grid, "Artwork", deviceInfo?.Cover is not null ? (deviceInfo.Cover.Rendered ? "Active / Rendered" : $"Status {deviceInfo.Cover.Status}") : "Unavailable");
        AddInfoRow(grid, "Agent service", AgentStatusFormatter.ToUserStatus(snapshot.HostStatus));
        AddInfoRow(grid, "Last seen", AgentStatusFormatter.FormatLastSeen(snapshot));

        AddPageBodyControl(panel, grid);
        AddPageFooter(panel, CreateActionsBar(
            ("Reconnect device", ReconnectAsync),
            ("Configure shortcuts", () => { ShowPage("Shortcuts"); return Task.CompletedTask; }),
            ("Appearance", () => { ShowPage("Appearance"); return Task.CompletedTask; }),
            ("Device details", () => { ShowPage("Device"); return Task.CompletedTask; }),
            ("Pairing", () => { ShowPage("Pairing"); return Task.CompletedTask; })));
        return panel;
    }

    private Control CreateDevicePage()
    {
        var snapshot = state.Snapshot();
        var deviceInfo = snapshot.HostStatus == "connected" ? snapshot.DeviceInfo : null;
        var isConnected = string.Equals(snapshot.HostStatus, "connected", StringComparison.OrdinalIgnoreCase);
        var hasActiveDevice = !string.IsNullOrWhiteSpace(snapshot.DeviceId);
        deviceTelemetryVisible = deviceInfo is not null;
        var panel = CreatePage("Device");
        var grid = CreateInfoGrid();

        AddConnectionSummary(grid, snapshot);
        AddSectionHeader(grid, "DEVICE INFO");
        AddInfoRow(grid, "Device name", snapshot.DeviceName ?? "Unavailable");
        AddInfoRow(grid, "Device ID", snapshot.DeviceId ?? "Unavailable");
        AddInfoRow(grid, "Status", AgentStatusFormatter.ToDeviceStatus(snapshot));

        if (deviceInfo is not null)
        {
            AddInfoRow(grid, "Firmware", FormatUnavailable(deviceInfo.FirmwareVersion));
            AddInfoRow(grid, "Uptime", FormatUptime(deviceInfo.UptimeSeconds));

            AddSectionHeader(grid, "NETWORK");
            AddInfoRow(grid, "Wi-Fi", FormatWifi(deviceInfo.Wifi));
            AddInfoRow(grid, "SSID", FormatUnavailable(deviceInfo.Wifi.Ssid));
            AddInfoRow(grid, "RSSI", FormatRssi(deviceInfo.Wifi.Rssi));
            AddInfoRow(grid, "IP", FormatUnavailable(deviceInfo.Wifi.Ip));
            AddInfoRow(grid, "Agent host", FormatUnavailable(deviceInfo.AgentHost));

            AddSectionHeader(grid, "STORAGE");
            AddInfoRow(grid, "SD", FormatSdState(deviceInfo.Sd));
            AddInfoRow(grid, "SD capacity", FormatBytes(deviceInfo.Sd.CapacityBytes));
            AddInfoRow(grid, "SD free", FormatBytes(deviceInfo.Sd.FreeBytes));

            AddSectionHeader(grid, "MEMORY");
            AddInfoRow(grid, "Heap free", FormatBytes(deviceInfo.Heap.FreeBytes));
            AddInfoRow(grid, "Heap min free", FormatBytes(deviceInfo.Heap.MinFreeBytes));
            AddInfoRow(grid, "Heap largest block", FormatBytes(deviceInfo.Heap.LargestBlockBytes));

            AddSectionHeader(grid, "AUDIO & ARTWORK");
            AddInfoRow(grid, "Audio", deviceInfo.Audio is not null ? $"Volume {deviceInfo.Audio.VolumePercent}%{(deviceInfo.Audio.Muted ? " (Muted)" : "")}" : "Unavailable");
            AddInfoRow(grid, "Artwork", deviceInfo.Cover is not null ? (deviceInfo.Cover.Rendered ? "Active / Rendered" : $"Status {deviceInfo.Cover.Status}") : "Unavailable");
        }
        else
        {
            AddSectionHeader(grid, "HARDWARE TELEMETRY");
            AddInfoRow(grid, "Telemetry", isConnected ? "Waiting for device report…" : "Unavailable while offline (connect device via USB or Wi-Fi)");
        }

        AddSectionHeader(grid, "CONNECTION");
        AddInfoRow(grid, "Remote IP", FormatRemoteHost(snapshot.RemoteAddress));
        AddInfoRow(grid, "Remote endpoint", snapshot.RemoteAddress ?? "Unavailable");
        AddInfoRow(grid, "Agent service", AgentStatusFormatter.ToUserStatus(snapshot.HostStatus));
        AddInfoRow(grid, "Last seen", AgentStatusFormatter.FormatLastSeen(snapshot));
        AddInfoRow(grid, "Agent port", snapshot.HostPort.ToString());
        AddInfoRow(grid, "Dashboard port", snapshot.DashboardPort == 0 ? "Unavailable" : snapshot.DashboardPort.ToString());

        AddSectionHeader(grid, "DANGER ZONE");
        AddInfoRow(grid, "Warning", "Destructive actions require physical or network re-pairing.");

        var dangerActions = CreateDeviceActionsBar(hasActiveDevice, isConnected,
            ("Factory reset", FactoryResetDeviceAsync), ("Forget device", ForgetDeviceAsync));
        dangerActions.Dock = DockStyle.Top;
        grid.Controls.Add(dangerActions, 0, grid.RowCount++);
        grid.SetColumnSpan(dangerActions, 2);
        AddPageBodyControl(panel, grid);
        AddPageFooter(panel, CreateDeviceActionsBar(hasActiveDevice, isConnected,
            ("Reconnect", ReconnectAsync), ("Restart", RestartDeviceAsync)));
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
            Text = "Loading pairing requests…",
            Padding = new Padding(0, 10, 0, 0)
        };

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Background,
            AutoScroll = true
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var instructionsLabel = new Label
        {
            Text = "USB connects automatically — no approval needed.\nFor Wi-Fi, connect CYD to the same network and approve its request below.",
            ForeColor = TextMuted,
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 12),
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Regular)
        };

        var pendingHeader = new Label
        {
            Text = "Waiting for approval",
            AutoSize = true,
            ForeColor = Accent,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
            Padding = new Padding(0, 6, 0, 6)
        };

        var pendingList = CreateThemedListView();
        pendingList.Height = 160;
        pendingList.Dock = DockStyle.Top;
        pendingList.MultiSelect = false;
        ConfigurePairingColumns(pendingList, MinimumSettingsClientWidth);
        AttachPairingColumnResizeHandler(pendingList);

        var pairedHeader = new Label
        {
            Text = "Trusted devices",
            AutoSize = true,
            ForeColor = Accent,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
            Padding = new Padding(0, 14, 0, 6)
        };

        var pairedList = CreateThemedListView();
        pairedList.Height = 160;
        pairedList.Dock = DockStyle.Top;
        pairedList.MultiSelect = false;
        ConfigurePairingColumns(pairedList, MinimumSettingsClientWidth);
        AttachPairingColumnResizeHandler(pairedList);

        var approveButton = CreateStyledActionButton("Approve", useAccent: true);
        approveButton.Enabled = false;

        var rejectButton = CreateNeutralButton("Reject");
        rejectButton.Enabled = false;

        var forgetPairedButton = CreateStyledActionButton("Forget paired device", useAccent: false);
        forgetPairedButton.Enabled = false;

        var refreshButton = CreateNeutralButton("Refresh");

        pendingList.SelectedIndexChanged += (_, _) =>
        {
            var hasSelection = pendingList.SelectedItems.Count > 0;
            approveButton.Enabled = hasSelection;
            rejectButton.Enabled = hasSelection;
        };

        pairedList.SelectedIndexChanged += (_, _) =>
        {
            forgetPairedButton.Enabled = pairedList.SelectedItems.Count > 0;
        };

        approveButton.Click += async (_, _) => await PairSelectedDeviceAsync(pendingList, pairedList, status, approve: true, approveButton, rejectButton).ConfigureAwait(true);
        rejectButton.Click += async (_, _) => await PairSelectedDeviceAsync(pendingList, pairedList, status, approve: false, approveButton, rejectButton).ConfigureAwait(true);
        forgetPairedButton.Click += async (_, _) => await ForgetPairedDeviceAsync(pairedList, pendingList, status, forgetPairedButton).ConfigureAwait(true);
        refreshButton.Click += async (_, _) => await RefreshPairingListAsync(pendingList, pairedList, status, approveButton, rejectButton, forgetPairedButton).ConfigureAwait(true);

        var actionsBar = new FlowLayoutPanel
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
        actionsBar.Controls.Add(approveButton);
        actionsBar.Controls.Add(rejectButton);
        actionsBar.Controls.Add(forgetPairedButton);
        actionsBar.Controls.Add(refreshButton);

        mainLayout.Controls.Add(instructionsLabel, 0, 0);
        mainLayout.Controls.Add(pendingHeader, 0, 1);
        mainLayout.Controls.Add(pendingList, 0, 2);
        mainLayout.Controls.Add(pairedHeader, 0, 3);
        mainLayout.Controls.Add(pairedList, 0, 4);

        AddPageBodyControl(panel, mainLayout);
        AddPageBodyControl(panel, status);
        AddPageFooter(panel, actionsBar);

        refreshPairingLists = () =>
        {
            if (!pendingList.IsDisposed && !pairedList.IsDisposed)
                _ = RefreshPairingListAsync(pendingList, pairedList, status, approveButton, rejectButton, forgetPairedButton);
        };
        refreshPairingLists();
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
        list.AccessibleName = "Agent-side shortcuts";
        list.AccessibleDescription = "Saved shortcuts available on this PC. The device displays up to the first six labels and IDs.";
        ConfigureShortcutsColumns(list, MinimumSettingsClientWidth);
        SuppressHorizontalListScrollbar(list);
        list.SizeChanged += (_, _) =>
        {
            ConfigureShortcutsColumns(list, list.ClientSize.Width);
            SuppressHorizontalListScrollbar(list);
        };
        list.MultiSelect = false;
        list.DoubleClick += async (_, _) => await EditShortcutAsync(list, shortcuts).ConfigureAwait(true);
        list.AllowDrop = true;
        list.ItemDrag += (_, e) =>
        {
            if (e.Item is ListViewItem) list.DoDragDrop(e.Item, DragDropEffects.Move);
        };
        list.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(typeof(ListViewItem)) == true) e.Effect = DragDropEffects.Move;
        };
        list.DragDrop += async (_, e) =>
        {
            var pt = list.PointToClient(new Point(e.X, e.Y));
            var targetItem = list.GetItemAt(pt.X, pt.Y);
            if (targetItem != null && e.Data?.GetData(typeof(ListViewItem)) is ListViewItem sourceItem && sourceItem != targetItem)
            {
                var fromIdx = sourceItem.Index;
                var toIdx = targetItem.Index;
                MoveShortcutInMemory(shortcuts, fromIdx, toIdx);
                if (await SaveShortcutsAsync(shortcuts).ConfigureAwait(true))
                {
                    ShowPage("Shortcuts");
                    SelectShortcutAt(toIdx);
                }
            }
        };

        var emptyState = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = TextMuted,
            BackColor = Surface,
            Text = CreateShortcutsEmptyStateText(loadResult),
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(20)
        };

        var cydPreviewCard = CreateCydHardwarePreviewPanel(shortcuts, list);

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

        var actionsBar = (FlowLayoutPanel)CreateActionsBar(
            ("Add", async () => await AddShortcutAsync(list, shortcuts, emptyState, loadResult).ConfigureAwait(true)),
            ("Edit", async () => await EditShortcutAsync(list, shortcuts).ConfigureAwait(true)),
            ("Delete", async () => await DeleteShortcutAsync(list, shortcuts, emptyState, loadResult).ConfigureAwait(true)),
            ("Test action", async () => await TestSelectedShortcutAsync(list, shortcuts).ConfigureAwait(true)),
            ("Move up", async () => await MoveShortcutAsync(list, shortcuts, -1).ConfigureAwait(true)),
            ("Move down", async () => await MoveShortcutAsync(list, shortcuts, 1).ConfigureAwait(true)),
            ("Move to…", async () => await MoveShortcutToPositionAsync(list, shortcuts).ConfigureAwait(true)),
            ("Import full config", ImportConfigurationAsync),
            ("Export full config", ExportConfigurationAsync));

        var editBtn = actionsBar.Controls.OfType<Button>().FirstOrDefault(b => b.Text == "Edit");
        var deleteBtn = actionsBar.Controls.OfType<Button>().FirstOrDefault(b => b.Text == "Delete");
        var testBtn = actionsBar.Controls.OfType<Button>().FirstOrDefault(b => b.Text == "Test action");
        var moveUpBtn = actionsBar.Controls.OfType<Button>().FirstOrDefault(b => b.Text == "Move up");
        var moveDownBtn = actionsBar.Controls.OfType<Button>().FirstOrDefault(b => b.Text == "Move down");
        var moveToBtn = actionsBar.Controls.OfType<Button>().FirstOrDefault(b => b.Text == "Move to…");

        void UpdateShortcutActionButtons()
        {
            var hasSelection = list.SelectedIndices.Count > 0;
            var selIndex = hasSelection ? list.SelectedIndices[0] : -1;
            if (editBtn != null) editBtn.Enabled = hasSelection;
            if (deleteBtn != null) deleteBtn.Enabled = hasSelection;
            if (testBtn != null) testBtn.Enabled = hasSelection;
            if (moveToBtn != null) moveToBtn.Enabled = hasSelection;
            if (moveUpBtn != null) moveUpBtn.Enabled = hasSelection && selIndex > 0;
            if (moveDownBtn != null) moveDownBtn.Enabled = hasSelection && selIndex < list.Items.Count - 1;
        }

        list.SelectedIndexChanged += (_, _) => UpdateShortcutActionButtons();
        UpdateShortcutActionButtons();

        AddPageBodyControl(panel, listHost);
        AddPageBodyControl(panel, cydPreviewCard);
        AddPageBodyControl(panel, shortcutCatalogNote);
        AddPageFooter(panel, actionsBar);

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
        list.AccessibleName = "Inactive device-reported shortcuts";
        list.AccessibleDescription = "Device-reported shortcut drafts that require configuration before they can run on this PC.";
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
        var configureButton = CreateNeutralButton("Configure selected");
        configureButton.AccessibleDescription = "Open the shortcut editor for the selected inactive device-reported shortcut.";
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

    internal static IReadOnlyList<string> FilterDiagnosticLines(
        IEnumerable<string> lines,
        string? searchText,
        bool errorsOnly)
    {
        return FilterDiagnosticLines(lines, searchText, errorsOnly ? "errors" : "all");
    }

    internal static IReadOnlyList<string> FilterDiagnosticLines(
        IEnumerable<string> lines,
        string? searchText,
        string? levelFilter)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var search = searchText?.Trim() ?? string.Empty;
        var filter = levelFilter?.Trim().ToLowerInvariant() ?? "all";
        return lines
            .Where(line => search.Length == 0 || line.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(line => MatchesDiagnosticLevel(line, filter))
            .ToArray();
    }

    private static bool MatchesDiagnosticLevel(string line, string filter)
    {
        if (filter is "all" or "") return true;
        if (filter is "errors" or "error" or "errors only") return IsDiagnosticErrorLine(line);
        if (filter is "warnings" or "warning" or "warnings & errors")
            return IsDiagnosticErrorLine(line) || line.Contains("warn", StringComparison.OrdinalIgnoreCase);
        if (filter is "info" or "info only")
            return !IsDiagnosticErrorLine(line) && !line.Contains("warn", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static bool IsDiagnosticErrorLine(string line)
    {
        return line.Contains("error", StringComparison.OrdinalIgnoreCase)
            || line.Contains("exception", StringComparison.OrdinalIgnoreCase)
            || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("failure", StringComparison.OrdinalIgnoreCase);
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
        var desktopSettings = pendingDesktopAppearance ?? LoadDesktopAppearanceConfiguration();
        pendingDesktopAppearance = desktopSettings;
        var settings = pendingAppearance ?? LoadAppearanceConfiguration();
        pendingAppearance = settings;

        var effectiveDesktopTheme = ResolveEffectiveDesktopTheme(desktopSettings.Theme);
        var effectiveDesktopBg = ResolveDesktopWindowBackground(desktopSettings, effectiveDesktopTheme);
        ApplyClientTheme(effectiveDesktopTheme, desktopSettings.Accent, effectiveDesktopBg);
        contentPanel.BackColor = ResolveClientBackground(effectiveDesktopTheme, effectiveDesktopBg);

        var panel = CreatePage("Appearance");
        var snapshot = state.Snapshot();
        var isConnected = string.Equals(snapshot.HostStatus, "connected", StringComparison.OrdinalIgnoreCase);

        var contentHost = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            RowCount = 7,
            ColumnCount = 1,
            BackColor = Background
        };
        contentHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var row = 0; row < 7; row++) contentHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var appearanceNote = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = TextMuted,
            Text = "Windows Agent (Desktop UI) and CYD Display (ESP32 Hardware UI) appearance settings are completely independent. Desktop changes apply locally to this window; CYD Display settings sync directly to the hardware screen.",
            Padding = new Padding(0, 0, 0, 10),
            Font = new Font(Font.FontFamily, 9F, FontStyle.Regular)
        };

        var (cydPreviewCard, updateCydPreview) = CreateAppearanceCydPreviewPanel(settings);

        var desktopForm = CreateAppearanceSectionPanel("WINDOWS AGENT APPEARANCE (DESKTOP UI)");
        AddComboRow(desktopForm, "Application theme", ["Dark", "Light", "System"], FormatChoiceDisplay(desktopSettings.Theme, "Dark"), value =>
        {
            desktopSettings.Theme = value.ToLowerInvariant();
            var resolvedTheme = ResolveEffectiveDesktopTheme(desktopSettings.Theme);
            var resolvedBg = ResolveDesktopWindowBackground(desktopSettings, resolvedTheme);
            ApplyClientTheme(resolvedTheme, desktopSettings.Accent, resolvedBg);
        });
        AddComboRow(desktopForm, "Window background style", ["Acrylic", "Minimal", "Gradient"], FormatChoiceDisplay(desktopSettings.WindowStyle, "Acrylic"), value =>
        {
            desktopSettings.WindowStyle = value.ToLowerInvariant();
            var resolvedTheme = ResolveEffectiveDesktopTheme(desktopSettings.Theme);
            var resolvedBg = ResolveDesktopWindowBackground(desktopSettings, resolvedTheme);
            ApplyClientTheme(resolvedTheme, desktopSettings.Accent, resolvedBg);
        });
        AddColorRow(desktopForm, "UI accent color", desktopSettings.Accent, value =>
        {
            desktopSettings.Accent = value;
            var resolvedTheme = ResolveEffectiveDesktopTheme(desktopSettings.Theme);
            var resolvedBg = ResolveDesktopWindowBackground(desktopSettings, resolvedTheme);
            ApplyClientTheme(resolvedTheme, desktopSettings.Accent, resolvedBg);
        });

        var cydThemeForm = CreateAppearanceSectionPanel("CYD DISPLAY APPEARANCE (HARDWARE UI — 320×240)");
        AddComboRow(cydThemeForm, "CYD display theme", ["Dark", "Light"], FormatChoiceDisplay(settings.Theme, "Dark"), value =>
        {
            settings.Theme = value.ToLowerInvariant();
            if (string.Equals(settings.Theme, "dark", StringComparison.OrdinalIgnoreCase) && string.Equals(settings.Background, "#F3F5F7", StringComparison.OrdinalIgnoreCase))
            {
                settings.Background = "#101216";
            }
            updateCydPreview();
        });
        AddComboRow(cydThemeForm, "Tile color scheme", ["control-center", "accent-tint", "oled-contrast"], settings.TileColorScheme, value =>
        {
            settings.TileColorScheme = value.ToLowerInvariant();
            updateCydPreview();
        });
        AddColorRow(cydThemeForm, "Device accent color", settings.Accent, value =>
        {
            settings.Accent = value;
            updateCydPreview();
        });
        AddColorRow(cydThemeForm, "Display background (OLED / Tint)", settings.Background, value =>
        {
            settings.Background = value;
            updateCydPreview();
        });
        AddComboRow(cydThemeForm, "Tile corner style", ["Rounded", "Flat", "Pill"], FormatChoiceDisplay(settings.ButtonStyle, "Rounded"), value =>
        {
            settings.ButtonStyle = value.ToLowerInvariant();
            updateCydPreview();
        });
        AddComboRow(cydThemeForm, "Shortcuts layout (tile size)", ["auto", "hero-1", "split-2", "quad-4", "grid-6"], settings.ShortcutsLayout, value =>
        {
            settings.ShortcutsLayout = value.ToLowerInvariant();
            updateCydPreview();
        });
        AddComboRow(cydThemeForm, "StandBy / Clock style", ["apple-standby", "watchos-modular", "minimal-oled"], settings.StandbyStyle, value =>
        {
            settings.StandbyStyle = value.ToLowerInvariant();
            updateCydPreview();
        });

        var displayForm = CreateAppearanceSectionPanel("CYD BACKLIGHT & STANDBY POWER");
        AddSliderRow(displayForm, "Brightness", 10, 100, settings.Brightness, "%", value =>
        {
            settings.Brightness = value;
            updateCydPreview();
        });
        AddSliderRow(displayForm, "Dim timeout", 0, 86400, settings.DimAfterSec, " sec", value =>
        {
            settings.DimAfterSec = value;
            updateCydPreview();
        }, FormatAppearanceTimeout);
        AddSliderRow(displayForm, "Dim level", 1, 100, settings.DimBrightness, "%", value =>
        {
            settings.DimBrightness = value;
            updateCydPreview();
        });
        AddSliderRow(displayForm, "Screen-off timeout", 0, 86400, settings.ScreenOffAfterSec, " sec", value =>
        {
            settings.ScreenOffAfterSec = value;
            updateCydPreview();
        }, FormatAppearanceTimeout);
        AddCheckRow(displayForm, "Wake on touch", settings.WakeOnTouch, value =>
        {
            settings.WakeOnTouch = value;
            updateCydPreview();
        });

        var pagesForm = CreateAppearanceSectionPanel("CYD PAGE ORDER & VISIBILITY");
        AddPageListRow(pagesForm, "Page order / visibility", settings, updateCydPreview);

        appearanceStatusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 36,
            ForeColor = isConnected ? TextMuted : Color.Khaki,
            Text = GetAppearanceSyncStatusText(isConnected, snapshot.DeviceName),
            Padding = new Padding(0, 8, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var actionsBar = (FlowLayoutPanel)CreateActionsBar(
            ("Restore defaults", () =>
            {
                var result = MessageBox.Show(this, "Restore both Windows Agent and CYD Display appearance settings to Apple HIG defaults?", "Restore appearance defaults", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    RestoreDefaultAppearance();
                }
                return Task.CompletedTask;
            }),
            ("Sync now", async () =>
            {
                await FlushAppearanceChangesAsync().ConfigureAwait(true);
            })
        );

        contentHost.Controls.Add(appearanceNote, 0, 0);
        contentHost.Controls.Add(cydPreviewCard, 0, 1);
        contentHost.Controls.Add(desktopForm, 0, 2);
        contentHost.Controls.Add(cydThemeForm, 0, 3);
        contentHost.Controls.Add(displayForm, 0, 4);
        contentHost.Controls.Add(pagesForm, 0, 5);
        contentHost.Controls.Add(appearanceStatusLabel, 0, 6);

        AddPageBodyControl(panel, contentHost);
        AddPageFooter(panel, actionsBar);
        return panel;
    }

    private static string FormatChoiceDisplay(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return char.ToUpperInvariant(raw[0]) + raw[1..].ToLowerInvariant();
    }

    private TableLayoutPanel CreateAppearanceSectionPanel(string sectionTitle)
    {
        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = Background,
            Padding = new Padding(0, 4, 0, 14),
            Margin = new Padding(0, 0, 0, 10)
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

        var header = new Label
        {
            Text = sectionTitle,
            ForeColor = Accent,
            Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold),
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 6)
        };
        form.Controls.Add(header, 0, form.RowCount);
        form.SetColumnSpan(header, 2);
        form.RowCount++;

        return form;
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
        combo.SelectedItem = values.FirstOrDefault(v => string.Equals(v, current, StringComparison.OrdinalIgnoreCase)) ?? values[0];
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

    private void AddSliderRow(TableLayoutPanel form, string label, int min, int max, int current, string suffix, Action<int> update, Func<int, string>? customFormatter = null)
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            BackColor = Background,
            ColumnCount = 2,
            Margin = new Padding(0, 6, 0, 6)
        };
        var initialText = customFormatter != null ? customFormatter(current) : $"{current}{suffix}";
        var valueLabel = new Label { Width = GetAppearanceSliderValueWidth(initialText), ForeColor = TextPrimary, Text = initialText, Padding = new Padding(8, 6, 0, 0), TextAlign = ContentAlignment.MiddleLeft };
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
            var formatted = customFormatter != null ? customFormatter(track.Value) : $"{track.Value}{suffix}";
            valueLabel.Text = formatted;
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

    private void AddPageListRow(TableLayoutPanel form, string label, AppearanceState settings, Action? onOrderOrVisibilityChanged = null)
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            BackColor = Background,
            ColumnCount = 3
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var list = new ListBox
        {
            Dock = DockStyle.Fill,
            Width = 160,
            Height = 118,
            BackColor = Surface,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };

        var visibilityPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            BackColor = Background,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };

        static string FormatPageDisplayName(string page) => page switch
        {
            "pc" => "PC (System)",
            "media" => "Media",
            "shortcuts" => "Shortcuts",
            "info" => "Info (Status)",
            _ => char.ToUpperInvariant(page[0]) + page[1..]
        };

        void RefreshPageList(int selectedIndex)
        {
            list.Items.Clear();
            foreach (var page in settings.PageOrder)
            {
                var display = FormatPageDisplayName(page);
                list.Items.Add(ForcedVisibleAppearancePages.Contains(page) ? $"{display} (always visible)" : display);
            }

            if (list.Items.Count > 0)
            {
                list.SelectedIndex = Math.Clamp(selectedIndex, 0, list.Items.Count - 1);
            }
        }

        void RefreshVisibilityControls()
        {
            visibilityPanel.Controls.Clear();
            foreach (var page in settings.PageOrder)
            {
                var isRequired = ForcedVisibleAppearancePages.Contains(page);
                var display = FormatPageDisplayName(page);
                var check = new CheckBox
                {
                    Text = isRequired ? $"{display} (Required)" : display,
                    Checked = isRequired || settings.PageVisibility.Contains(page, StringComparer.Ordinal),
                    Enabled = !isRequired,
                    AutoSize = true,
                    BackColor = Background,
                    ForeColor = isRequired ? TextMuted : TextPrimary,
                    Margin = new Padding(0, 3, 0, 3)
                };
                if (!isRequired)
                {
                    check.CheckedChanged += (_, _) =>
                    {
                        var visible = settings.PageVisibility.ToHashSet(StringComparer.Ordinal);
                        if (check.Checked)
                        {
                            visible.Add(page);
                        }
                        else
                        {
                            visible.Remove(page);
                        }

                        visible.UnionWith(ForcedVisibleAppearancePages);
                        settings.PageVisibility = settings.PageOrder.Where(visible.Contains).ToArray();
                        onOrderOrVisibilityChanged?.Invoke();
                        QueueAppearanceSave();
                    };
                }
                visibilityPanel.Controls.Add(check);
            }
        }

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
            RefreshVisibilityControls();
            onOrderOrVisibilityChanged?.Invoke();
            QueueAppearanceSave();
        }

        up.Click += (_, _) => MoveSelected(-1);
        down.Click += (_, _) => MoveSelected(1);
        buttons.Controls.Add(up);
        buttons.Controls.Add(down);
        panel.Controls.Add(list, 0, 0);
        panel.Controls.Add(buttons, 1, 0);
        panel.Controls.Add(visibilityPanel, 2, 0);
        RefreshPageList(0);
        RefreshVisibilityControls();
        panel.Dock = DockStyle.Top;
        form.Controls.Add(panel, 0, form.RowCount);
        form.SetColumnSpan(panel, 2);
        form.RowCount++;
    }

    private (Control Panel, Action Update) CreateAppearanceCydPreviewPanel(AppearanceState settings)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 186,
            BackColor = Background,
            Padding = new Padding(0, 0, 0, 10),
            ColumnCount = 1,
            RowCount = 3
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        var headerLabel = new Label
        {
            Text = "CYD DISPLAY PREVIEW (320×240 ST7789 HARDWARE WYSIWYG)",
            ForeColor = Accent,
            Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        card.Controls.Add(headerLabel, 0, 0);

        var bezel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(18, 20, 26),
            Padding = new Padding(6, 4, 6, 4),
            BorderStyle = BorderStyle.FixedSingle
        };

        var displayHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(4)
        };
        displayHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        displayHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        displayHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));

        var topBar = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 7.5F, FontStyle.Regular),
            Text = "CYD-DECK  •  Control Center Grid  •  ST7789 320×240",
            TextAlign = ContentAlignment.MiddleLeft
        };
        displayHost.Controls.Add(topBar, 0, 0);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(2)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

        var sampleButtons = new List<Button>();
        var previewShortcuts = LoadConfiguration().Shortcuts ?? [];
        var sampleLabels = Enumerable.Range(0, 6).Select(index => index < previewShortcuts.Count ? previewShortcuts[index].Name : "Empty").ToArray();
        for (var i = 0; i < 6; i++)
        {
            var btn = new Button
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(3),
                FlatStyle = FlatStyle.Flat,
                Font = new Font(Font.FontFamily, 8F, FontStyle.Bold),
                Text = sampleLabels[i],
                TextAlign = ContentAlignment.MiddleCenter,
                Enabled = true,
                TabStop = false,
                Cursor = Cursors.Default,
                AccessibleName = $"CYD shortcut preview slot {i + 1}"
            };
            sampleButtons.Add(btn);
            grid.Controls.Add(btn, i % 3, i / 3);
        }
        displayHost.Controls.Add(grid, 0, 1);

        var bottomBar = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 7.5F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleCenter
        };
        displayHost.Controls.Add(bottomBar, 0, 2);

        bezel.Controls.Add(displayHost);
        card.Controls.Add(bezel, 0, 1);

        var infoLabel = new Label
        {
            ForeColor = TextMuted,
            Font = new Font(Font.FontFamily, 8.5F, FontStyle.Regular),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        card.Controls.Add(infoLabel, 0, 2);

        void UpdatePreview()
        {
            var isLight = string.Equals(settings.Theme, "light", StringComparison.OrdinalIgnoreCase);
            var screenBg = ResolveCydPreviewBackground(settings.Theme, settings.Background);
            var accentColor = ParseHexColor(settings.Accent, Accent);
            var textColor = isLight ? Color.FromArgb(28, 30, 36) : Color.FromArgb(245, 247, 250);
            var textMutedColor = isLight ? Color.FromArgb(100, 104, 114) : Color.FromArgb(148, 154, 166);
            var btnBg = ResolveCydTileBackground(settings.Theme, settings.TileColorScheme, accentColor);

            displayHost.BackColor = screenBg;
            grid.BackColor = screenBg;
            topBar.BackColor = screenBg;
            topBar.ForeColor = textMutedColor;
            bottomBar.BackColor = screenBg;
            bottomBar.ForeColor = textMutedColor;
            bottomBar.Text = $"● ○ ○ ○   •   {settings.TileColorScheme.ToUpperInvariant()}   •   {settings.StandbyStyle.ToUpperInvariant()}   •   {settings.Brightness}%";

            for (var i = 0; i < sampleButtons.Count; i++)
            {
                var btn = sampleButtons[i];
                btn.BackColor = btnBg;
                btn.ForeColor = textColor;
                btn.FlatAppearance.BorderColor = (i == 0) ? accentColor : (isLight ? Color.FromArgb(205, 210, 220) : Color.FromArgb(46, 50, 60));
                btn.FlatAppearance.BorderSize = string.Equals(settings.ButtonStyle, "flat", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            }

            infoLabel.Text = $"CYD Hardware UI: 320×240 • Scheme: {settings.TileColorScheme} • Clock: {settings.StandbyStyle} • Dim: {FormatAppearanceTimeout(settings.DimAfterSec)}";
        }

        MarkDeviceColorPreview(displayHost);
        foreach (var child in FindControls<Control>(displayHost)) MarkDeviceColorPreview(child);
        UpdatePreview();
        return (card, UpdatePreview);
    }

    private static Color ResolveCydPreviewBackground(string? theme, string? hexBg)
    {
        var isLight = string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase);
        var raw = ParseHexColor(hexBg ?? string.Empty, isLight ? Color.FromArgb(243, 245, 247) : Color.FromArgb(16, 18, 22));
        if (!isLight && raw.GetBrightness() > 0.35f)
        {
            return Color.FromArgb(
                Math.Clamp((int)(raw.R * 0.22), 11, 42),
                Math.Clamp((int)(raw.G * 0.22), 13, 44),
                Math.Clamp((int)(raw.B * 0.25), 18, 52));
        }

        return raw;
    }

    private static Color ResolveCydTileBackground(string? theme, string? scheme, Color accent)
    {
        if (string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase))
        {
            return Color.FromArgb(255, 255, 255);
        }

        return (scheme?.ToLowerInvariant()) switch
        {
            "accent-tint" => Color.FromArgb(
                Math.Clamp(22 + accent.R / 7, 22, 58),
                Math.Clamp(24 + accent.G / 7, 24, 60),
                Math.Clamp(30 + accent.B / 7, 30, 68)),
            "oled-contrast" => Color.FromArgb(20, 20, 24),
            _ => Color.FromArgb(28, 28, 30)
        };
    }

    internal static string FormatAppearanceTimeout(int seconds)
    {
        if (seconds <= 0)
        {
            return "Disabled";
        }

        if (seconds < 60)
        {
            return $"{seconds} sec";
        }

        if (seconds % 60 == 0)
        {
            var mins = seconds / 60;
            if (mins >= 60 && mins % 60 == 0)
            {
                return $"{mins / 60} hr";
            }

            return $"{mins} min";
        }

        return $"{seconds / 60}m {seconds % 60}s";
    }

    internal static string GetAppearanceSyncStatusText(bool isConnected, string? deviceName = null)
    {
        return isConnected
            ? $"✓ Settings synced with connected device ({(string.IsNullOrWhiteSpace(deviceName) ? "CYD" : deviceName)})."
            : "ℹ Settings saved locally; will sync when CYD reconnects.";
    }

    private void RestoreDefaultAppearance()
    {
        pendingDesktopAppearance = DesktopAppearanceState.Default;
        pendingAppearance = AppearanceState.Default;
        SaveDesktopAppearanceConfiguration(pendingDesktopAppearance);
        SaveAppearanceConfiguration(pendingAppearance);
        var resolvedTheme = ResolveEffectiveDesktopTheme(pendingDesktopAppearance.Theme);
        var resolvedBg = ResolveDesktopWindowBackground(pendingDesktopAppearance, resolvedTheme);
        ApplyClientTheme(resolvedTheme, pendingDesktopAppearance.Accent, resolvedBg);
        QueueAppearanceSave();
        ShowPage("Appearance");
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

    internal static Color ResolveClientAccentForTests(string accent) => ResolveClientAccent(accent);

    internal static Color ResolveClientBackgroundForTests(string theme, string background) => ResolveClientBackground(theme, background);

    internal static string[] NormalizeAppearancePageOrderForTests(IReadOnlyList<string> pages)
    {
        return NormalizeAppearancePageOrder(pages);
    }

    internal static string[] NormalizeAppearancePageVisibilityForTests(IReadOnlyList<string> pageOrder, IReadOnlyList<string> pageVisibility)
    {
        return NormalizeAppearancePageVisibility(pageOrder, pageVisibility);
    }

    private static string[] NormalizeAppearancePageOrder(IReadOnlyList<string> pages)
    {
        var normalized = new List<string>();
        foreach (var page in pages)
        {
            if (DefaultAppearancePageOrder.Contains(page, StringComparer.Ordinal) && !normalized.Contains(page, StringComparer.Ordinal))
            {
                normalized.Add(page);
            }
        }

        foreach (var page in DefaultAppearancePageOrder)
        {
            if (normalized.Contains(page, StringComparer.Ordinal))
            {
                continue;
            }

            var defaultIndex = Array.IndexOf(DefaultAppearancePageOrder, page);
            var insertIndex = normalized.FindIndex(existing => Array.IndexOf(DefaultAppearancePageOrder, existing) > defaultIndex);
            if (insertIndex < 0)
            {
                normalized.Add(page);
            }
            else
            {
                normalized.Insert(insertIndex, page);
            }
        }

        return normalized.ToArray();
    }

    private static string[] NormalizeAppearancePageVisibility(IReadOnlyList<string> pageOrder, IReadOnlyList<string> pageVisibility)
    {
        var visible = new HashSet<string>(pageVisibility.Where(page => DefaultAppearancePageOrder.Contains(page, StringComparer.Ordinal)), StringComparer.Ordinal);
        visible.UnionWith(ForcedVisibleAppearancePages);
        return pageOrder.Where(visible.Contains).ToArray();
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

    internal static Size GetMinimumWindowSizeForTests() => new(MinimumWindowWidth, MinimumWindowHeight);

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

    private void ApplyClientTheme(string theme, string? deviceAccent = null, string? deviceBackground = null)
    {
        var useLightTheme = string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase);
        clientLightTheme = useLightTheme;
        var clientAccent = ResolveClientAccent(deviceAccent);
        var clientBackground = ResolveClientBackground(theme, deviceBackground);
        SuspendLayout();
        try
        {
            ApplyClientThemeColors(this, theme, clientAccent, clientBackground);
            UpdateTitleBarTheme();
        }
        finally
        {
            ResumeLayout(performLayout: true);
        }
    }

    private static void ApplyClientThemeColors(Control control, string theme, Color? accent = null, Color? background = null)
    {
        if (!ShouldPreserveClientThemeBackColor(control.Tag))
        {
            var mappedBackColor = MapClientThemeColor(control.BackColor, theme);
            control.BackColor = background.HasValue && (mappedBackColor.ToArgb() == Background.ToArgb() || mappedBackColor.ToArgb() == LightBackground.ToArgb())
                ? background.Value
                : mappedBackColor;
        }

        if (!ShouldPreserveClientThemeBackColor(control.Tag))
            control.ForeColor = MapClientThemeColor(control.ForeColor, theme);
        if (accent.HasValue && control.ForeColor.ToArgb() == Accent.ToArgb())
        {
            control.ForeColor = accent.Value;
        }
        foreach (Control child in control.Controls)
        {
            ApplyClientThemeColors(child, theme, accent, background);
        }
    }

    private static Color ResolveClientAccent(string? accent)
    {
        return ParseHexColor(accent ?? string.Empty, DeviceDefaultAccent);
    }

    private static Color ResolveClientBackground(string theme, string? background)
    {
        return ParseHexColor(background ?? string.Empty, GetClientThemeBackground(theme));
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

        AddSectionHeader(grid, "WINDOWS AGENT UPDATES");
        AddInfoRow(grid, "Agent version", GetAgentVersion());
        AddInfoRow(grid, "Protocol version", ProtocolConstants.CurrentVersion.ToString());
        AddInfoRow(grid, "Discovery status", "Not checked — no release source configured");

        AddSectionHeader(grid, "CYD FIRMWARE UPDATES");
        var isConnected = string.Equals(snapshot.HostStatus, "connected", StringComparison.OrdinalIgnoreCase);
        var fwVersion = isConnected ? snapshot.DeviceInfo?.FirmwareVersion : null;
        updateDeviceIp = isConnected ? snapshot.DeviceInfo?.Wifi.Ip : null;
        AddInfoRow(grid, "Firmware version", fwVersion is not null ? fwVersion : (isConnected ? "Waiting for device report…" : "Device offline"));
        AddInfoRow(grid, "Device IP", FormatUnavailable(updateDeviceIp));
        AddInfoRow(grid, "OTA method", "Network / ArduinoOTA");

        AddPageBodyControl(panel, grid);
        AddPageFooter(panel, CreateUpdateActionsBar(updateDeviceIp));
        return panel;
    }

    private Control CreateLogsPage()
    {
        var panel = CreatePage("Logs");
        var recentLines = AgentDiagnosticLog.ReadRecentLines().ToList();
        var text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Surface,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.None,
            Font = new Font("Cascadia Mono", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
        ApplySupportedScrollTheme(text);

        var search = new TextBox
        {
            Width = 280,
            Height = 30,
            PlaceholderText = "Search recent logs",
            AccessibleName = "Search recent logs",
            BackColor = Surface,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 4, 12, 4)
        };
        var errorsOnly = new CheckBox
        {
            Text = "Errors only",
            AccessibleName = "Show error log entries only",
            AutoSize = true,
            BackColor = Background,
            ForeColor = TextPrimary,
            Margin = new Padding(0, 9, 12, 4)
        };
        var levelCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Surface,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Width = 150,
            Height = 30,
            AccessibleName = "Filter logs by level",
            Margin = new Padding(0, 4, 12, 4)
        };
        levelCombo.Items.AddRange(["All levels", "Errors only", "Warnings & Errors", "Info only"]);
        levelCombo.SelectedIndex = 0;

        var filterBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            BackColor = Background,
            Padding = new Padding(0, 0, 0, 8)
        };
        filterBar.Controls.Add(search);
        filterBar.Controls.Add(errorsOnly);
        filterBar.Controls.Add(levelCombo);

        void RefreshFilteredLogs()
        {
            var level = errorsOnly.Checked ? "Errors only" : (levelCombo.SelectedItem?.ToString() ?? "All levels");
            text.Lines = FilterDiagnosticLines(recentLines, search.Text, level).ToArray();
        }

        search.TextChanged += (_, _) => RefreshFilteredLogs();
        errorsOnly.CheckedChanged += (_, _) =>
        {
            if (errorsOnly.Checked && levelCombo.SelectedIndex != 1)
            {
                levelCombo.SelectedIndex = 1;
            }
            else if (!errorsOnly.Checked && levelCombo.SelectedIndex == 1)
            {
                levelCombo.SelectedIndex = 0;
            }
            RefreshFilteredLogs();
        };
        levelCombo.SelectedIndexChanged += (_, _) =>
        {
            errorsOnly.Checked = levelCombo.SelectedIndex == 1;
            RefreshFilteredLogs();
        };
        RefreshFilteredLogs();

        var content = new Panel { Dock = DockStyle.Fill, BackColor = Background };
        content.Controls.Add(text);
        content.Controls.Add(filterBar);
        AddPageBodyControl(panel, content);
        AddPageFooter(panel, CreateActionsBar(
            ("Copy logs", () => Clipboard.SetText(text.Text)),
            ("Export logs…", () => ExportLogsToFile(text.Text)),
            ("Open log folder", OpenLogFolder),
            ("Clear view", () =>
            {
                AgentDiagnosticLog.ClearView();
                recentLines.Clear();
                RefreshFilteredLogs();
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
        DisableHorizontalAutoScroll(panel);
        panel.SizeChanged += (_, _) => DisableHorizontalAutoScroll(panel);
        panel.ControlAdded += (_, _) => DisableHorizontalAutoScroll(panel);
        ApplySupportedScrollTheme(panel);
        return panel;
    }

    private static void DisableHorizontalAutoScroll(Panel panel)
    {
        panel.AutoScrollMinSize = new Size(0, panel.AutoScrollMinSize.Height);
        panel.HorizontalScroll.Enabled = false;
        panel.HorizontalScroll.Visible = false;
        panel.HorizontalScroll.Maximum = 0;
    }

    private static void AddPageBodyControl(Control page, Control control)
    {
        FindPageBody(page).Controls.Add(control);
    }

    private static void AddPageFooter(Control page, Control footer)
    {
        footer.Tag = PageFooterTag;
        page.Controls.Add(footer);
        footer.SendToBack();
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
        ThemedHeaderWindow? headerWindow = null;
        list.HandleCreated += (_, _) =>
        {
            var handle = SendMessage(list.Handle, 0x101F, IntPtr.Zero, IntPtr.Zero);
            if (handle != IntPtr.Zero) { headerWindow = new ThemedHeaderWindow(list); headerWindow.AssignHandle(handle); }
        };
        list.HandleDestroyed += (_, _) => { headerWindow?.ReleaseHandle(); headerWindow = null; };
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
        var firstSeen = Math.Max(90, TextRenderer.MeasureText("First seen", SystemFonts.MessageBoxFont).Width + 24);
        var address = width >= 460 ? 184 : 150;
        var deviceId = Math.Clamp(width / 5, 80, 120);
        var name = Math.Max(40, width - firstSeen - address - deviceId);
        return [name, deviceId, address, firstSeen];
    }

    internal static int[] CalculateShortcutsColumnWidths(int availableWidth)
    {
        var width = Math.Max(360, availableWidth - ShortcutsListScrollbarAllowance);
        var id = 0; // ID remains available in the editor, not in the everyday list.
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
        SuppressHorizontalListScrollbar(list);
    }

    private static void SuppressHorizontalListScrollbar(ListView list)
    {
        const int sbHorz = 0;
        if (list.IsHandleCreated)
        {
            ShowScrollBar(list.Handle, sbHorz, false);
        }
    }

    internal static bool ControlHasHorizontalScrollbarForTests(Control control)
    {
        const int gwlStyle = -16;
        const int wsHscroll = 0x00100000;
        return (GetWindowLongPtr(control.Handle, gwlStyle).ToInt64() & wsHscroll) != 0;
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

    private void AddConnectionSummary(TableLayoutPanel grid, AgentStatusSnapshot snapshot)
    {
        var summary = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1,
            BackColor = Surface, Padding = new Padding(18), Margin = new Padding(0, 0, 0, 12)
        };
        var headline = new Label
        {
            Text = AgentStatusFormatter.ToDeviceStatus(snapshot), AutoSize = true,
            ForeColor = Accent, Font = new Font("Segoe UI", 20F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8)
        };
        var detail = new Label
        {
            Text = GetConnectionDetail(snapshot), AutoSize = true, ForeColor = TextMuted,
            Dock = DockStyle.Top, Margin = Padding.Empty
        };
        summary.Controls.Add(headline);
        summary.Controls.Add(detail);
        summary.SizeChanged += (_, _) => detail.MaximumSize = new Size(Math.Max(100, summary.ClientSize.Width - summary.Padding.Horizontal), 0);
        grid.Controls.Add(summary, 0, grid.RowCount++);
        grid.SetColumnSpan(summary, 2);
        infoValueLabels["Connection headline"] = headline;
        infoValueLabels["Connection detail"] = detail;
    }

    internal static string GetConnectionDetail(AgentStatusSnapshot snapshot) => snapshot.HostStatus switch
    {
        "connected" when snapshot.RemoteAddress == "USB" => "USB connection active. Settings sync automatically.",
        "connected" => "Network connection active. Settings sync automatically.",
        "pairing pending" => "Open Pairing to approve this device.",
        var status when status.StartsWith("faulted:", StringComparison.Ordinal) => status[8..].Trim(),
        "starting" => "Windows Agent is starting.",
        _ => "Connect CYD via USB or open Pairing for a network connection."
    };

    private void AddSectionHeader(TableLayoutPanel grid, string text)
    {
        var label = new Label
        {
            Text = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant()),
            AutoSize = true,
            ForeColor = TextMuted,
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

    private Control CreateDeviceActionsBar(bool hasActiveDevice, bool isConnected, params (string Text, Func<Task> Action)[] actions)
    {
        var bar = (FlowLayoutPanel)CreateActionsBar(actions);
        UpdateDeviceActionAvailability(bar, hasActiveDevice, isConnected);

        return bar;
    }

    internal static void UpdateDeviceActionAvailability(Control root, bool hasActiveDevice, bool isConnected)
    {
        foreach (var button in FindControls<Button>(root))
        {
            var action = button.Tag as string ?? button.Text;
            if (action is not ("Restart" or "Factory reset" or "Forget device")) continue;
            button.Tag = action;
            button.Enabled = action == "Forget device" ? hasActiveDevice : isConnected;
            button.Cursor = button.Enabled ? Cursors.Hand : Cursors.Default;
            button.AccessibleDescription = button.Enabled ? action : action == "Forget device"
                ? "No paired device to forget." : "Connect the device to use this action.";
        }
    }

    private Control CreateUpdateActionsBar(string? deviceIp)
    {
        var bar = (FlowLayoutPanel)CreateActionsBar(
            ("Check for updates", () =>
            {
                MessageBox.Show(
                    this,
                    "Automatic release checking is not configured. No online version check was performed. Use a trusted firmware file to install an update.",
                    "Check for updates",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return Task.CompletedTask;
            }),
            ("Install firmware from file (.bin)", InstallFirmwareFromFileAsync));

        if (string.IsNullOrWhiteSpace(deviceIp) && bar.Controls.Count > 1 && bar.Controls[1] is Button button)
        {
            button.Enabled = false;
            button.Text = "Install firmware (device offline)";
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
            || text.Contains("Forget", StringComparison.OrdinalIgnoreCase);
        var button = CreateStyledActionButton(text, isPrimary);
        if (isDestructive)
        {
            button.ForeColor = Danger;
            button.FlatAppearance.BorderColor = Danger;
        }
        return button;
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
            Font = new Font("Segoe UI", 9.5F, useAccent ? FontStyle.Bold : FontStyle.Regular),
            Padding = new Padding(12, 0, 12, 0),
            Margin = new Padding(0, ActionButtonVerticalMargin, ActionButtonHorizontalMargin, 0),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = useAccent ? 0 : 1;
        button.FlatAppearance.BorderColor = Border;
        button.EnabledChanged += (_, _) =>
        {
            button.BackColor = useAccent && button.Enabled ? Accent : SurfaceAlt;
            button.ForeColor = !button.Enabled ? TextMuted : useAccent ? Color.White : TextPrimary;
        };
        button.FlatAppearance.MouseOverBackColor = useAccent ? Color.FromArgb(62, 126, 190) : Border;
        button.FlatAppearance.MouseDownBackColor = useAccent ? Color.FromArgb(46, 102, 159) : Surface;
        return button;
    }

    private async Task RefreshPairingListAsync(ListView list, Label status)
    {
        await RefreshPairingListAsync(list, pairedList: null, status, approveButton: null, rejectButton: null, forgetPairedButton: null).ConfigureAwait(true);
    }

    private async Task RefreshPairingListAsync(
        ListView pendingList,
        ListView? pairedList,
        Label status,
        Button? approveButton,
        Button? rejectButton,
        Button? forgetPairedButton)
    {
        try
        {
            var pendingSelection = pendingList.SelectedItems.Cast<ListViewItem>().Select(item => item.Tag as string).ToHashSet();
            var pairedSelection = pairedList?.SelectedItems.Cast<ListViewItem>().Select(item => item.Tag as string).ToHashSet();
            var pendingDevices = await pairing.ListPendingAsync().ConfigureAwait(true);
            if (!pendingList.IsDisposed) pendingList.Height = Math.Clamp(54 + pendingDevices.Count * 28, 54, 140);
            if (!pendingList.IsDisposed)
            {
                pendingList.Items.Clear();
                foreach (var device in pendingDevices)
                {
                    pendingList.Items.Add(new ListViewItem([
                        FormatUnavailable(device.Name),
                        FormatUnavailable(device.DeviceId),
                        FormatUnavailable(device.RemoteAddress),
                        AgentStatusFormatter.FormatRelativeTime(device.FirstSeenUtc)
                    ]) { Tag = device.DeviceId });
                }
            }

            if (pairedList is not null && !pairedList.IsDisposed)
            {
                var approvedDevices = await pairing.ListApprovedAsync().ConfigureAwait(true);
                pairedList.Height = Math.Clamp(54 + approvedDevices.Count * 28, 54, 140);
                pairedList.Items.Clear();
                foreach (var device in approvedDevices)
                {
                    pairedList.Items.Add(new ListViewItem([
                        FormatUnavailable(device.Name),
                        FormatUnavailable(device.DeviceId),
                        FormatUnavailable(device.RemoteAddress),
                        AgentStatusFormatter.FormatRelativeTime(device.FirstSeenUtc)
                    ]) { Tag = device.DeviceId });
                }
            }

            if (!pendingList.IsDisposed)
                foreach (ListViewItem item in pendingList.Items) item.Selected = pendingSelection.Contains(item.Tag as string);
            if (pairedList is not null && !pairedList.IsDisposed && pairedSelection is not null)
                foreach (ListViewItem item in pairedList.Items) item.Selected = pairedSelection.Contains(item.Tag as string);

            if (approveButton is not null && !approveButton.IsDisposed) approveButton.Enabled = pendingList.SelectedItems.Count > 0;
            if (rejectButton is not null && !rejectButton.IsDisposed) rejectButton.Enabled = pendingList.SelectedItems.Count > 0;
            if (forgetPairedButton is not null && !forgetPairedButton.IsDisposed) forgetPairedButton.Enabled = pairedList?.SelectedItems.Count > 0;

            if (!status.IsDisposed)
            {
                status.ForeColor = TextMuted;
                status.Text = pendingDevices.Count == 0
                    ? "No Wi-Fi requests. USB devices connect automatically."
                    : $"{pendingDevices.Count} device(s) waiting for pairing approval.";
            }
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
        await PairSelectedDeviceAsync(list, pairedList: null, status, approve, approveButton: null, rejectButton: null).ConfigureAwait(true);
    }

    private async Task PairSelectedDeviceAsync(
        ListView pendingList,
        ListView? pairedList,
        Label status,
        bool approve,
        Button? approveButton,
        Button? rejectButton)
    {
        if (pendingList.SelectedItems.Count == 0 || pendingList.SelectedItems[0].Tag is not string deviceId || string.IsNullOrWhiteSpace(deviceId))
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
            await RefreshPairingListAsync(pendingList, pairedList, status, approveButton, rejectButton, forgetPairedButton: null).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            AgentDiagnosticLog.Write($"Pairing {(approve ? "approval" : "rejection")} failed: {exception.GetType().Name}");
            status.ForeColor = Color.LightSalmon;
            status.Text = approve ? "Pairing could not be approved." : "Pairing could not be rejected.";
        }
    }

    private async Task ForgetPairedDeviceAsync(
        ListView pairedList,
        ListView pendingList,
        Label status,
        Button? forgetPairedButton)
    {
        if (pairedList.SelectedItems.Count == 0 || pairedList.SelectedItems[0].Tag is not string deviceId || string.IsNullOrWhiteSpace(deviceId))
        {
            status.ForeColor = Color.Khaki;
            status.Text = "Select a paired device first.";
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Forget paired device '{deviceId}' on this PC? It will need to be approved again before it can control the agent.",
            "Forget paired device",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
        {
            AgentDiagnosticLog.Write($"Forget paired device cancelled for '{deviceId}'");
            return;
        }

        try
        {
            var forgotten = await runtime.ForgetDeviceAsync(deviceId).ConfigureAwait(true);
            status.ForeColor = forgotten ? TextMuted : Color.Khaki;
            status.Text = forgotten ? $"Device '{deviceId}' forgotten." : "Device was not found in paired list.";
            await RefreshPairingListAsync(pendingList, pairedList, status, approveButton: null, rejectButton: null, forgetPairedButton).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            AgentDiagnosticLog.Write($"Forget paired device failed: {exception.GetType().Name}");
            status.ForeColor = Color.LightSalmon;
            status.Text = "Device could not be forgotten.";
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
            AppDisplayName,
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
            AppDisplayName,
            MessageBoxButtons.OK,
            sent ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private async Task ForgetDeviceAsync()
    {
        var deviceId = state.Snapshot().DeviceId;
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            MessageBox.Show(
                this,
                "No device ID is known to forget.",
                AppDisplayName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Forget device '{deviceId}' on this PC? It will need to be approved again before it can control the agent.",
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
            var forgotten = await runtime.ForgetDeviceAsync(deviceId).ConfigureAwait(true);
            MessageBox.Show(
                this,
                forgotten ? $"Device '{deviceId}' forgotten. Reconnect it to approve pairing again." : "No active pairing record was found for the device.",
                AppDisplayName,
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
                AppDisplayName,
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

    private void ExportLogsToFile(string logContent)
    {
        using var saveDialog = new SaveFileDialog
        {
            Title = "Export logs",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"cyd-deck-logs-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };
        if (saveDialog.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                File.WriteAllText(saveDialog.FileName, logContent);
                MessageBox.Show(this, "Logs exported successfully.", AppDisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AgentDiagnosticLog.Write($"Export logs failed: {ex.GetType().Name}");
                MessageBox.Show(this, $"Failed to export logs: {ex.Message}", AppDisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
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
            ShowPage("Shortcuts");
            SelectShortcutAt(shortcuts.Count - 1);
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
            ShowPage("Shortcuts");
            SelectShortcutAt(index);
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

        var result = MessageBox.Show(this, "Delete selected shortcut?", AppDisplayName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
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

        MoveShortcutInMemory(shortcuts, index, target);
        if (await SaveShortcutsAsync(shortcuts).ConfigureAwait(true))
        {
            ShowPage("Shortcuts");
            SelectShortcutAt(target);
        }
    }

    private async Task MoveShortcutToPositionAsync(ListView list, List<ShortcutDefinition> shortcuts)
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

        var input = Microsoft.VisualBasic.Interaction.InputBox(
            $"Enter a position from 1 to {shortcuts.Count}.",
            "Move shortcut",
            (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!int.TryParse(input, out var oneBasedPosition))
        {
            return;
        }

        var target = NormalizeShortcutTargetIndex(index, oneBasedPosition, shortcuts.Count);
        if (target == index)
        {
            return;
        }

        MoveShortcutInMemory(shortcuts, index, target);
        if (await SaveShortcutsAsync(shortcuts).ConfigureAwait(true))
        {
            ShowPage("Shortcuts");
            SelectShortcutAt(target);
        }
    }

    internal static int NormalizeShortcutTargetIndex(int currentIndex, int oneBasedPosition, int count)
    {
        if (currentIndex < 0 || currentIndex >= count || count <= 0)
        {
            return -1;
        }

        return Math.Clamp(oneBasedPosition - 1, 0, count - 1);
    }

    internal static void MoveShortcutInMemory<T>(IList<T> shortcuts, int sourceIndex, int targetIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= shortcuts.Count || targetIndex < 0 || targetIndex >= shortcuts.Count || sourceIndex == targetIndex)
        {
            return;
        }

        var shortcut = shortcuts[sourceIndex];
        shortcuts.RemoveAt(sourceIndex);
        shortcuts.Insert(targetIndex, shortcut);
    }

    private void SelectShortcutAt(int index)
    {
        var list = FindControls<ListView>(contentPanel).FirstOrDefault(control => control.AccessibleName == "Agent-side shortcuts");
        if (list is null || index < 0 || index >= list.Items.Count) return;
        list.Items[index].Selected = true;
        list.Items[index].Focused = true;
        list.Items[index].EnsureVisible();
        list.Focus();
    }

    private static void ReloadShortcutList(ListView list, IReadOnlyList<ShortcutDefinition> shortcuts, int selectedIndex)
    {
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            foreach (var shortcut in shortcuts)
            {
                AddShortcutItem(list, shortcut);
            }

            list.Items[selectedIndex].Selected = true;
        }
        finally
        {
            list.EndUpdate();
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

        try { await runtime.UpdateShortcutsAsync(shortcuts).ConfigureAwait(true); }
        catch (Exception exception)
        {
            AgentDiagnosticLog.Write($"Shortcuts saved locally; sync failed: {exception.GetType().Name}");
        }
        return true;
    }

    private async Task ImportConfigurationAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import CYD-DECK configuration",
            Filter = "CYD-DECK configuration (*.json)|*.json|JSON files (*.json)|*.json|All files (*.*)|*.*"
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
            Title = "Export CYD-DECK configuration",
            FileName = "cyd-deck-config.json",
            Filter = "CYD-DECK configuration (*.json)|*.json|JSON files (*.json)|*.json|All files (*.*)|*.*"
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
        var desktopSettings = pendingDesktopAppearance;
        var settings = pendingAppearance;
        if (settings is null && desktopSettings is null)
        {
            return;
        }

        try
        {
            if (desktopSettings is not null)
            {
                SaveDesktopAppearanceConfiguration(desktopSettings);
            }

            if (settings is not null)
            {
                SaveAppearanceConfiguration(settings.EnsureForcedPagesVisible());
            }
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

        if (settings is null)
        {
            return;
        }

        var applied = await TryApplyAppearanceAsync(settings.EnsureForcedPagesVisible()).ConfigureAwait(true);
        if (appearanceStatusLabel is not null)
        {
            appearanceStatusLabel.ForeColor = applied ? TextMuted : Color.Khaki;
            appearanceStatusLabel.Text = applied
                ? "✓ Desktop UI saved locally; CYD Display settings synced with connected device."
                : "ℹ Desktop UI saved locally; CYD Display settings will sync when CYD reconnects.";
        }
    }

    private DesktopAppearanceState LoadDesktopAppearanceConfiguration()
    {
        var defaults = DesktopAppearanceState.Default;
        try
        {
            return DesktopAppearanceState.FromConfiguration(LoadConfiguration().DesktopAppearance ?? new DesktopAppearanceConfiguration());
        }
        catch (Exception exception)
        {
            AgentDiagnosticLog.Write($"Desktop appearance configuration load failed: {exception.GetType().Name}");
            return defaults;
        }
    }

    private void SaveDesktopAppearanceConfiguration(DesktopAppearanceState settings)
    {
        configurationStore.Save(LoadConfiguration() with { DesktopAppearance = settings.ToConfiguration() });
        AgentDiagnosticLog.Write("Windows Agent desktop appearance settings updated");
    }

    internal static string ResolveEffectiveDesktopTheme(string? configuredTheme)
    {
        if (string.Equals(configuredTheme, "light", StringComparison.OrdinalIgnoreCase))
        {
            return "light";
        }

        if (string.Equals(configuredTheme, "system", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("AppsUseLightTheme") is int useLight && useLight == 1)
                {
                    return "light";
                }
            }
            catch
            {
                // Fallback to dark mode if registry read is unavailable.
            }
        }

        return "dark";
    }

    private static string? ResolveDesktopWindowBackground(DesktopAppearanceState settings, string effectiveTheme)
    {
        if (string.Equals(effectiveTheme, "light", StringComparison.OrdinalIgnoreCase))
        {
            return "#F6F7F9";
        }

        return (settings.WindowStyle?.ToLowerInvariant()) switch
        {
            "minimal" => "#0B0C10",
            "gradient" => "#12151E",
            _ => "#0D0F14"
        };
    }

    private AppearanceState LoadAppearanceConfiguration()
    {
        var defaults = AppearanceState.Default;
        try
        {
            return AppearanceState.FromConfiguration(LoadConfiguration().Appearance ?? new AppearanceConfiguration()).EnsureForcedPagesVisible();
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
        AgentDiagnosticLog.Write("CYD display appearance settings updated");
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

    private Control CreateCydHardwarePreviewPanel(List<ShortcutDefinition> shortcuts, ListView? list = null)
    {
        var isDeckBuilder = list != null;
        var currentAppearance = pendingAppearance ?? LoadAppearanceConfiguration();
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = isDeckBuilder ? 218 : 162,
            BackColor = Background,
            Padding = new Padding(0, 0, 0, 10),
            ColumnCount = isDeckBuilder ? 2 : 1,
            RowCount = 3
        };
        if (isDeckBuilder)
        {
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
        }
        else
        {
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        }

        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, isDeckBuilder ? 158 : 102));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        var headerLabel = new Label
        {
            Text = isDeckBuilder
                ? "CYD HARDWARE DECK PREVIEW (320×240 WYSIWYG)"
                : "CYD HARDWARE SHORTCUT GRID (CONTROL CENTER)",
            ForeColor = Accent,
            Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        card.Controls.Add(headerLabel, 0, 0);

        if (isDeckBuilder)
        {
            var inspectorHeader = new Label
            {
                Text = "TILE INSPECTOR & SHORTCUTS LAYOUT",
                ForeColor = Accent,
                Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold),
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };
            card.Controls.Add(inspectorHeader, 1, 0);
        }

        var bezelOuter = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(18, 20, 26),
            Padding = new Padding(6, 22, 6, 5),
            Margin = new Padding(0, 0, isDeckBuilder ? 8 : 0, 0)
        };
        bezelOuter.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, bezelOuter.Width - 1, bezelOuter.Height - 1);
            using var path = CreateRoundedRectPath(rect, 12);
            using var fill = new SolidBrush(Color.FromArgb(14, 16, 21));
            using var borderPen = new Pen(Color.FromArgb(52, 58, 72), 1.2f);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(borderPen, path);

            // Simulated CYD top bar with Status Bar Clock on the right
            using var barFont = new Font(Font.FontFamily, 7F, FontStyle.Bold);
            using var mutedBrush = new SolidBrush(Color.FromArgb(148, 156, 170));
            using var accentBrush = new SolidBrush(Accent);
            e.Graphics.DrawString("Media   Skróty   PC   Info", barFont, mutedBrush, new PointF(10, 5));
            var clockStr = DateTime.Now.ToString("HH:mm");
            var clockSize = e.Graphics.MeasureString(clockStr, barFont);
            e.Graphics.DrawString(clockStr, barFont, accentBrush, new PointF(bezelOuter.Width - clockSize.Width - 10, 5));
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = Color.FromArgb(14, 16, 21),
            Padding = new Padding(3)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

        var slotButtons = new List<Button>();
        Action<int>? updateInspector = null;

        for (var i = 0; i < 6; i++)
        {
            var slotIndex = i;
            var isAssigned = slotIndex < shortcuts.Count;
            var shortcut = isAssigned ? shortcuts[slotIndex] : null;
            var cleanTitle = isAssigned ? shortcut!.Name : "Empty";
            var iconName = isAssigned ? shortcut!.Icon : "empty";
            var actionType = isAssigned ? shortcut!.Action.Type : string.Empty;

            var slotBtn = new Button
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(4),
                FlatStyle = FlatStyle.Flat,
                Font = new Font(Font.FontFamily, 8.5F, isAssigned ? FontStyle.Bold : FontStyle.Regular),
                BackColor = isAssigned ? Color.FromArgb(28, 30, 36) : Color.FromArgb(20, 22, 27),
                ForeColor = isAssigned ? TextPrimary : TextMuted,
                Text = cleanTitle,
                TextAlign = ContentAlignment.BottomCenter,
                Padding = new Padding(2, 0, 2, 5),
                Cursor = Cursors.Hand,
                AccessibleName = list != null ? $"CYD hardware slot {slotIndex + 1}" : $"CYD overview slot {slotIndex + 1}"
            };
            slotBtn.FlatAppearance.BorderSize = 1;
            slotBtn.FlatAppearance.BorderColor = isAssigned ? Color.FromArgb(52, 58, 70) : Color.FromArgb(34, 38, 46);
            slotBtn.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var iconSize = Math.Clamp(Math.Min(slotBtn.Width, slotBtn.Height) / 3, 14, 24);
                var iconY = Math.Max(5, (slotBtn.Height - 20 - iconSize) / 2);
                var iconRect = new Rectangle((slotBtn.Width - iconSize) / 2, iconY, iconSize, iconSize);
                var iconColor = isAssigned ? Accent : TextMuted;
                DrawShortcutVectorIcon(e.Graphics, iconName, actionType, iconRect, iconColor);
            };
            slotBtn.Click += (_, _) =>
            {
                if (list != null)
                {
                    if (isAssigned && slotIndex < list.Items.Count)
                    {
                        list.SelectedIndices.Clear();
                        list.Items[slotIndex].Selected = true;
                        list.Items[slotIndex].EnsureVisible();
                    }
                    updateInspector?.Invoke(slotIndex);
                }
                else
                {
                    ShowPage("Shortcuts");
                    if (isAssigned)
                    {
                        SelectShortcutAt(slotIndex);
                    }
                }
            };
            slotButtons.Add(slotBtn);
        }

        void RebuildPreviewGridLayout(string layoutMode)
        {
            var effective = (layoutMode ?? "auto").Trim().ToLowerInvariant();
            if (effective == "auto")
            {
                effective = shortcuts.Count switch
                {
                    1 => "hero-1",
                    2 => "split-2",
                    3 or 4 => "quad-4",
                    _ => "grid-6"
                };
            }

            grid.SuspendLayout();
            grid.Controls.Clear();
            grid.ColumnStyles.Clear();
            grid.RowStyles.Clear();

            if (effective == "hero-1")
            {
                grid.ColumnCount = 1;
                grid.RowCount = 1;
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                slotButtons[0].Visible = true;
                grid.Controls.Add(slotButtons[0], 0, 0);
                for (var k = 1; k < 6; k++) slotButtons[k].Visible = false;
            }
            else if (effective == "split-2")
            {
                grid.ColumnCount = 2;
                grid.RowCount = 1;
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                for (var k = 0; k < 2; k++)
                {
                    slotButtons[k].Visible = true;
                    grid.Controls.Add(slotButtons[k], k, 0);
                }
                for (var k = 2; k < 6; k++) slotButtons[k].Visible = false;
            }
            else if (effective == "quad-4")
            {
                grid.ColumnCount = 2;
                grid.RowCount = 2;
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
                for (var k = 0; k < 4; k++)
                {
                    slotButtons[k].Visible = true;
                    grid.Controls.Add(slotButtons[k], k % 2, k / 2);
                }
                for (var k = 4; k < 6; k++) slotButtons[k].Visible = false;
            }
            else
            {
                grid.ColumnCount = 3;
                grid.RowCount = 2;
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
                for (var k = 0; k < 6; k++)
                {
                    slotButtons[k].Visible = true;
                    grid.Controls.Add(slotButtons[k], k % 3, k / 3);
                }
            }

            grid.ResumeLayout(true);
        }

        RebuildPreviewGridLayout(currentAppearance.ShortcutsLayout);
        bezelOuter.Controls.Add(grid);
        card.Controls.Add(bezelOuter, 0, 1);

        var infoLabel = new Label
        {
            Text = $"Active on CYD: {Math.Min(shortcuts.Count, 6)}/6 tiles assigned • Layout: {currentAppearance.ShortcutsLayout} • {shortcuts.Count} saved on PC",
            ForeColor = TextMuted,
            Font = new Font(Font.FontFamily, 8.5F, FontStyle.Regular),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        if (isDeckBuilder && list != null)
        {
            var inspectorCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                Padding = new Padding(12, 8, 12, 8),
                Margin = new Padding(4, 0, 0, 0)
            };
            inspectorCard.Paint += (_, e) =>
            {
                using var pen = new Pen(Border, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, inspectorCard.Width - 1, inspectorCard.Height - 1);
            };

            var inspectorTitle = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = TextPrimary,
                Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold),
                Text = "Select a tile to inspect"
            };
            var inspectorSlotMeta = new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                ForeColor = Accent,
                Font = new Font(Font.FontFamily, 8F, FontStyle.Bold),
                Text = "Slot —"
            };
            var inspectorActionLine = new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                ForeColor = TextMuted,
                Font = new Font(Font.FontFamily, 8.25F, FontStyle.Regular),
                Text = "Action: —"
            };
            var inspectorTargetLine = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = TextMuted,
                Font = new Font(Font.FontFamily, 8F, FontStyle.Regular),
                Text = "Click any tile on the CYD preview or row below to view its hotkey, executable path, or script parameters."
            };

            var inspectorActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Surface
            };
            var quickEditBtn = new Button
            {
                Text = "Edit Tile…",
                Width = 82,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = SurfaceAlt,
                ForeColor = TextPrimary,
                Font = new Font(Font.FontFamily, 8F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 2, 6, 0)
            };
            quickEditBtn.FlatAppearance.BorderSize = 1;
            quickEditBtn.FlatAppearance.BorderColor = Accent;
            quickEditBtn.Click += async (_, _) => await EditShortcutAsync(list, shortcuts).ConfigureAwait(true);

            var quickTestBtn = new Button
            {
                Text = "Test",
                Width = 54,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = SurfaceAlt,
                ForeColor = TextPrimary,
                Font = new Font(Font.FontFamily, 8F, FontStyle.Regular),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 2, 8, 0)
            };
            quickTestBtn.FlatAppearance.BorderSize = 1;
            quickTestBtn.FlatAppearance.BorderColor = Border;
            quickTestBtn.Click += async (_, _) => await TestSelectedShortcutAsync(list, shortcuts).ConfigureAwait(true);

            var layoutCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 88,
                BackColor = SurfaceAlt,
                ForeColor = TextPrimary,
                Font = new Font(Font.FontFamily, 8F, FontStyle.Regular),
                Margin = new Padding(0, 3, 0, 0),
                AccessibleName = "Shortcuts layout preset"
            };
            string[] layoutPresets = ["auto", "hero-1", "split-2", "quad-4", "grid-6"];
            layoutCombo.Items.AddRange(layoutPresets.Cast<object>().ToArray());
            layoutCombo.SelectedItem = layoutPresets.FirstOrDefault(p => string.Equals(p, currentAppearance.ShortcutsLayout, StringComparison.OrdinalIgnoreCase)) ?? "auto";
            layoutCombo.SelectedIndexChanged += (_, _) =>
            {
                if (layoutCombo.SelectedItem is string mode)
                {
                    pendingAppearance ??= LoadAppearanceConfiguration();
                    pendingAppearance.ShortcutsLayout = mode;
                    RebuildPreviewGridLayout(mode);
                    infoLabel.Text = $"Active on CYD: {Math.Min(shortcuts.Count, 6)}/6 tiles assigned • Layout: {mode} • {shortcuts.Count} saved on PC";
                    QueueAppearanceSave();
                }
            };

            inspectorActions.Controls.Add(quickEditBtn);
            inspectorActions.Controls.Add(quickTestBtn);
            inspectorActions.Controls.Add(layoutCombo);

            inspectorCard.Controls.Add(inspectorTargetLine);
            inspectorCard.Controls.Add(inspectorActionLine);
            inspectorCard.Controls.Add(inspectorSlotMeta);
            inspectorCard.Controls.Add(inspectorTitle);
            inspectorCard.Controls.Add(inspectorActions);

            updateInspector = selectedIndex =>
            {
                for (var b = 0; b < slotButtons.Count; b++)
                {
                    var isSel = b == selectedIndex;
                    var hasItem = b < shortcuts.Count;
                    slotButtons[b].FlatAppearance.BorderColor = isSel
                        ? Accent
                        : (hasItem ? Color.FromArgb(52, 58, 70) : Color.FromArgb(34, 38, 46));
                }

                if (selectedIndex >= 0 && selectedIndex < shortcuts.Count)
                {
                    var item = shortcuts[selectedIndex];
                    var target = item.Action.Url
                        ?? item.Action.ExecutablePath
                        ?? item.Action.ScriptPath
                        ?? item.Action.AdapterKey
                        ?? (item.Action.Keys.Count > 0 ? string.Join(" + ", item.Action.Keys) : "—");
                    var slotBadge = selectedIndex < 6 ? $"CYD Tile #{selectedIndex + 1}" : $"Catalog #{selectedIndex + 1} (PC)";
                    inspectorTitle.Text = item.Name;
                    inspectorSlotMeta.Text = $"{slotBadge}  •  ID: {item.Id}  •  Icon: {item.Icon}";
                    inspectorActionLine.Text = $"Type: {FormatActionTypeName(item.Action.Type)}";
                    inspectorTargetLine.Text = $"Binding: {target}";
                    quickEditBtn.Enabled = true;
                    quickTestBtn.Enabled = true;
                }
                else
                {
                    inspectorTitle.Text = selectedIndex >= 0 ? $"Empty CYD Tile #{selectedIndex + 1}" : "No Tile Selected";
                    inspectorSlotMeta.Text = "Unassigned slot";
                    inspectorActionLine.Text = "Type: None";
                    inspectorTargetLine.Text = "Click 'Add' below to bind an application, URL, script, or keyboard shortcut to this CYD tile.";
                    quickEditBtn.Enabled = false;
                    quickTestBtn.Enabled = false;
                }
            };

            list.SelectedIndexChanged += (_, _) =>
            {
                var idx = GetSelectedShortcutIndex(list);
                updateInspector(idx);
            };
            updateInspector(shortcuts.Count > 0 ? 0 : -1);
            card.Controls.Add(inspectorCard, 1, 1);
        }

        card.Controls.Add(infoLabel, 0, 2);
        if (isDeckBuilder)
        {
            card.SetColumnSpan(infoLabel, 2);
        }

        return card;
    }

    private Control CreateAppleMediaControlWidget()
    {
        var card = new Panel
        {
            Dock = DockStyle.Top,
            Height = 128,
            BackColor = Surface,
            Padding = new Padding(16, 12, 16, 12),
            Margin = new Padding(0, 0, 0, 12)
        };
        card.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Border, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };

        var headerLabel = new Label
        {
            Text = "NOW PLAYING & CONTROL CENTER AUDIO",
            ForeColor = Accent,
            Font = new Font(Font.FontFamily, 8F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 10)
        };

        var artBox = new Panel
        {
            Size = new Size(64, 64),
            Location = new Point(16, 36),
            BackColor = SurfaceAlt
        };
        artBox.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = CreateRoundedRectPath(new Rectangle(0, 0, 63, 63), 12);
            using var brush = new LinearGradientBrush(new Rectangle(0, 0, 64, 64), Color.FromArgb(40, 52, 78), Color.FromArgb(22, 26, 36), 45f);
            e.Graphics.FillPath(brush, path);
            using var borderPen = new Pen(Color.FromArgb(65, 75, 95), 1f);
            e.Graphics.DrawPath(borderPen, path);
            DrawNavigationVectorIcon(e.Graphics, "Overview", new Rectangle(20, 20, 24, 24), Accent);
        };

        var trackTitleLabel = new Label
        {
            Location = new Point(94, 36),
            Size = new Size(280, 22),
            ForeColor = TextPrimary,
            Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold),
            AutoEllipsis = true,
            Text = "Ready for Playback"
        };

        var trackArtistLabel = new Label
        {
            Location = new Point(94, 58),
            Size = new Size(280, 18),
            ForeColor = TextMuted,
            Font = new Font(Font.FontFamily, 8.5F, FontStyle.Regular),
            AutoEllipsis = true,
            Text = "System Audio • Apple Music / Spotify / Browser"
        };

        var audioStatusPill = new Label
        {
            Location = new Point(94, 80),
            Size = new Size(260, 18),
            ForeColor = Accent,
            Font = new Font(Font.FontFamily, 8F, FontStyle.Bold),
            Text = "Volume 50%"
        };

        var transportFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            BackColor = Surface,
            Location = new Point(390, 42),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        Button CreateVectorTransportBtn(string iconKind, string accessibleName, bool primary, Func<Task> onClick)
        {
            var btn = new Button
            {
                Size = primary ? new Size(44, 40) : new Size(38, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : SurfaceAlt,
                ForeColor = primary ? Color.White : TextPrimary,
                Margin = new Padding(4, primary ? 0 : 2, 4, 0),
                Cursor = Cursors.Hand,
                AccessibleName = accessibleName,
                Text = string.Empty
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = primary ? Accent : Border;
            btn.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var fg = primary ? Color.White : TextPrimary;
                var cx = btn.Width / 2;
                var cy = btn.Height / 2;
                using var pen = new Pen(fg, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                using var brush = new SolidBrush(fg);
                switch (iconKind)
                {
                    case "prev":
                        e.Graphics.DrawLine(pen, cx - 5, cy - 6, cx - 5, cy + 6);
                        e.Graphics.FillPolygon(brush, [new Point(cx - 4, cy), new Point(cx + 5, cy - 6), new Point(cx + 5, cy + 6)]);
                        break;
                    case "play":
                        e.Graphics.FillPolygon(brush, [new Point(cx - 4, cy - 7), new Point(cx - 4, cy + 7), new Point(cx + 7, cy)]);
                        break;
                    case "next":
                        e.Graphics.FillPolygon(brush, [new Point(cx - 5, cy - 6), new Point(cx - 5, cy + 6), new Point(cx + 4, cy)]);
                        e.Graphics.DrawLine(pen, cx + 5, cy - 6, cx + 5, cy + 6);
                        break;
                    case "mute":
                        e.Graphics.FillPolygon(brush, [new Point(cx - 6, cy - 3), new Point(cx - 3, cy - 3), new Point(cx + 1, cy - 6), new Point(cx + 1, cy + 6), new Point(cx - 3, cy + 3), new Point(cx - 6, cy + 3)]);
                        e.Graphics.DrawLine(pen, cx + 4, cy - 3, cx + 8, cy + 3);
                        e.Graphics.DrawLine(pen, cx + 8, cy - 3, cx + 4, cy + 3);
                        break;
                    case "voldown":
                        e.Graphics.DrawLine(pen, cx - 5, cy, cx + 5, cy);
                        break;
                    case "volup":
                        e.Graphics.DrawLine(pen, cx - 5, cy, cx + 5, cy);
                        e.Graphics.DrawLine(pen, cx, cy - 5, cx, cy + 5);
                        break;
                }
            };
            btn.Click += async (_, _) => await onClick().ConfigureAwait(true);
            return btn;
        }

        transportFlow.Controls.Add(CreateVectorTransportBtn("prev", "Media Previous", false, () => runtime.ExecuteHostMediaCommandAsync(MediaCommand.Previous)));
        transportFlow.Controls.Add(CreateVectorTransportBtn("play", "Media Play Pause", true, () => runtime.ExecuteHostMediaCommandAsync(MediaCommand.PlayPause)));
        transportFlow.Controls.Add(CreateVectorTransportBtn("next", "Media Next", false, () => runtime.ExecuteHostMediaCommandAsync(MediaCommand.Next)));
        transportFlow.Controls.Add(CreateVectorTransportBtn("mute", "Audio Mute", false, () => runtime.ToggleHostMuteAsync()));
        transportFlow.Controls.Add(CreateVectorTransportBtn("voldown", "Volume Down", false, async () =>
        {
            var currentVol = state.Snapshot().CurrentAudio?.VolumePercent ?? 50;
            await runtime.SetHostVolumeAsync(Math.Max(0, currentVol - 5)).ConfigureAwait(true);
        }));
        transportFlow.Controls.Add(CreateVectorTransportBtn("volup", "Volume Up", false, async () =>
        {
            var currentVol = state.Snapshot().CurrentAudio?.VolumePercent ?? 50;
            await runtime.SetHostVolumeAsync(Math.Min(100, currentVol + 5)).ConfigureAwait(true);
        }));

        card.SizeChanged += (_, _) =>
        {
            transportFlow.Location = new Point(Math.Max(360, card.ClientSize.Width - transportFlow.Width - 16), 44);
            var textWidth = Math.Max(140, transportFlow.Left - 106);
            trackTitleLabel.Width = textWidth;
            trackArtistLabel.Width = textWidth;
            audioStatusPill.Width = textWidth;
        };

        void RefreshMediaCard()
        {
            if (card.IsDisposed) return;
            var snap = state.Snapshot();
            var media = snap.CurrentMedia;
            var audio = snap.CurrentAudio;
            trackTitleLabel.Text = media is not null && !string.IsNullOrWhiteSpace(media.Title) ? media.Title : "Ready for Playback";
            trackArtistLabel.Text = media is not null && !string.IsNullOrWhiteSpace(media.Artist)
                ? $"{media.Artist}{(!string.IsNullOrWhiteSpace(media.AlbumTitle) ? $" — {media.AlbumTitle}" : string.Empty)}"
                : $"{(media is not null ? media.PlaybackState.ToString() : "Idle")} • Windows Media Session";
            audioStatusPill.Text = audio is not null
                ? $"Output Volume: {audio.VolumePercent}%{(audio.IsMuted ? " (Muted)" : string.Empty)}"
                : "Output Volume: Ready";
        }

        RefreshMediaCard();
        var prevRefresh = refreshOverviewWidgets;
        refreshOverviewWidgets = () =>
        {
            prevRefresh?.Invoke();
            RefreshMediaCard();
        };

        card.Controls.Add(headerLabel);
        card.Controls.Add(artBox);
        card.Controls.Add(trackTitleLabel);
        card.Controls.Add(trackArtistLabel);
        card.Controls.Add(audioStatusPill);
        card.Controls.Add(transportFlow);
        return card;
    }

    private Control CreateAppleSystemMonitorWidget()
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 136,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Background,
            Margin = new Padding(0, 0, 0, 12)
        };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var gaugePanels = new List<Panel>();

        Panel CreateMetricCard(string title, string unit, Color ringColor, Func<HostSystemTelemetrySnapshot?, (int Value, string Subtext, IReadOnlyList<double> History)> selector)
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                Margin = new Padding(4)
            };
            card.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var borderPen = new Pen(Border, 1);
                e.Graphics.DrawRectangle(borderPen, 0, 0, card.Width - 1, card.Height - 1);

                var telemetry = state.Snapshot().SystemTelemetry;
                var (val, sub, history) = selector(telemetry);
                var clamped = Math.Clamp(val, 0, 100);

                // Circular Apple Watch / Activity Monitor ring on the left
                var ringRect = new Rectangle(12, 28, 52, 52);
                using var trackPen = new Pen(SurfaceAlt, 5.5f);
                using var valuePen = new Pen(ringColor, 5.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                e.Graphics.DrawArc(trackPen, ringRect, 0, 360);
                var sweep = (int)Math.Round(clamped * 3.6);
                if (sweep > 0)
                {
                    e.Graphics.DrawArc(valuePen, ringRect, -90, sweep);
                }

                // Title & Value
                using var titleBrush = new SolidBrush(TextMuted);
                using var valueBrush = new SolidBrush(TextPrimary);
                using var titleFont = new Font(Font.FontFamily, 7.5F, FontStyle.Bold);
                using var valueFont = new Font(Font.FontFamily, 13F, FontStyle.Bold);
                using var subFont = new Font(Font.FontFamily, 7.5F, FontStyle.Regular);

                e.Graphics.DrawString(title, titleFont, titleBrush, new PointF(12, 8));
                e.Graphics.DrawString($"{val}{unit}", valueFont, valueBrush, new PointF(74, 28));
                e.Graphics.DrawString(sub, subFont, titleBrush, new PointF(74, 52));

                // Subtle sparkline along the bottom
                var sparkRect = new Rectangle(12, card.Height - 28, Math.Max(40, card.Width - 24), 18);
                if (history.Count > 1 && sparkRect.Width > 12)
                {
                    var pts = new PointF[history.Count];
                    for (var i = 0; i < history.Count; i++)
                    {
                        var x = sparkRect.Left + (i / (float)(history.Count - 1)) * sparkRect.Width;
                        var y = sparkRect.Bottom - ((float)Math.Clamp(history[i], 0.0, 100.0) / 100f) * sparkRect.Height;
                        pts[i] = new PointF(x, y);
                    }
                    using var sparkPen = new Pen(Color.FromArgb(180, ringColor), 1.5f);
                    e.Graphics.DrawLines(sparkPen, pts);
                }
            };
            gaugePanels.Add(card);
            return card;
        }

        host.Controls.Add(CreateMetricCard("CPU LOAD", "%", Accent, t => ((int)Math.Round(t?.CpuPercent ?? 0), "Processor", t?.CpuHistory ?? [])), 0, 0);
        host.Controls.Add(CreateMetricCard("MEMORY", "%", AppleGreen, t =>
        {
            var gbUsed = (t?.RamUsedBytes ?? 0UL) / (1024.0 * 1024.0 * 1024.0);
            var gbTotal = (t?.RamTotalBytes ?? 0UL) / (1024.0 * 1024.0 * 1024.0);
            return ((int)Math.Round(t?.RamPercent ?? 0), gbTotal > 0 ? $"{gbUsed:0.0}/{gbTotal:0} GB" : "System RAM", t?.RamHistory ?? []);
        }), 1, 0);
        host.Controls.Add(CreateMetricCard("GPU ENGINE", "%", ApplePurple, t => ((int)Math.Round(t?.GpuPercent ?? 0), "Graphics", t?.GpuHistory ?? [])), 2, 0);
        host.Controls.Add(CreateMetricCard("THERMAL", "°C", AppleAmber, t => ((int)Math.Round(t?.TempCelsius ?? 42), "Core Temp", t?.TempHistory ?? [])), 3, 0);

        var prevRefresh = refreshOverviewWidgets;
        refreshOverviewWidgets = () =>
        {
            prevRefresh?.Invoke();
            foreach (var p in gaugePanels)
            {
                if (!p.IsDisposed) p.Invalidate();
            }
        };

        return host;
    }

    private static GraphicsPath CreateRoundedRectPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawNavigationVectorIcon(Graphics g, string page, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var x = bounds.X;
        var y = bounds.Y;
        var w = bounds.Width;
        var h = bounds.Height;
        switch (page)
        {
            case "Overview":
                g.DrawRectangle(pen, x + 1, y + 1, w / 2 - 2, h / 2 - 2);
                g.DrawRectangle(pen, x + w / 2 + 1, y + 1, w / 2 - 2, h / 2 - 2);
                g.DrawRectangle(pen, x + 1, y + h / 2 + 1, w / 2 - 2, h / 2 - 2);
                g.DrawRectangle(pen, x + w / 2 + 1, y + h / 2 + 1, w / 2 - 2, h / 2 - 2);
                break;
            case "Shortcuts":
                g.DrawRectangle(pen, x + 1, y + 2, w - 2, h - 4);
                g.DrawLine(pen, x + 4, y + h / 2, x + w - 4, y + h / 2);
                g.DrawLine(pen, x + w / 2, y + 4, x + w / 2, y + h - 4);
                break;
            case "Appearance":
                g.DrawEllipse(pen, x + 1, y + 1, w - 2, h - 2);
                g.DrawEllipse(pen, x + w / 2 - 2, y + h / 2 - 2, 4, 4);
                break;
            case "Device":
                g.DrawRectangle(pen, x + 1, y + 2, w - 2, h - 6);
                g.DrawLine(pen, x + 4, y + h - 1, x + w - 4, y + h - 1);
                break;
            case "Pairing":
                g.DrawEllipse(pen, x + 1, y + 3, w / 2, h - 6);
                g.DrawEllipse(pen, x + w / 2 - 1, y + 3, w / 2, h - 6);
                break;
            case "Updates":
                g.DrawEllipse(pen, x + 1, y + 1, w - 2, h - 2);
                g.DrawLine(pen, x + w / 2, y + 4, x + w / 2, y + h - 4);
                g.DrawLine(pen, x + w / 2 - 3, y + 7, x + w / 2, y + 4);
                g.DrawLine(pen, x + w / 2 + 3, y + 7, x + w / 2, y + 4);
                break;
            case "Logs":
                g.DrawLine(pen, x + 2, y + 4, x + w - 2, y + 4);
                g.DrawLine(pen, x + 2, y + h / 2, x + w - 4, y + h / 2);
                g.DrawLine(pen, x + 2, y + h - 4, x + w - 6, y + h - 4);
                break;
        }
    }

    private static void DrawShortcutVectorIcon(Graphics g, string? iconName, string? actionType, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(color);
        var x = bounds.X;
        var y = bounds.Y;
        var w = bounds.Width;
        var h = bounds.Height;
        var key = (iconName ?? string.Empty).Trim().ToLowerInvariant();
        if (key == "empty")
        {
            g.DrawLine(pen, x + w / 2 - 3, y + h / 2, x + w / 2 + 3, y + h / 2);
            g.DrawLine(pen, x + w / 2, y + h / 2 - 3, x + w / 2, y + h / 2 + 3);
            return;
        }

        if (key.Contains("play") || key.Contains("media"))
        {
            g.FillPolygon(brush, [new Point(x + 3, y + 2), new Point(x + 3, y + h - 2), new Point(x + w - 2, y + h / 2)]);
        }
        else if (key.Contains("mute") || key.Contains("vol") || key.Contains("audio"))
        {
            g.DrawRectangle(pen, x + 2, y + 5, 4, h - 10);
            g.DrawLine(pen, x + 6, y + 5, x + 11, y + 2);
            g.DrawLine(pen, x + 6, y + h - 5, x + 11, y + h - 2);
            g.DrawLine(pen, x + 11, y + 2, x + 11, y + h - 2);
        }
        else if (string.Equals(actionType, ShortcutActionTypes.OpenUrl, StringComparison.OrdinalIgnoreCase) || key.Contains("web") || key.Contains("url"))
        {
            g.DrawEllipse(pen, x + 1, y + 1, w - 2, h - 2);
            g.DrawLine(pen, x + 2, y + h / 2, x + w - 2, y + h / 2);
        }
        else if (string.Equals(actionType, ShortcutActionTypes.Hotkey, StringComparison.OrdinalIgnoreCase) || key.Contains("key"))
        {
            g.DrawRectangle(pen, x + 1, y + 3, w - 2, h - 6);
            g.DrawLine(pen, x + 4, y + h / 2, x + w - 4, y + h / 2);
        }
        else
        {
            g.DrawRectangle(pen, x + 2, y + 2, w - 4, h - 4);
            g.DrawLine(pen, x + 5, y + 6, x + 9, y + h / 2);
            g.DrawLine(pen, x + 9, y + h / 2, x + 5, y + h - 6);
        }
    }

    private async Task TestSelectedShortcutAsync(ListView list, List<ShortcutDefinition> shortcuts)
    {
        var index = GetSelectedShortcutIndex(list);
        if (index < 0 || index >= shortcuts.Count)
        {
            MessageBox.Show(this, "Select a shortcut from the list to test.", "Test action", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var shortcut = shortcuts[index];
        try
        {
            var action = shortcut.Action;
            if (string.Equals(action.Type, ShortcutActionTypes.OpenUrl, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(action.Url))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(action.Url) { UseShellExecute = true });
                MessageBox.Show(this, $"Opening URL in default browser:\n{action.Url}", "Test action", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (string.Equals(action.Type, ShortcutActionTypes.LaunchApplication, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(action.ExecutablePath))
            {
                var psi = new System.Diagnostics.ProcessStartInfo(action.ExecutablePath, string.Join(" ", action.Arguments)) { UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
                MessageBox.Show(this, $"Launched application:\n{action.ExecutablePath}", "Test action", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (string.Equals(action.Type, ShortcutActionTypes.Hotkey, StringComparison.Ordinal) && action.Keys.Count > 0)
            {
                var sender = new Runtime.WindowsHotkeySender();
                await sender.SendAsync(action.Keys, CancellationToken.None).ConfigureAwait(true);
                MessageBox.Show(this, $"Hotkey executed: {string.Join("+", action.Keys)}", "Test action", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (string.Equals(action.Type, ShortcutActionTypes.Script, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(action.ScriptPath))
            {
                var psi = new System.Diagnostics.ProcessStartInfo(action.ScriptPath, string.Join(" ", action.Arguments)) { UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
                MessageBox.Show(this, $"Running script:\n{action.ScriptPath}", "Test action", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, $"Action configuration is valid: {shortcut.Action.Type}", "Test action", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Shortcut execution error: {ex.Message}", "Test action", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    internal static string FormatActionTypeName(string actionType) => actionType switch
    {
        ShortcutActionTypes.LaunchApplication => "Launch application",
        ShortcutActionTypes.OpenUrl => "Open URL",
        ShortcutActionTypes.Hotkey => "Send hotkey",
        ShortcutActionTypes.Script => "Run script",
        ShortcutActionTypes.PowerShell => "Run script (PowerShell)",
        ShortcutActionTypes.PredefinedAdapter => "Built-in action",
        _ => actionType
    };

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
        return new ListViewItem([shortcut.Name, shortcut.Id, FormatActionTypeName(shortcut.Action.Type), target]);
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

        UpdateSidebarConnectionPill();

        var currentSnapshot = state.Snapshot();
        var telemetryAvailable = currentSnapshot.HostStatus == "connected" && currentSnapshot.DeviceInfo is not null;
        var currentUpdateIp = currentSnapshot.HostStatus == "connected" ? currentSnapshot.DeviceInfo?.Wifi.Ip : null;
        if ((currentPage == "Device" && telemetryAvailable != deviceTelemetryVisible)
            || (currentPage == "Updates" && currentUpdateIp != updateDeviceIp))
        {
            ShowPage(currentPage);
            return;
        }

        if (ShouldRebuildPageOnStateChanged(currentPage))
        {
            ShowPage(currentPage);
            return;
        }

        if (string.Equals(currentPage, "Overview", StringComparison.Ordinal))
        {
            refreshOverviewWidgets?.Invoke();
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
            var current = state.Snapshot();
            UpdateDeviceActionAvailability(contentPanel, !string.IsNullOrWhiteSpace(current.DeviceId), current.HostStatus == "connected");
        }

        if (string.Equals(currentPage, "Appearance", StringComparison.Ordinal) && appearanceStatusLabel is not null && !appearanceStatusLabel.IsDisposed)
        {
            var isConnected = string.Equals(state.Snapshot().HostStatus, "connected", StringComparison.OrdinalIgnoreCase);
            if (!appearanceSaveTimer.Enabled)
            {
                appearanceStatusLabel.Text = GetAppearanceSyncStatusText(isConnected, state.Snapshot().DeviceName);
                appearanceStatusLabel.ForeColor = isConnected ? TextMuted : Color.Khaki;
            }
        }
    }

    internal static bool ShouldRebuildPageOnStateChanged(string page)
    {
        return false;
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
        var deviceInfo = snapshot.HostStatus == "connected" ? snapshot.DeviceInfo : null;

        if (string.Equals(page, "Overview", StringComparison.Ordinal))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Connection headline"] = AgentStatusFormatter.ToDeviceStatus(snapshot),
                ["Connection detail"] = GetConnectionDetail(snapshot),
                ["Transport"] = snapshot.RemoteAddress == "USB" ? "USB" : snapshot.HostStatus == "connected" ? "Network" : "Disconnected",
                ["CYD"] = AgentStatusFormatter.ToDeviceStatus(snapshot),
                ["Telemetry"] = snapshot.HostStatus == "connected" ? "Waiting for device report…" : "Unavailable while offline (connect device via USB or Wi-Fi)",
                ["Firmware"] = FormatUnavailable(deviceInfo?.FirmwareVersion),
                ["Wi-Fi"] = FormatWifi(deviceInfo?.Wifi),
                ["SSID"] = FormatUnavailable(deviceInfo?.Wifi.Ssid),
                ["RSSI"] = FormatRssi(deviceInfo?.Wifi.Rssi),
                ["Device IP"] = FormatUnavailable(deviceInfo?.Wifi.Ip),
                ["Agent host"] = FormatUnavailable(deviceInfo?.AgentHost),
                ["Uptime"] = FormatUptime(deviceInfo?.UptimeSeconds),
                ["Agent service"] = AgentStatusFormatter.ToUserStatus(snapshot.HostStatus),
                ["Device name"] = snapshot.DeviceName ?? "Not paired",
                ["Device ID"] = snapshot.DeviceId ?? "None",
                ["Last seen"] = AgentStatusFormatter.FormatLastSeen(snapshot),
                ["Mode"] = snapshot.Mode,
                ["LAN IP"] = snapshot.LanAddresses.Count == 0 ? "Unavailable" : string.Join(", ", snapshot.LanAddresses),
                ["Port"] = snapshot.HostPort.ToString(),
                ["Audio"] = deviceInfo?.Audio is not null ? $"Volume {deviceInfo.Audio.VolumePercent}%{(deviceInfo.Audio.Muted ? " (Muted)" : "")}" : "Unavailable",
                ["Artwork"] = deviceInfo?.Cover is not null ? (deviceInfo.Cover.Rendered ? "Active / Rendered" : $"Status {deviceInfo.Cover.Status}") : "Unavailable"
            };
        }

        if (string.Equals(page, "Device", StringComparison.Ordinal))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Device name"] = snapshot.DeviceName ?? "Unavailable",
                ["Device ID"] = snapshot.DeviceId ?? "Unavailable",
                ["Connection headline"] = AgentStatusFormatter.ToDeviceStatus(snapshot),
                ["Connection detail"] = GetConnectionDetail(snapshot),
                ["Status"] = AgentStatusFormatter.ToDeviceStatus(snapshot),
                ["Telemetry"] = snapshot.HostStatus == "connected" ? "Waiting for device report…" : "Unavailable while offline (connect device via USB or Wi-Fi)",
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
                ["Audio"] = deviceInfo?.Audio is not null ? $"Volume {deviceInfo.Audio.VolumePercent}%{(deviceInfo.Audio.Muted ? " (Muted)" : "")}" : "Unavailable",
                ["Artwork"] = deviceInfo?.Cover is not null ? (deviceInfo.Cover.Rendered ? "Active / Rendered" : $"Status {deviceInfo.Cover.Status}") : "Unavailable",
                ["Remote IP"] = FormatRemoteHost(snapshot.RemoteAddress),
                ["Remote endpoint"] = snapshot.RemoteAddress ?? "Unavailable",
                ["Agent service"] = AgentStatusFormatter.ToUserStatus(snapshot.HostStatus),
                ["Last seen"] = AgentStatusFormatter.FormatLastSeen(snapshot),
                ["Agent port"] = snapshot.HostPort.ToString(),
                ["Dashboard port"] = snapshot.DashboardPort == 0 ? "Unavailable" : snapshot.DashboardPort.ToString()
            };
        }

        if (page == "Updates")
        {
            var connected = snapshot.HostStatus == "connected";
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Firmware version"] = connected ? snapshot.DeviceInfo?.FirmwareVersion ?? "Waiting for device report…" : "Device offline",
                ["Device IP"] = connected ? FormatUnavailable(snapshot.DeviceInfo?.Wifi.Ip) : "Unavailable"
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
        if (IsDisposed || !IsHandleCreated || currentPage != "Pairing") return;
        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => { if (currentPage == "Pairing") refreshPairingLists?.Invoke(); }));
        }
        else refreshPairingLists?.Invoke();
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

    private sealed class DesktopAppearanceState
    {
        public static DesktopAppearanceState Default => new()
        {
            Theme = "dark",
            WindowStyle = "acrylic",
            Accent = "#5299E0"
        };

        public string Theme { get; set; } = "dark";
        public string WindowStyle { get; set; } = "acrylic";
        public string Accent { get; set; } = "#5299E0";

        public static DesktopAppearanceState FromConfiguration(DesktopAppearanceConfiguration configuration)
        {
            return new DesktopAppearanceState
            {
                Theme = string.IsNullOrWhiteSpace(configuration.Theme) ? "dark" : configuration.Theme,
                WindowStyle = string.IsNullOrWhiteSpace(configuration.WindowStyle) ? "acrylic" : configuration.WindowStyle,
                Accent = string.IsNullOrWhiteSpace(configuration.Accent) ? "#5299E0" : configuration.Accent
            };
        }

        public DesktopAppearanceConfiguration ToConfiguration()
        {
            return new DesktopAppearanceConfiguration
            {
                Theme = Theme,
                WindowStyle = WindowStyle,
                Accent = Accent
            };
        }
    }

    private sealed class AppearanceState
    {
        public static AppearanceState Default => new()
        {
            Theme = "dark",
            Accent = "#0A84FF",
            Background = "#101216",
            ButtonStyle = "rounded",
            TileColorScheme = "control-center",
            StandbyStyle = "apple-standby",
            ShortcutsLayout = "auto",
            PageOrder = DefaultAppearancePageOrder.ToArray(),
            PageVisibility = DefaultAppearancePageOrder.ToArray(),
            Brightness = 70,
            DimAfterSec = 300,
            DimBrightness = 20,
            ScreenOffAfterSec = 1800,
            WakeOnTouch = true
        };

        public string Theme { get; set; } = "dark";
        public string Accent { get; set; } = "#0A84FF";
        public string Background { get; set; } = "#101216";
        public string ButtonStyle { get; set; } = "rounded";
        public string TileColorScheme { get; set; } = "control-center";
        public string StandbyStyle { get; set; } = "apple-standby";
        public string ShortcutsLayout { get; set; } = "auto";
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
                TileColorScheme = string.IsNullOrWhiteSpace(configuration.TileColorScheme) ? "control-center" : configuration.TileColorScheme,
                StandbyStyle = string.IsNullOrWhiteSpace(configuration.StandbyStyle) ? "apple-standby" : configuration.StandbyStyle,
                ShortcutsLayout = string.IsNullOrWhiteSpace(configuration.ShortcutsLayout) ? "auto" : configuration.ShortcutsLayout,
                PageOrder = NormalizeAppearancePageOrder(configuration.PageOrder),
                PageVisibility = NormalizeAppearancePageVisibility(NormalizeAppearancePageOrder(configuration.PageOrder), configuration.PageVisibility),
                Brightness = configuration.Brightness,
                DimAfterSec = configuration.DimAfterSec,
                DimBrightness = configuration.DimBrightness,
                ScreenOffAfterSec = configuration.ScreenOffAfterSec,
                WakeOnTouch = configuration.WakeOnTouch
            };
        }

        public AppearanceState EnsureForcedPagesVisible()
        {
            PageOrder = NormalizeAppearancePageOrder(PageOrder);
            PageVisibility = NormalizeAppearancePageVisibility(PageOrder, PageVisibility);

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
                TileColorScheme = TileColorScheme,
                StandbyStyle = StandbyStyle,
                ShortcutsLayout = ShortcutsLayout,
                PageOrder = NormalizeAppearancePageOrder(PageOrder),
                PageVisibility = NormalizeAppearancePageVisibility(NormalizeAppearancePageOrder(PageOrder), PageVisibility),
                Brightness = Brightness,
                DimAfterSec = DimAfterSec,
                DimBrightness = DimBrightness,
                ScreenOffAfterSec = ScreenOffAfterSec,
                WakeOnTouch = WakeOnTouch
            };
        }
    }
}
