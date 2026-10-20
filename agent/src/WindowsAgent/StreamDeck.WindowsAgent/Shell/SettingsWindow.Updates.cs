using System.Diagnostics;
using StreamDeck.Host;
using StreamDeck.Protocol;
using StreamDeck.WindowsAgent.Updates;

namespace StreamDeck.WindowsAgent.Shell;

public sealed partial class SettingsWindow
{
    private readonly UpdateManager updateManager;
    private readonly bool ownsUpdateManager;
    private Label? updateStatusLabel;
    private ProgressBar? updateProgressBar;
    private Button? checkUpdatesButton;
    private Button? stageAgentButton;
    private Button? installAgentButton;
    private Button? stageFirmwareButton;
    private Label? firmwareStatusLabel;
    private bool isCheckingUpdates;
    private bool isDownloadingUpdate;

    private Control CreateUpdatesPage()
    {
        var snapshot = state.Snapshot();
        var isConnected = string.Equals(snapshot.HostStatus, "connected", StringComparison.OrdinalIgnoreCase);
        var fwVersion = isConnected ? snapshot.DeviceInfo?.FirmwareVersion : null;
        updateDeviceIp = isConnected ? snapshot.DeviceInfo?.Wifi.Ip : null;

        var panel = CreatePage("Updates");

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0),
            Margin = Padding.Empty,
            BackColor = Background
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        // 1. Karta: Program CYD-DECK (Windows Agent)
        var agentCard = DesktopCard("Program CYD-DECK (Windows Agent)", "Windows Agent Updates");
        var agentGrid = CreateCardGrid(rowCount: 4);

        AddCardRow(agentGrid, 0, "Wersja zainstalowana", GetAgentVersion());
        AddCardRow(agentGrid, 1, "Wersja protokołu", ProtocolConstants.CurrentVersion.ToString());

        updateStatusLabel = new Label
        {
            Text = GetInitialUpdateStatusText(),
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextMuted,
            Font = new Font(Font.FontFamily, 9.5f)
        };
        AddCardCustomRow(agentGrid, 2, "Status wydań", updateStatusLabel);

        updateProgressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Height = 16,
            Minimum = 0,
            Maximum = 100,
            Visible = false,
            Style = ProgressBarStyle.Continuous
        };
        AddCardCustomRow(agentGrid, 3, "Pobieranie", updateProgressBar);

        agentCard.Controls.Add(agentGrid);

        var agentActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Surface,
            Margin = Padding.Empty
        };

        checkUpdatesButton = CreateStyledActionButton("Sprawdź aktualizacje", useAccent: true);
        checkUpdatesButton.Click += async (_, _) => await RunCheckForUpdatesAsync().ConfigureAwait(true);
        agentActions.Controls.Add(checkUpdatesButton);

        stageAgentButton = CreateStyledActionButton("Pobierz instalator", useAccent: false);
        stageAgentButton.Visible = updateManager.LastCheckResult?.HasAgentUpdate == true && updateManager.StagedAgentInstallerPath is null;
        stageAgentButton.Click += async (_, _) => await RunStageAgentUpdateAsync().ConfigureAwait(true);
        agentActions.Controls.Add(stageAgentButton);

        installAgentButton = CreateStyledActionButton("Zainstaluj aktualizację", useAccent: true);
        installAgentButton.Visible = updateManager.StagedAgentInstallerPath is not null;
        installAgentButton.Click += (_, _) => PromptAndInstallAgent();
        agentActions.Controls.Add(installAgentButton);

        agentCard.Controls.Add(agentActions);
        mainLayout.Controls.Add(agentCard, 0, 0);

        // 2. Karta: Oprogramowanie układowe CYD (ESP32 Firmware)
        var firmwareCard = DesktopCard("Oprogramowanie układowe CYD (ESP32 Firmware)", "CYD Firmware Updates");
        var fwGrid = CreateCardGrid(rowCount: 4);

        var deviceName = snapshot.DeviceName ?? snapshot.DeviceId ?? "Brak urządzenia";
        AddCardRow(fwGrid, 0, "Podłączone urządzenie", isConnected ? deviceName : "Urządzenie offline");
        AddCardRow(fwGrid, 1, "Wersja oprogramowania", fwVersion ?? (isConnected ? "Oczekiwanie na raport z CYD…" : "Urządzenie niepodłączone"));

        var connectionType = updateDeviceIp is not null
            ? $"{updateDeviceIp} (Wi-Fi)"
            : (isConnected ? "Kabel USB (brak adresu IP Wi-Fi)" : "Brak połączenia");
        AddCardRow(fwGrid, 2, "Połączenie urządzenia", connectionType);

        firmwareStatusLabel = new Label
        {
            Text = GetFirmwareStatusText(snapshot, isConnected, fwVersion),
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextMuted,
            Font = new Font(Font.FontFamily, 9.5f)
        };
        AddCardCustomRow(fwGrid, 3, "Status aktualizacji", firmwareStatusLabel);

        firmwareCard.Controls.Add(fwGrid);

        var fwActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Surface,
            Margin = Padding.Empty
        };

        stageFirmwareButton = CreateStyledActionButton("Zaktualizuj CYD przez Wi-Fi (OTA)", useAccent: true);
        stageFirmwareButton.Visible = isConnected && updateDeviceIp is not null && updateManager.LastCheckResult?.HasFirmwareUpdate == true;
        stageFirmwareButton.Click += async (_, _) => await RunFirmwareUpdateFlowAsync().ConfigureAwait(true);
        fwActions.Controls.Add(stageFirmwareButton);

        var manualFwButton = CreateStyledActionButton("Zainstaluj z pliku (.bin)", useAccent: false);
        manualFwButton.Click += async (_, _) => await InstallFirmwareFromFileAsync().ConfigureAwait(true);
        fwActions.Controls.Add(manualFwButton);

        if (isConnected && updateDeviceIp is null)
        {
            var webFlasherButton = CreateStyledActionButton("Web Installer (dla USB)", useAccent: false);
            webFlasherButton.Click += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://blankeuuu.github.io/cyd-deck/web-installer/",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    AgentDiagnosticLog.Write($"Failed to open web installer: {ex.Message}");
                }
            };
            fwActions.Controls.Add(webFlasherButton);
        }

        firmwareCard.Controls.Add(fwActions);
        mainLayout.Controls.Add(firmwareCard, 0, 1);

        AddPageBodyControl(panel, mainLayout);
        return panel;
    }

    private Control CreateUpdateAvailableBanner()
    {
        var releaseTag = updateManager.LastCheckResult?.LatestRelease?.TagName ?? "nowa wersja";
        var banner = new Panel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = clientLightTheme ? Color.FromArgb(220, 235, 252) : Color.FromArgb(36, 52, 75),
            Cursor = Cursors.Hand,
            Padding = new Padding(16, 0, 16, 0)
        };

        var label = new Label
        {
            Text = $"Dostępna nowa wersja CYD-DECK ({releaseTag}). Kliknij tutaj, aby przejść do aktualizacji.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = clientLightTheme ? Color.FromArgb(20, 50, 100) : Color.FromArgb(170, 205, 255),
            Font = new Font(Font.FontFamily, 9.25f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        label.Click += (_, _) => ShowPage("Updates");
        banner.Click += (_, _) => ShowPage("Updates");
        banner.Controls.Add(label);
        return banner;
    }

    public void RefreshUpdateStatus()
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        InvalidatePageCache();
        ShowPage(currentPage);
    }
    private string GetInitialUpdateStatusText()
    {
        if (updateManager.LastCheckResult is null)
        {
            return "Nie sprawdzono wydań online. Kliknij 'Sprawdź aktualizacje'.";
        }

        return updateManager.LastCheckResult.Status switch
        {
            UpdateCheckStatus.UpToDate => $"Aplikacja jest aktualna (wersja {GetAgentVersion()}).",
            UpdateCheckStatus.UpdateAvailable => $"Dostępna nowa wersja: {updateManager.LastCheckResult.LatestRelease?.TagName}.",
            UpdateCheckStatus.ChecksumMissing => updateManager.LastCheckResult.ErrorMessage ?? "Brak sum kontrolnych w wydaniu.",
            UpdateCheckStatus.Error => updateManager.LastCheckResult.ErrorMessage ?? "Błąd sprawdzania aktualizacji.",
            _ => "Status nieznany."
        };
    }

    private string GetFirmwareStatusText(AgentStatusSnapshot snapshot, bool isConnected, string? fwVersion)
    {
        if (!isConnected)
        {
            return "Podłącz urządzenie CYD przez USB lub Wi-Fi.";
        }

        if (updateDeviceIp is null)
        {
            return "Połączenie wyłącznie USB. Aktualizacja bezprzewodowa (OTA) wymaga połączenia z Wi-Fi. Dla USB użyj Web Installera.";
        }

        if (updateManager.LastCheckResult?.HasFirmwareUpdate == true)
        {
            return $"Dostępne nowe oprogramowanie układowe: {updateManager.LastCheckResult.LatestRelease?.TagName}.";
        }

        return "Oprogramowanie układowe jest aktualne lub nie sprawdzono wydań.";
    }

    private async Task RunCheckForUpdatesAsync()
    {
        if (isCheckingUpdates || IsDisposed || Disposing)
        {
            return;
        }

        isCheckingUpdates = true;
        if (checkUpdatesButton is not null && !checkUpdatesButton.IsDisposed)
        {
            checkUpdatesButton.Enabled = false;
            checkUpdatesButton.Text = "Sprawdzanie…";
        }
        if (updateStatusLabel is not null && !updateStatusLabel.IsDisposed)
        {
            updateStatusLabel.ForeColor = TextMuted;
            updateStatusLabel.Text = "Pobieranie informacji o najnowszych wydaniach z GitHub…";
        }

        try
        {
            var ct = desktopViewLifetime?.Token ?? CancellationToken.None;
            var snapshot = state.Snapshot();
            var result = await updateManager.CheckForUpdatesAsync(snapshot.DeviceInfo?.FirmwareVersion, cancellationToken: ct).ConfigureAwait(true);

            if (IsDisposed || Disposing)
            {
                return;
            }

            if (updateStatusLabel is not null && !updateStatusLabel.IsDisposed)
            {
                if (result.Status == UpdateCheckStatus.UpToDate)
                {
                    updateStatusLabel.ForeColor = AppleGreen;
                    updateStatusLabel.Text = $"Posiadasz najnowszą wersję programu ({GetAgentVersion()}).";
                }
                else if (result.Status == UpdateCheckStatus.UpdateAvailable)
                {
                    updateStatusLabel.ForeColor = Accent;
                    var tag = result.LatestRelease?.TagName ?? "nowa";
                    updateStatusLabel.Text = $"Dostępne nowe stabilne wydanie: {tag}!";
                }
                else if (result.Status == UpdateCheckStatus.ChecksumMissing)
                {
                    updateStatusLabel.ForeColor = Danger;
                    updateStatusLabel.Text = result.ErrorMessage ?? "Wydanie nie zawiera zaufanego pliku sum kontrolnych.";
                }
                else
                {
                    updateStatusLabel.ForeColor = Danger;
                    updateStatusLabel.Text = result.ErrorMessage ?? "Nie udało się sprawdzić aktualizacji.";
                }
            }

            InvalidatePageCache();
            ShowPage(currentPage);
        }
        catch (OperationCanceledException)
        {
            // Operation canceled
        }
        catch (Exception ex)
        {
            AgentDiagnosticLog.Write($"Check updates error: {ex.Message}");
            if (updateStatusLabel is not null && !updateStatusLabel.IsDisposed)
            {
                updateStatusLabel.ForeColor = Danger;
                updateStatusLabel.Text = $"Błąd: {ex.Message}";
            }
        }
        finally
        {
            isCheckingUpdates = false;
            if (checkUpdatesButton is not null && !checkUpdatesButton.IsDisposed)
            {
                checkUpdatesButton.Enabled = true;
                checkUpdatesButton.Text = "Sprawdź aktualizacje";
            }
        }
    }

    private async Task RunStageAgentUpdateAsync()
    {
        if (isDownloadingUpdate || updateManager.LastCheckResult?.AgentAsset is null || IsDisposed || Disposing)
        {
            return;
        }

        isDownloadingUpdate = true;
        if (stageAgentButton is not null && !stageAgentButton.IsDisposed)
        {
            stageAgentButton.Enabled = false;
            stageAgentButton.Text = "Pobieranie…";
        }
        if (updateProgressBar is not null && !updateProgressBar.IsDisposed)
        {
            updateProgressBar.Visible = true;
            updateProgressBar.Value = 0;
        }

        var progress = new Progress<UpdateStagingProgress>(p =>
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            if (updateProgressBar is not null && !updateProgressBar.IsDisposed && p.TotalBytes > 0)
            {
                updateProgressBar.Value = Math.Clamp(p.PercentComplete, 0, 100);
            }
            if (updateStatusLabel is not null && !updateStatusLabel.IsDisposed)
            {
                updateStatusLabel.Text = $"{p.StatusMessage} ({p.BytesDownloaded / 1024 / 1024} / {Math.Max(1, p.TotalBytes / 1024 / 1024)} MB)";
            }
        });

        try
        {
            var ct = desktopViewLifetime?.Token ?? CancellationToken.None;
            var result = await updateManager.StageAgentUpdateAsync(progress, cancellationToken: ct).ConfigureAwait(true);
            if (IsDisposed || Disposing)
            {
                return;
            }

            if (result.Success)
            {
                if (updateStatusLabel is not null && !updateStatusLabel.IsDisposed)
                {
                    updateStatusLabel.ForeColor = AppleGreen;
                    updateStatusLabel.Text = "Instalator został pobrany i zweryfikowany sumą SHA-256. Gotowy do instalacji.";
                }

                InvalidatePageCache();
                ShowPage(currentPage);
            }
            else
            {
                if (updateStatusLabel is not null && !updateStatusLabel.IsDisposed)
                {
                    updateStatusLabel.ForeColor = Danger;
                    updateStatusLabel.Text = result.SafeError ?? "Błąd podczas pobierania lub weryfikacji pakietu.";
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Operation canceled
        }
        catch (Exception ex)
        {
            AgentDiagnosticLog.Write($"Stage agent error: {ex.Message}");
            if (updateStatusLabel is not null && !updateStatusLabel.IsDisposed)
            {
                updateStatusLabel.ForeColor = Danger;
                updateStatusLabel.Text = $"Błąd pobierania: {ex.Message}";
            }
        }
        finally
        {
            isDownloadingUpdate = false;
            if (updateProgressBar is not null && !updateProgressBar.IsDisposed)
            {
                updateProgressBar.Visible = false;
            }
            if (stageAgentButton is not null && !stageAgentButton.IsDisposed)
            {
                stageAgentButton.Enabled = true;
                stageAgentButton.Text = "Pobierz instalator";
            }
        }
    }

    private void PromptAndInstallAgent()
    {
        var targetVersion = updateManager.LastCheckResult?.LatestRelease?.TagName ?? "nowej";
        var confirm = MessageBox.Show(
            this,
            $"Czy chcesz zainstalować wersję {targetVersion} programu CYD-DECK?\n\nAplikacja zostanie zamknięta, a instalator zaktualizuje pliki programu zachowując Twoje ustawienia i parowanie urządzeń.",
            "Instalacja aktualizacji",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);

        if (confirm != DialogResult.Yes)
        {
            AgentDiagnosticLog.Write("User cancelled agent update installation");
            return;
        }

        if (!ResolveUnsavedChanges(InstallAgentUpdate)) return;
        InstallAgentUpdate();
    }

    private void InstallAgentUpdate()
    {
        if (IsDisposed || Disposing) return;
        if (!updateManager.TryInstallStagedAgent(silent: true, out var errorMessage))
        {
            MessageBox.Show(
                this,
                errorMessage ?? "Nie udało się uruchomić instalatora aktualizacji.",
                "Błąd instalatora",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task RunFirmwareUpdateFlowAsync()
    {
        if (isDownloadingUpdate || IsDisposed || Disposing) return;
        isDownloadingUpdate = true;
        var originPage = currentPage;
        try
        {
            await RunFirmwareUpdateFlowCoreAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AgentDiagnosticLog.Write($"Firmware staging error: {ex.Message}");
            if (!IsDisposed && !Disposing && currentPage == originPage && firmwareStatusLabel is { IsDisposed: false })
                firmwareStatusLabel.Text = $"Błąd pobierania: {ex.Message}";
        }
        finally { isDownloadingUpdate = false; }
    }
    private async Task RunFirmwareUpdateFlowCoreAsync()
    {
        var originPage = currentPage;
        var originLabel = firmwareStatusLabel;
        var snapshot = state.Snapshot();
        if (snapshot.DeviceInfo?.Wifi.Ip is not { Length: > 0 } deviceIp)
        {
            MessageBox.Show(
                this,
                "Urządzenie nie posiada zgłoszonego adresu IP Wi-Fi. Aktualizacja bezprzewodowa (OTA) wymaga połączenia sieciowego.",
                "Brak połączenia Wi-Fi",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var targetVersion = updateManager.LastCheckResult?.LatestRelease?.TagName ?? "nowego";
        var confirm = MessageBox.Show(
            this,
            $"Czy chcesz zaktualizować oprogramowanie układowe podłączonego CYD do wersji {targetVersion} przez sieć Wi-Fi (OTA)?\n\nOperacja wymaga podania jednorazowego hasła konfiguracyjnego (setup password).",
            "Aktualizacja oprogramowania CYD",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        if (updateManager.StagedFirmwarePath is null || !File.Exists(updateManager.StagedFirmwarePath))
        {
            var ct = desktopViewLifetime?.Token ?? CancellationToken.None;
            var fwProgress = new Progress<UpdateStagingProgress>(p =>
            {
                if (!IsDisposed && !Disposing && currentPage == originPage && ReferenceEquals(originLabel, firmwareStatusLabel) && firmwareStatusLabel is { IsDisposed: false })
                {
                    firmwareStatusLabel.Text = $"{p.StatusMessage} ({p.BytesDownloaded / 1024} / {Math.Max(1, p.TotalBytes / 1024)} KB)";
                }
            });

            var result = await updateManager.StageFirmwareUpdateAsync(fwProgress, cancellationToken: ct).ConfigureAwait(true);
            if (IsDisposed || Disposing || currentPage != originPage || !ReferenceEquals(originLabel, firmwareStatusLabel)) return;
            if (!result.Success || result.StagedFilePath is null)
            {
                if (IsDisposed || Disposing)
                {
                    return;
                }

                MessageBox.Show(
                    this,
                    result.SafeError ?? "Nie udało się pobrać obrazu oprogramowania układowego.",
                    "Błąd pobierania firmware",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
        }

        if (IsDisposed || Disposing)
        {
            return;
        }

        if (currentPage != originPage || !ReferenceEquals(originLabel, firmwareStatusLabel)) return;
        if (!updateManager.TryGetVerifiedFirmware(out var firmwarePath, out var firmwareVersion, out var error, out var firmwareHash))
        {
            MessageBox.Show(this, error, "Błąd integralności firmware", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        using var dialog = new FirmwareUpdateDialog(
            state,
            firmwarePath!,
            deviceIp,
            snapshot.DeviceInfo?.FirmwareVersion,
            firmwareVersion,
            firmwareHash);

        dialog.ShowDialog(this);
        if (IsDisposed || Disposing) return;
        InvalidatePageCache();
        ShowPage(currentPage);
    }

    private static TableLayoutPanel CreateCardGrid(int rowCount)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = rowCount,
            Margin = Padding.Empty,
            Padding = new Padding(0, 8, 0, 8)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (var i = 0; i < rowCount; i++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        }

        return grid;
    }

    private void AddCardRow(TableLayoutPanel grid, int row, string label, string value)
    {
        grid.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextMuted,
            Font = new Font(Font.FontFamily, 9.5f)
        }, 0, row);

        grid.Controls.Add(new Label
        {
            Text = value,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextPrimary,
            Font = new Font(Font.FontFamily, 9.5f, FontStyle.Regular)
        }, 1, row);
    }

    private void AddCardCustomRow(TableLayoutPanel grid, int row, string label, Control control)
    {
        grid.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextMuted,
            Font = new Font(Font.FontFamily, 9.5f)
        }, 0, row);

        grid.Controls.Add(control, 1, row);
    }
}
