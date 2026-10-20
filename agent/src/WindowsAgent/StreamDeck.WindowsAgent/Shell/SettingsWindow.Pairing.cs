using System.Drawing.Drawing2D;
using StreamDeck.Host;
using StreamDeck.Protocol;
using StreamDeck.WindowsAgent.Runtime;

namespace StreamDeck.WindowsAgent.Shell;

public sealed partial class SettingsWindow
{
    private TableLayoutPanel? pairingPendingContainer;
    private TableLayoutPanel? pairingTrustedContainer;
    private Label? pairingStatusFeedback;
    private Label? pairingConnectionBadge;
    private bool isPairingRefreshing;

    private Control CreateDesktopPairingPage()
    {
        var page = CreatePage("Pairing");
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(0),
            BackColor = Background
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        // Lewa kolumna: Stan + Instrukcja oraz Urządzenia oczekujące
        var leftColumn = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        leftColumn.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        leftColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 260));
        leftColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // 1. Karta: Status połączenia i instrukcja
        var statusCard = DesktopCard("Status połączenia i instrukcja", "Connection status & guide");

        var statusRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));

        pairingConnectionBadge = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold),
            AccessibleName = "Connection status pill"
        };
        statusRow.Controls.Add(pairingConnectionBadge, 0, 0);

        var refreshConnBtn = CompactButton("↻", async () =>
        {
            await ReconnectAsync();
            await RefreshPairingViewAsync();
        });
        refreshConnBtn.Dock = DockStyle.Fill;
        refreshConnBtn.Margin = Padding.Empty;
        refreshConnBtn.AccessibleName = "Odśwież porty i połączenie";
        desktopTips.SetToolTip(refreshConnBtn, "Odśwież porty szeregowe i ponów próbę połączenia");
        statusRow.Controls.Add(refreshConnBtn, 1, 0);

        statusCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        statusCard.Controls.Add(statusRow, 0, statusCard.RowCount++);

        // Instrukcja USB
        var usbGuide = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 4, 0, 2)
        };
        usbGuide.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        usbGuide.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        usbGuide.Controls.Add(new Label
        {
            Text = "Połączenie przewodowe USB (Zalecane)",
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
            ForeColor = TextPrimary
        }, 0, 0);
        usbGuide.Controls.Add(new Label
        {
            Text = "Podłącz CYD kablem USB do komputera. Pierwsze połączenie wymaga zatwierdzenia urządzenia na liście oczekujących. Kolejne sesje łączą się automatycznie.",
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 8.5f),
            ForeColor = TextMuted
        }, 0, 1);
        statusCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        statusCard.Controls.Add(usbGuide, 0, statusCard.RowCount++);

        // Instrukcja Wi-Fi
        var wifiGuide = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 4, 0, 2)
        };
        wifiGuide.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        wifiGuide.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        wifiGuide.Controls.Add(new Label
        {
            Text = "Połączenie bezprzewodowe Wi-Fi",
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
            ForeColor = TextPrimary
        }, 0, 0);
        wifiGuide.Controls.Add(new Label
        {
            Text = "Połącz CYD z siecią przez portal konfiguracyjny (192.168.4.1). Nowo wykryte urządzenie w sieci lokalnej pojawi się na liście żądań do zatwierdzenia.",
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 8.5f),
            ForeColor = TextMuted
        }, 0, 1);
        statusCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        statusCard.Controls.Add(wifiGuide, 0, statusCard.RowCount++);
        FinishCard(statusCard);

        leftColumn.Controls.Add(statusCard, 0, 0);

        // 2. Karta: Urządzenia oczekujące
        var pendingCard = DesktopCard("Oczekujące na zatwierdzenie", "Pending devices");
        var pendingSubtitle = new Label
        {
            Text = "Żądania oczekujące na akceptację przez użytkownika:",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = new Font(Font.FontFamily, 8.5f),
            ForeColor = TextMuted,
            Padding = new Padding(0, 0, 0, 4)
        };
        pendingCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        pendingCard.Controls.Add(pendingSubtitle, 0, pendingCard.RowCount++);

        pairingPendingContainer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 0,
            AutoScroll = true,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 4),
            BackColor = Surface,
            AccessibleName = "Device List Scroll"
        };
        pairingPendingContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pendingCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        pendingCard.Controls.Add(pairingPendingContainer, 0, pendingCard.RowCount++);
        FinishCard(pendingCard);

        leftColumn.Controls.Add(pendingCard, 0, 1);

        // Prawa kolumna: Zaufane urządzenia
        var rightColumn = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        rightColumn.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        rightColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var trustedCard = DesktopCard("Zaufane urządzenia", "Trusted devices");
        var trustedSubtitle = new Label
        {
            Text = "Urządzenia autoryzowane do sterowania skrótami i synchronizacji:",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = new Font(Font.FontFamily, 8.5f),
            ForeColor = TextMuted,
            Padding = new Padding(0, 0, 0, 4)
        };
        trustedCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        trustedCard.Controls.Add(trustedSubtitle, 0, trustedCard.RowCount++);

        pairingTrustedContainer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 0,
            AutoScroll = true,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 4),
            BackColor = Surface,
            AccessibleName = "Device List Scroll"
        };
        pairingTrustedContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        trustedCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        trustedCard.Controls.Add(pairingTrustedContainer, 0, trustedCard.RowCount++);

        var trustedBottomBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        trustedBottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        trustedBottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));

        pairingStatusFeedback = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font.FontFamily, 8.5f),
            ForeColor = TextMuted,
            Text = "Wszystkie zaufane urządzenia są gotowe.",
            AccessibleName = "Komunikat statusu parowania"
        };
        trustedBottomBar.Controls.Add(pairingStatusFeedback, 0, 0);

        var refreshListBtn = CompactButton("↻ Odśwież", async () => await RefreshPairingViewAsync());
        refreshListBtn.Dock = DockStyle.Fill;
        refreshListBtn.Margin = Padding.Empty;
        refreshListBtn.AccessibleName = "Odśwież listę urządzeń";
        desktopTips.SetToolTip(refreshListBtn, "Odśwież listę urządzeń oczekujących i zaufanych");
        trustedBottomBar.Controls.Add(refreshListBtn, 1, 0);

        trustedCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        trustedCard.Controls.Add(trustedBottomBar, 0, trustedCard.RowCount++);
        FinishCard(trustedCard);

        rightColumn.Controls.Add(trustedCard, 0, 0);

        grid.Controls.Add(leftColumn, 0, 0);
        grid.Controls.Add(rightColumn, 1, 0);

        AddPageBodyControl(page, grid);

        refreshPairingLists = () =>
        {
            if (!IsDisposed && currentPage == "Pairing")
            {
                _ = RefreshPairingViewAsync();
            }
        };

        UpdateConnectionBadge();
        _ = RefreshPairingViewAsync();

        return page;
    }

    private void UpdateConnectionBadge()
    {
        if (pairingConnectionBadge is null || pairingConnectionBadge.IsDisposed) return;
        var snapshot = state.Snapshot();
        var isConnected = snapshot.HostStatus == "connected";
        var isPending = snapshot.HostStatus == "pairing pending";

        if (isConnected)
        {
            var isUsb = snapshot.RemoteAddress == "USB";
            var portDesc = isUsb ? (snapshot.Connection?.ActiveUsbPort ?? "USB") : snapshot.RemoteAddress ?? "Wi-Fi";
            pairingConnectionBadge.Text = $"● Połączono ({portDesc}) • Urządzenie aktywne";
            pairingConnectionBadge.ForeColor = clientLightTheme ? Color.FromArgb(0, 100, 0) : AppleGreen;
        }
        else if (isPending)
        {
            pairingConnectionBadge.Text = "● Oczekuje na zatwierdzenie parowania • Sprawdź listę";
            pairingConnectionBadge.ForeColor = clientLightTheme ? Color.FromArgb(140, 76, 0) : AppleAmber;
        }
        else
        {
            pairingConnectionBadge.Text = "● Brak aktywnego połączenia (Offline)";
            pairingConnectionBadge.ForeColor = TextMuted;
        }
    }

    private async Task RefreshPairingViewAsync()
    {
        if (isPairingRefreshing) return;
        isPairingRefreshing = true;

        try
        {
            UpdateConnectionBadge();

            IReadOnlyList<PendingDevice> pendingDevices;
            IReadOnlyList<PairedDevice> approvedDevices;

            try
            {
                pendingDevices = await pairing.ListPendingAsync().ConfigureAwait(true);
                approvedDevices = await pairing.ListApprovedAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AgentDiagnosticLog.Write($"Pairing list fetch failed: {ex.GetType().Name}");
                if (pairingStatusFeedback is { IsDisposed: false })
                {
                    pairingStatusFeedback.ForeColor = Color.LightSalmon;
                    pairingStatusFeedback.Text = "Nie udało się wczytać listy urządzeń.";
                }
                return;
            }

            RenderPendingDevices(pendingDevices);
            RenderApprovedDevices(approvedDevices);

            if (pairingStatusFeedback is { IsDisposed: false })
            {
                pairingStatusFeedback.ForeColor = TextMuted;
                if (pendingDevices.Count > 0)
                {
                    pairingStatusFeedback.Text = $"Wykryto {pendingDevices.Count} urządzenie(a) oczekujące na zatwierdzenie.";
                }
                else
                {
                    pairingStatusFeedback.Text = approvedDevices.Count > 0
                        ? $"Zaufane urządzenia: {approvedDevices.Count}. Połączenie gotowe."
                        : "Brak oczekujących żądań.";
                }
            }
        }
        finally
        {
            isPairingRefreshing = false;
        }
    }

    private void RenderPendingDevices(IReadOnlyList<PendingDevice> devices)
    {
        if (pairingPendingContainer is null || pairingPendingContainer.IsDisposed) return;
        pairingPendingContainer.SuspendLayout();
        foreach (Control c in pairingPendingContainer.Controls.Cast<Control>().ToArray()) c.Dispose();
        pairingPendingContainer.Controls.Clear();
        pairingPendingContainer.RowStyles.Clear();
        pairingPendingContainer.RowCount = 0;

        if (devices.Count == 0)
        {
            var empty = CreatePairingEmptyState(
                "Brak oczekujących żądań",
                "Żadne urządzenie nie oczekuje obecnie na parowanie.\nPodłącz CYD kablem USB lub skonfiguruj sieć Wi-Fi.");
            pairingPendingContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            pairingPendingContainer.Controls.Add(empty, 0, pairingPendingContainer.RowCount++);
        }
        else
        {
            foreach (var device in devices)
            {
                var row = CreatePendingDeviceCard(device);
                pairingPendingContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
                pairingPendingContainer.Controls.Add(row, 0, pairingPendingContainer.RowCount++);
            }
        }

        // Spacer aby pochłonąć pozostałą wysokość i zapobiec rozciąganiu wierszy
        pairingPendingContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        pairingPendingContainer.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.Transparent }, 0, pairingPendingContainer.RowCount++);

        pairingPendingContainer.ResumeLayout(true);
    }

    private void RenderApprovedDevices(IReadOnlyList<PairedDevice> devices)
    {
        if (pairingTrustedContainer is null || pairingTrustedContainer.IsDisposed) return;
        pairingTrustedContainer.SuspendLayout();
        foreach (Control c in pairingTrustedContainer.Controls.Cast<Control>().ToArray()) c.Dispose();
        pairingTrustedContainer.Controls.Clear();
        pairingTrustedContainer.RowStyles.Clear();
        pairingTrustedContainer.RowCount = 0;

        if (devices.Count == 0)
        {
            var empty = CreatePairingEmptyState(
                "Brak zaufanych urządzeń",
                "Zatwierdzone urządzenia pojawią się tutaj i będą łączyć się automatycznie.");
            pairingTrustedContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            pairingTrustedContainer.Controls.Add(empty, 0, pairingTrustedContainer.RowCount++);
        }
        else
        {
            foreach (var device in devices)
            {
                var row = CreateTrustedDeviceCard(device);
                pairingTrustedContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
                pairingTrustedContainer.Controls.Add(row, 0, pairingTrustedContainer.RowCount++);
            }
        }

        // Spacer aby pochłonąć pozostałą wysokość i zapobiec rozciąganiu wierszy
        pairingTrustedContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        pairingTrustedContainer.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.Transparent }, 0, pairingTrustedContainer.RowCount++);

        pairingTrustedContainer.ResumeLayout(true);
    }

    private Control CreatePairingEmptyState(string title, string description)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = SurfaceAlt,
            Margin = new Padding(2, 6, 2, 6),
            Padding = new Padding(12, 16, 12, 12)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 10f, FontStyle.Bold),
            ForeColor = TextPrimary,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var descLabel = new Label
        {
            Text = description,
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 8.5f),
            ForeColor = TextMuted,
            TextAlign = ContentAlignment.TopCenter
        };

        panel.Controls.Add(titleLabel, 0, 0);
        panel.Controls.Add(descLabel, 0, 1);

        panel.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = CreateRoundedRectPath(new Rectangle(0, 0, panel.Width - 1, panel.Height - 1), 10);
            using var borderPen = new Pen(Border, 1f);
            e.Graphics.DrawPath(borderPen, path);
        };

        return panel;
    }

    private Control CreatePendingDeviceCard(PendingDevice device)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = SurfaceAlt,
            Margin = new Padding(2, 2, 2, 6),
            Padding = new Padding(12, 8, 10, 8)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var infoPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        infoPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 52f));
        infoPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 48f));

        var titleLabel = new Label
        {
            Text = string.IsNullOrWhiteSpace(device.Name) ? "CYD Display" : device.Name,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold),
            ForeColor = TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var addressDesc = device.RemoteAddress ?? "USB";
        var timeDesc = AgentStatusFormatter.FormatRelativeTime(device.FirstSeenUtc);
        var detailsLabel = new Label
        {
            Text = $"ID: {device.DeviceId}  •  {addressDesc}  •  Wykryto: {timeDesc}",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = new Font(Font.FontFamily, 8.5f),
            ForeColor = TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        };

        infoPanel.Controls.Add(titleLabel, 0, 0);
        infoPanel.Controls.Add(detailsLabel, 0, 1);
        row.Controls.Add(infoPanel, 0, 0);

        var approveBtn = CompactButton("Zatwierdź", async () => await ApproveDeviceAsync(device.DeviceId, device.Name));
        approveBtn.Dock = DockStyle.Fill;
        approveBtn.Margin = new Padding(3, 9, 3, 9);
        approveBtn.BackColor = Accent;
        approveBtn.ForeColor = Color.White;
        approveBtn.AccessibleName = $"Zatwierdź {device.DeviceId}";
        desktopTips.SetToolTip(approveBtn, $"Zatwierdź urządzenie '{device.Name}' ({device.DeviceId}) jako zaufane");
        row.Controls.Add(approveBtn, 1, 0);

        var rejectBtn = CompactButton("Odrzuć", async () => await RejectDeviceAsync(device.DeviceId, device.Name));
        rejectBtn.Dock = DockStyle.Fill;
        rejectBtn.Margin = new Padding(3, 9, 3, 9);
        rejectBtn.BackColor = Surface;
        rejectBtn.ForeColor = TextPrimary;
        rejectBtn.AccessibleName = $"Odrzuć {device.DeviceId}";
        desktopTips.SetToolTip(rejectBtn, $"Odrzuć żądanie parowania urządzenia '{device.Name}' ({device.DeviceId})");
        row.Controls.Add(rejectBtn, 2, 0);

        row.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = CreateRoundedRectPath(new Rectangle(0, 0, row.Width - 1, row.Height - 1), 8);
            using var borderPen = new Pen(Border, 1f);
            e.Graphics.DrawPath(borderPen, path);
        };

        return row;
    }

    private Control CreateTrustedDeviceCard(PairedDevice device)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = SurfaceAlt,
            Margin = new Padding(2, 2, 2, 6),
            Padding = new Padding(12, 8, 10, 8)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var infoPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        infoPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 52f));
        infoPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 48f));

        var titleLabel = new Label
        {
            Text = $"●  {(string.IsNullOrWhiteSpace(device.Name) ? "CYD Display" : device.Name)}  (Zaufane)",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold),
            ForeColor = clientLightTheme ? Color.FromArgb(0, 100, 0) : AppleGreen,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var addressDesc = device.RemoteAddress ?? "USB";
        var timeDesc = AgentStatusFormatter.FormatRelativeTime(device.FirstSeenUtc);
        var detailsLabel = new Label
        {
            Text = $"ID: {device.DeviceId}  •  {addressDesc}  •  Dodano: {timeDesc}",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = new Font(Font.FontFamily, 8.5f),
            ForeColor = TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        };

        infoPanel.Controls.Add(titleLabel, 0, 0);
        infoPanel.Controls.Add(detailsLabel, 0, 1);
        row.Controls.Add(infoPanel, 0, 0);

        var forgetBtn = CompactButton("Zapomnij", async () => await ForgetDeviceWithConfirmationAsync(device.DeviceId, device.Name));
        forgetBtn.Dock = DockStyle.Fill;
        forgetBtn.Margin = new Padding(3, 9, 3, 9);
        forgetBtn.BackColor = Surface;
        forgetBtn.ForeColor = clientLightTheme ? Color.FromArgb(180, 40, 40) : Color.FromArgb(255, 110, 110);
        forgetBtn.AccessibleName = $"Zapomnij {device.DeviceId}";
        desktopTips.SetToolTip(forgetBtn, $"Usuń urządzenie '{device.Name}' ({device.DeviceId}) z listy zaufanych");
        row.Controls.Add(forgetBtn, 1, 0);

        row.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = CreateRoundedRectPath(new Rectangle(0, 0, row.Width - 1, row.Height - 1), 8);
            using var borderPen = new Pen(Border, 1f);
            e.Graphics.DrawPath(borderPen, path);
        };

        return row;
    }

    private async Task ApproveDeviceAsync(string deviceId, string deviceName)
    {
        try
        {
            var success = await pairing.ApproveAsync(deviceId).ConfigureAwait(true);
            if (pairingStatusFeedback is { IsDisposed: false })
            {
                pairingStatusFeedback.ForeColor = success ? (clientLightTheme ? Color.FromArgb(0, 100, 0) : AppleGreen) : Color.LightSalmon;
                pairingStatusFeedback.Text = success
                    ? $"Zatwierdzono urządzenie '{deviceName}'."
                    : $"Nie znaleziono żądania dla '{deviceName}'.";
            }
            await RefreshPairingViewAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AgentDiagnosticLog.Write($"Approve pairing failed: {ex.GetType().Name}");
            if (pairingStatusFeedback is { IsDisposed: false })
            {
                pairingStatusFeedback.ForeColor = Color.LightSalmon;
                pairingStatusFeedback.Text = "Nie udało się zatwierdzić urządzenia.";
            }
        }
    }

    private async Task RejectDeviceAsync(string deviceId, string deviceName)
    {
        try
        {
            var success = await pairing.RejectAsync(deviceId).ConfigureAwait(true);
            if (pairingStatusFeedback is { IsDisposed: false })
            {
                pairingStatusFeedback.ForeColor = TextMuted;
                pairingStatusFeedback.Text = success
                    ? $"Odrzucono żądanie urządzenia '{deviceName}'."
                    : $"Nie znaleziono żądania dla '{deviceName}'.";
            }
            await RefreshPairingViewAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AgentDiagnosticLog.Write($"Reject pairing failed: {ex.GetType().Name}");
            if (pairingStatusFeedback is { IsDisposed: false })
            {
                pairingStatusFeedback.ForeColor = Color.LightSalmon;
                pairingStatusFeedback.Text = "Nie udało się odrzucić urządzenia.";
            }
        }
    }

    private async Task ForgetDeviceWithConfirmationAsync(string deviceId, string deviceName)
    {
        var confirm = MessageBox.Show(
            this,
            $"Czy na pewno chcesz usunąć urządzenie '{deviceId}' z listy zaufanych na tym komputerze?\n\nUrządzenie utraci dostęp i przy kolejnej próbie połączenia będzie wymagało ponownego zatwierdzenia.",
            "Zapomnij urządzenie",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (confirm != DialogResult.Yes) return;

        try
        {
            var forgotten = await runtime.ForgetDeviceAsync(deviceId).ConfigureAwait(true);
            if (pairingStatusFeedback is { IsDisposed: false })
            {
                pairingStatusFeedback.ForeColor = forgotten ? TextMuted : Color.LightSalmon;
                pairingStatusFeedback.Text = forgotten
                    ? $"Urządzenie '{deviceName}' zostało zapomniane."
                    : $"Urządzenie '{deviceId}' nie znajdowało się na liście.";
            }
            await RefreshPairingViewAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AgentDiagnosticLog.Write($"Forget device failed: {ex.GetType().Name}");
            if (pairingStatusFeedback is { IsDisposed: false })
            {
                pairingStatusFeedback.ForeColor = Color.LightSalmon;
                pairingStatusFeedback.Text = "Nie udało się usunąć urządzenia.";
            }
        }
    }
}
