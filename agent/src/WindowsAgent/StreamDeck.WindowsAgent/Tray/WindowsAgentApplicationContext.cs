using StreamDeck.WindowsAgent.Updates;
using StreamDeck.WindowsAgent.Runtime;
using StreamDeck.WindowsAgent.Shell;
using StreamDeck.Host;
using StreamDeck.Media;
using StreamDeck.Protocol;

namespace StreamDeck.WindowsAgent.Tray;

public sealed class WindowsAgentApplicationContext : ApplicationContext
{
    private readonly AgentState state;
    private readonly WindowsAgentRuntime runtime;
    private readonly WindowsStartupService startup;
    private readonly IPairingApprovalService pairing;
    private readonly Control uiControl;
    private readonly Icon appIcon;
    private readonly NotifyIcon notifyIcon;
    private readonly ToolStripMenuItem statusItem = new("Device: Disconnected");
    private readonly ToolStripMenuItem deviceItem = new("Device: none");
    private readonly ToolStripMenuItem ipItem = new("IP: none");
    private readonly ToolStripSeparator mediaSeparator = new();
    private readonly ToolStripMenuItem mediaStatusItem = new("🎵 No active media");
    private readonly ToolStripMenuItem mediaPlayPauseItem = new("▶ Play / Pause");
    private readonly ToolStripMenuItem mediaPrevItem = new("⏮ Previous Track");
    private readonly ToolStripMenuItem mediaNextItem = new("⏭ Next Track");
    private readonly ToolStripMenuItem mediaMuteItem = new("🔊 Mute");
    private readonly ToolStripMenuItem startWithWindowsItem = new("Start with Windows");
    private readonly RegisteredWaitHandle singleInstanceWait;
    private SettingsWindow? mainWindow;
    private bool exiting;
    private bool resourcesDisposed;
    private readonly UpdateManager updateManager;
    private readonly CancellationTokenSource updateLifetime = new();
    private string? lastKnownHostStatus;
    private string? balloonTipTargetPage;

