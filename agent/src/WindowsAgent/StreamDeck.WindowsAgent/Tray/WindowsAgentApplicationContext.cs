using StreamDeck.WindowsAgent.Runtime;
using StreamDeck.WindowsAgent.Shell;
using StreamDeck.Host;

namespace StreamDeck.WindowsAgent.Tray;

public sealed class WindowsAgentApplicationContext : ApplicationContext
{
    private readonly AgentState state;
    private readonly WindowsAgentRuntime runtime;
    private readonly LocalDashboardServer dashboard;
    private readonly WindowsStartupService startup;
    private readonly IPairingApprovalService pairing;
    private readonly Control uiControl;
    private readonly Icon appIcon;
    private readonly NotifyIcon notifyIcon;
    private readonly ToolStripMenuItem statusItem = new("Device: Disconnected");
    private readonly ToolStripMenuItem deviceItem = new("Device: none");
    private readonly ToolStripMenuItem ipItem = new("IP: none");
    private readonly ToolStripMenuItem startWithWindowsItem = new("Start with Windows");
    private readonly RegisteredWaitHandle singleInstanceWait;
    private SettingsWindow? mainWindow;
    private bool exiting;

    public WindowsAgentApplicationContext(
        AgentState state,
        WindowsAgentRuntime runtime,
        LocalDashboardServer dashboard,
        WindowsStartupService startup,
        IPairingApprovalService pairing,
        SingleInstanceGuard singleInstance,
        AgentOptions options)
    {
        this.state = state;
        this.runtime = runtime;
        this.dashboard = dashboard;
        this.startup = startup;
        this.pairing = pairing;

        uiControl = new Control();
        uiControl.CreateControl();

        appIcon = AppIconFactory.CreateIcon();

        notifyIcon = new NotifyIcon
        {
            Icon = appIcon,
            Text = "PC Dock",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        notifyIcon.MouseUp += OnNotifyIconMouseUp;
        state.Changed += (_, _) => PostUpdateMenu();
        singleInstanceWait = ThreadPool.RegisterWaitForSingleObject(
            singleInstance.ShowWindowWaitHandle,
            (_, _) => PostShowMainWindow(),
            state: null,
            Timeout.InfiniteTimeSpan,
            executeOnlyOnce: false);
        AgentDiagnosticLog.Write("ApplicationContext constructed");
        _ = Task.Run(StartAsync);
        if (!options.StartMinimized)
        {
            PostShowMainWindow();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            exiting = true;
            singleInstanceWait.Unregister(null);
            mainWindow?.Dispose();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            appIcon.Dispose();
            uiControl.Dispose();
        }

        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildMenu()
    {
        statusItem.Enabled = false;
        deviceItem.Enabled = false;
        ipItem.Enabled = false;
        startWithWindowsItem.CheckOnClick = true;
        startWithWindowsItem.Checked = startup.IsEnabled();
        startWithWindowsItem.Click += (_, _) => startup.SetEnabled(startWithWindowsItem.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add("PC Dock").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open PC Dock", null, (_, _) => ShowMainWindow());
        menu.Items.Add(statusItem);
        menu.Items.Add(deviceItem);
        menu.Items.Add(ipItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Reconnect", null, async (_, _) => await ReconnectAsync().ConfigureAwait(true));
        menu.Items.Add(startWithWindowsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await ExitAsync().ConfigureAwait(true));
        return menu;
    }

    private async Task StartAsync()
    {
        try
        {
            AgentDiagnosticLog.Write("Startup begin");
            await dashboard.StartAsync().ConfigureAwait(false);
            AgentDiagnosticLog.Write($"Dashboard started port={dashboard.Port}");
            await runtime.StartAsync().ConfigureAwait(false);
            AgentDiagnosticLog.Write($"Runtime started port={runtime.Port}");
            PostUpdateMenu();
        }
        catch (Exception exception)
        {
            AgentDiagnosticLog.Write($"Startup fault {exception}");
            state.SetFaulted($"faulted: {exception.GetType().Name}");
            PostUpdateMenu();
        }
    }

    private async Task ReconnectAsync()
    {
        try
        {
            await runtime.ReconnectAsync().ConfigureAwait(false);
            PostUpdateMenu();
        }
        catch (Exception exception)
        {
            state.SetFaulted($"faulted: {exception.GetType().Name}");
        }
    }

    private async Task ExitAsync()
    {
        if (exiting)
        {
            return;
        }

        exiting = true;
        singleInstanceWait.Unregister(null);
        notifyIcon.Visible = false;

        try
        {
            mainWindow?.CloseForExit();
            await dashboard.DisposeAsync().ConfigureAwait(true);
            await runtime.DisposeAsync().ConfigureAwait(true);
        }
        finally
        {
            ExitThread();
        }
    }

    private void ShowMainWindow()
    {
        if (exiting)
        {
            return;
        }

        try
        {
            AgentDiagnosticLog.Write("ShowMainWindow executing");
            if (mainWindow is null || mainWindow.IsDisposed)
            {
                mainWindow = new SettingsWindow(state, runtime, dashboard, startup, pairing, appIcon);
            }

            if (!mainWindow.Visible)
            {
                mainWindow.Show();
            }

            if (mainWindow.WindowState == FormWindowState.Minimized)
            {
                mainWindow.WindowState = FormWindowState.Normal;
            }

            mainWindow.ShowAndActivateWindow();
            AgentDiagnosticLog.Write("ShowMainWindow completed");
        }
        catch (Exception ex)
        {
            AgentDiagnosticLog.Write($"ShowMainWindow error: {ex}");
        }
    }

    private void OnNotifyIconMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ShowMainWindow();
        }
    }

    private void UpdateMenu()
    {
        if (exiting)
        {
            return;
        }

        if (notifyIcon.ContextMenuStrip is null)
        {
            return;
        }

        var snapshot = state.Snapshot();
        statusItem.Text = $"Device: {AgentStatusFormatter.ToDeviceStatus(snapshot)}";
        deviceItem.Text = $"Device: {snapshot.DeviceName ?? "none"}";
        ipItem.Text = snapshot.LanAddresses.Count == 0
            ? "IP: none"
            : $"IP: {string.Join(", ", snapshot.LanAddresses)}";
        notifyIcon.Text = TrimNotifyText($"PC Dock - {AgentStatusFormatter.ToUserStatus(snapshot.HostStatus)}");
    }

    private void PostUpdateMenu()
    {
        if (exiting)
        {
            return;
        }

        if (uiControl.IsHandleCreated && uiControl.InvokeRequired)
        {
            uiControl.BeginInvoke(new Action(UpdateMenu));
            return;
        }

        UpdateMenu();
    }

    private void PostShowMainWindow()
    {
        if (exiting)
        {
            return;
        }

        AgentDiagnosticLog.Write("PostShowMainWindow triggered");
        if (uiControl.IsHandleCreated && uiControl.InvokeRequired)
        {
            uiControl.BeginInvoke(new Action(ShowMainWindow));
            return;
        }

        ShowMainWindow();
    }

    private static string TrimNotifyText(string value)
    {
        return value.Length <= 63 ? value : value[..63];
    }
}