    public WindowsAgentApplicationContext(
        AgentState state,
        WindowsAgentRuntime runtime,
        WindowsStartupService startup,
        IPairingApprovalService pairing,
        SingleInstanceGuard singleInstance,
        AgentOptions options,
        UpdateManager? updateManager = null)
    {
        this.updateManager = updateManager ?? new UpdateManager();
        this.state = state;
        this.runtime = runtime;
        this.startup = startup;
        this.pairing = pairing;

        uiControl = new Control();
        uiControl.CreateControl();

        appIcon = AppIconFactory.CreateIcon();

        notifyIcon = new NotifyIcon
        {
            Icon = appIcon,
            Text = "CYD-DECK",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        notifyIcon.MouseUp += OnNotifyIconMouseUp;
        notifyIcon.BalloonTipClicked += (_, _) =>
        {
            if (balloonTipTargetPage is not null)
            {
                var target = balloonTipTargetPage;
                balloonTipTargetPage = null;
                ShowMainWindow(target);
            }
            else
            {
                ShowMainWindow();
            }
        };
        state.Changed += (_, _) => PostUpdateMenu();
        singleInstanceWait = ThreadPool.RegisterWaitForSingleObject(
            singleInstance.ShowWindowWaitHandle,
            (_, _) => PostShowMainWindow(),
            state: null,
            Timeout.InfiniteTimeSpan,
            executeOnlyOnce: false);
        AgentDiagnosticLog.Write("ApplicationContext constructed");
        _ = Task.Run(StartAsync);
        // Mock fixtures remain offline unless explicitly supplied a fake update manager.
        if (!options.MockMode || updateManager is not null) _ = RunAutomaticUpdateCheckAsync();
        if (!options.StartMinimized)
        {
            PostShowMainWindow();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            exiting = true;
            updateLifetime.Cancel();
            updateManager.Dispose();
            updateLifetime.Dispose();
            singleInstanceWait.Unregister(null);
            mainWindow?.Dispose();
            notifyIcon.Visible = false;
            var menu = notifyIcon.ContextMenuStrip; notifyIcon.ContextMenuStrip = null;
            menu?.Dispose();
            notifyIcon.Dispose();
            appIcon.Dispose();
            uiControl.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task RunAutomaticUpdateCheckAsync()
    {
        try
        {
            await updateManager.CheckAutomaticallyAsync(
                () => state.Snapshot().DeviceInfo?.FirmwareVersion,
                TimeSpan.FromSeconds(3), updateLifetime.Token).ConfigureAwait(false);
            if (exiting) return;
            uiControl.BeginInvoke(new Action(() =>
            {
                if (!exiting) mainWindow?.RefreshUpdateStatus();
            }));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AgentDiagnosticLog.Write($"Background auto-update check error: {ex.Message}");
        }
    }
    private ContextMenuStrip BuildMenu()
    {
        statusItem.Enabled = false;
        deviceItem.Enabled = false;
        ipItem.Enabled = false;
        startWithWindowsItem.CheckOnClick = true;
        startWithWindowsItem.Checked = startup.IsEnabled();
        startWithWindowsItem.Click += (_, _) => startup.SetEnabled(startWithWindowsItem.Checked);

        mediaStatusItem.Click += (_, _) => ShowMainWindow("Media");
        mediaPlayPauseItem.Click += async (_, _) => await SafeExecuteMediaCommandAsync(MediaCommand.PlayPause).ConfigureAwait(false);
        mediaPrevItem.Click += async (_, _) => await SafeExecuteMediaCommandAsync(MediaCommand.Previous).ConfigureAwait(false);
        mediaNextItem.Click += async (_, _) => await SafeExecuteMediaCommandAsync(MediaCommand.Next).ConfigureAwait(false);
        mediaMuteItem.Click += async (_, _) => await SafeToggleMuteAsync().ConfigureAwait(false);

        var menu = new ContextMenuStrip();
        menu.Items.Add("CYD-DECK").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open CYD-DECK", null, (_, _) => ShowMainWindow());
        menu.Items.Add(statusItem);
        menu.Items.Add(deviceItem);
        menu.Items.Add(ipItem);
        menu.Items.Add(mediaSeparator);
        menu.Items.Add(mediaStatusItem);
        menu.Items.Add(mediaPlayPauseItem);
        menu.Items.Add(mediaPrevItem);
        menu.Items.Add(mediaNextItem);
        menu.Items.Add(mediaMuteItem);
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
        updateLifetime.Cancel();
        singleInstanceWait.Unregister(null);
        notifyIcon.Visible = false;

        try
        {
            mainWindow?.CloseForExit();
            await runtime.DisposeAsync().ConfigureAwait(true);
        }
        finally
        {
            ExitThread();
        }
    }

    private void ShowMainWindow(string? targetPage = null)
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
                mainWindow = new SettingsWindow(state, runtime, startup, pairing, appIcon, sharedUpdateManager: updateManager);
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
            if (!string.IsNullOrWhiteSpace(targetPage))
            {
                mainWindow.ShowPage(targetPage);
            }
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

        if (notifyIcon.ContextMenuStrip is null || notifyIcon.ContextMenuStrip.IsDisposed)
        {
            return;
        }

        var snapshot = state.Snapshot();
        statusItem.Text = $"Device: {AgentStatusFormatter.ToDeviceStatus(snapshot)}";
        deviceItem.Text = $"Device: {snapshot.DeviceName ?? "none"}";
        ipItem.Text = snapshot.LanAddresses.Count == 0
            ? "IP: none"
            : $"IP: {string.Join(", ", snapshot.LanAddresses)}";

        var media = snapshot.CurrentMedia;
        var audio = snapshot.CurrentAudio;
        if (media is not null && !string.IsNullOrWhiteSpace(media.Title))
        {
            var track = string.IsNullOrWhiteSpace(media.Artist) ? media.Title : $"{media.Title} · {media.Artist}";
            if (track.Length > 40) track = track[..37] + "…";
            mediaStatusItem.Text = $"🎵 {track}";
            mediaStatusItem.Enabled = true;
            mediaPlayPauseItem.Text = media.PlaybackState == MediaPlaybackState.Playing ? "⏸ Pause" : "▶ Play";
            mediaPlayPauseItem.Enabled = true;
            mediaNextItem.Enabled = true;
            mediaPrevItem.Enabled = true;
        }
        else
        {
            mediaStatusItem.Text = "🎵 No active media";
            mediaStatusItem.Enabled = false;
            mediaPlayPauseItem.Text = "▶ Play / Pause";
            mediaPlayPauseItem.Enabled = false;
            mediaNextItem.Enabled = false;
            mediaPrevItem.Enabled = false;
        }

        if (audio is not null)
        {
            mediaMuteItem.Text = audio.IsMuted ? $"🔇 Unmute ({audio.VolumePercent}%)" : $"🔊 Mute ({audio.VolumePercent}%)";
            mediaMuteItem.Enabled = true;
        }
        else
        {
            mediaMuteItem.Text = "🔊 Mute";
            mediaMuteItem.Enabled = false;
        }

        var devicePart = !string.IsNullOrWhiteSpace(snapshot.DeviceName) ? $" ({snapshot.DeviceName})" : "";
        notifyIcon.Text = TrimNotifyText($"CYD-DECK{devicePart} - {AgentStatusFormatter.ToUserStatus(snapshot.HostStatus)}");

        CheckConnectionNotification(snapshot);
    }

    private void CheckConnectionNotification(AgentStatusSnapshot snapshot)
    {
        var previousStatus = lastKnownHostStatus;
        if (snapshot.HostStatus == previousStatus)
        {
            return;
        }

        lastKnownHostStatus = snapshot.HostStatus;

        if (ShouldTriggerPairingNotification(previousStatus, snapshot.HostStatus))
        {
            balloonTipTargetPage = "Pairing";
            var deviceName = snapshot.DeviceName ?? snapshot.DeviceId ?? "CYD";
            notifyIcon.ShowBalloonTip(3000, "CYD-DECK", $"Wykryto nowe urządzenie ({deviceName}). Kliknij, aby zatwierdzić parowanie w Windows Agent.", ToolTipIcon.Info);
            return;
        }

        if (previousStatus is not null)
        {
            var wasConnected = previousStatus == "connected";
            var isConnected = snapshot.HostStatus == "connected";

            if (isConnected)
            {
                balloonTipTargetPage = null;
                var transport = snapshot.RemoteAddress == "USB" ? "USB" : "Wi-Fi";
                var deviceName = snapshot.DeviceName ?? snapshot.DeviceId ?? "Device";
                notifyIcon.ShowBalloonTip(2500, "CYD-DECK", $"{deviceName} connected via {transport}.", ToolTipIcon.Info);
            }
            else if (wasConnected)
            {
                balloonTipTargetPage = null;
                notifyIcon.ShowBalloonTip(2500, "CYD-DECK", "Device disconnected.", ToolTipIcon.Warning);
            }
        }
    }

    internal void CheckConnectionNotificationForTests(AgentStatusSnapshot snapshot) => CheckConnectionNotification(snapshot);
    internal string? GetBalloonTipTargetPageForTests() => balloonTipTargetPage;
    internal SettingsWindow? MainWindowForTests => mainWindow;
    internal void TriggerBalloonTipClickedForTests()
    {
        if (balloonTipTargetPage is not null)
        {
            var target = balloonTipTargetPage;
            balloonTipTargetPage = null;
            ShowMainWindow(target);
        }
        else
        {
            ShowMainWindow();
        }
    }

    internal static bool ShouldTriggerPairingNotification(string? previousStatus, string currentStatus)
    {
        return currentStatus == "pairing pending" && previousStatus != "pairing pending";
    }

    private async Task SafeExecuteMediaCommandAsync(MediaCommand command)
    {
        try
        {
            await runtime.ExecuteHostMediaCommandAsync(command).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AgentDiagnosticLog.Write($"Tray media command failed ({command}): {ex.GetType().Name} - {ex.Message}");
        }
    }

    private async Task SafeToggleMuteAsync()
    {
        try
        {
            await runtime.ToggleHostMuteAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AgentDiagnosticLog.Write($"Tray toggle mute failed: {ex.GetType().Name} - {ex.Message}");
        }
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
            uiControl.BeginInvoke(new Action(() => ShowMainWindow()));
            return;
        }

        ShowMainWindow();
    }

    private static string TrimNotifyText(string value)
    {
        return value.Length <= 63 ? value : value[..63];
    }
}
