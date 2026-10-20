using System.Net;

namespace StreamDeck.WindowsAgent.Updates;

public sealed class FirmwareUpdateDialog : Form
{
    private static readonly Color Background = Color.FromArgb(18, 20, 25);
    private static readonly Color Surface = Color.FromArgb(28, 31, 38);
    private static readonly Color TextPrimary = Color.FromArgb(238, 241, 245);
    private static readonly Color TextMuted = Color.FromArgb(150, 158, 170);
    private static readonly Color Accent = Color.FromArgb(212, 59, 72);
    private static readonly Color Danger = Color.FromArgb(255, 69, 58);
    private static readonly Color SuccessColor = Color.FromArgb(48, 209, 88);

    private readonly AgentState state;
    private readonly string firmwarePath;
    private readonly IPAddress? deviceAddress;
    private readonly string? previousVersion;
    private readonly string? expectedVersion;
    private readonly string? expectedSha256;
    private readonly Label statusLabel;
    private readonly ProgressBar progressBar;
    private readonly TextBox passwordBox;
    private readonly Button installButton;
    private readonly Button closeButton;
    private bool isUpdating;

    public FirmwareUpdateDialog(
        AgentState state,
        string firmwarePath,
        string? deviceIp,
        string? previousVersion,
        string? expectedVersion = null,
        string? expectedSha256 = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(firmwarePath);

        this.state = state;
        this.firmwarePath = firmwarePath;
        this.previousVersion = previousVersion;
        this.expectedVersion = expectedVersion;
        this.expectedSha256 = expectedSha256;

        if (!string.IsNullOrWhiteSpace(deviceIp) && IPAddress.TryParse(deviceIp, out var parsedAddress))
        {
            deviceAddress = parsedAddress;
        }

        Text = "Aktualizacja oprogramowania CYD";
        Width = 540;
        Height = 360;
        MinimumSize = new Size(540, 360);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Background;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            RowCount = 7,
            ColumnCount = 1,
            BackColor = Background
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var targetVerText = !string.IsNullOrWhiteSpace(expectedVersion) ? expectedVersion : "Z pliku binarnego";
        var deviceText = deviceAddress is not null ? deviceAddress.ToString() : "Brak adresu IP Wi-Fi (połączenie USB)";

        panel.Controls.Add(new Label
        {
            Text = $"Plik obrazu: {Path.GetFileName(firmwarePath)}\nDocelowa wersja firmware: {targetVerText}\nUrządzenie: {deviceText}\nBieżąca wersja na urządzeniu: {FormatUnavailable(previousVersion)}",
            AutoSize = true,
            ForeColor = TextPrimary,
            Padding = new Padding(0, 0, 0, 10)
        });

        panel.Controls.Add(new Label
        {
            Text = "Hasło konfiguracyjne urządzenia (setup password):",
            AutoSize = true,
            ForeColor = TextMuted,
            Padding = new Padding(0, 0, 0, 4)
        });

        passwordBox = new TextBox
        {
            Dock = DockStyle.Top,
            UseSystemPasswordChar = true,
            BackColor = Surface,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Enabled = deviceAddress is not null
        };
        panel.Controls.Add(passwordBox);

        progressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 22,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous,
            Margin = new Padding(0, 12, 0, 8)
        };
        panel.Controls.Add(progressBar);

        var initialStatus = deviceAddress is not null
            ? "Wprowadź hasło konfiguracyjne, aby rozpocząć bezprzewodową aktualizację (OTA). Hasło jest jednorazowe i nie jest zapisywane."
            : "UWAGA: Urządzenie jest połączone wyłącznie przez USB (brak IP Wi-Fi). Aktualizacja bezprzewodowa (OTA) wymaga połączenia z siecią Wi-Fi. Skorzystaj z Web Installera w przeglądarce lub skonfiguruj Wi-Fi na urządzeniu.";

        statusLabel = new Label
        {
            Text = initialStatus,
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = deviceAddress is not null ? TextMuted : Danger
        };
        panel.Controls.Add(statusLabel);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Background
        };

        installButton = CreateButton("Zaktualizuj CYD");
        installButton.Enabled = deviceAddress is not null;
        installButton.Click += async (_, _) => await RunUpdateAsync().ConfigureAwait(true);

        closeButton = CreateButton("Zamknij");
        closeButton.Click += (_, _) => Close();

        actions.Controls.Add(closeButton);
        actions.Controls.Add(installButton);
        panel.Controls.Add(actions);

        Controls.Add(panel);
    }

    internal bool GetIsUpdatingForTests() => isUpdating;
    internal void SetIsUpdatingForTests(bool value) => isUpdating = value;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (isUpdating)
        {
            e.Cancel = true;
            if (Visible)
            {
                MessageBox.Show(
                    this,
                    "Trwa przesyłanie oprogramowania układowego. Nie można zamknąć okna przed zakończeniem lub anulowaniem operacji.",
                    "Aktualizacja w toku",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            return;
        }

        base.OnFormClosing(e);
    }

    private async Task RunUpdateAsync()
    {
        if (deviceAddress is null)
        {
            statusLabel.ForeColor = Danger;
            statusLabel.Text = "Brak adresu IP urządzenia. Aktualizacja OTA przez kabel USB nie jest obsługiwana.";
            return;
        }

        var password = passwordBox.Text;
        passwordBox.Clear();
        if (string.IsNullOrWhiteSpace(password))
        {
            statusLabel.ForeColor = Danger;
            statusLabel.Text = "Wymagane jest hasło konfiguracyjne urządzenia.";
            return;
        }

        isUpdating = true;
        installButton.Enabled = false;
        closeButton.Enabled = false;
        passwordBox.Enabled = false;
        statusLabel.ForeColor = TextMuted;

        try
        {
            var updateStartedAtUtc = DateTimeOffset.UtcNow;
            var progress = new Progress<FirmwareUpdateProgress>(UpdateProgress);
            var updater = new ArduinoOtaFirmwareUpdater();
            statusLabel.Text = "Nawiązywanie połączenia i przesyłanie obrazu…";

            var result = await updater.UploadAsync(
                new FirmwareUpdateFileRequest(deviceAddress, password, firmwarePath, timeout: TimeSpan.FromSeconds(15), expectedSha256: expectedSha256),
                progress).ConfigureAwait(true);

            password = string.Empty;
            if (!result.Success)
            {
                statusLabel.ForeColor = Danger;
                statusLabel.Text = result.SafeError ?? "Błąd przesyłania oprogramowania układowego.";
                closeButton.Enabled = true;
                return;
            }

            statusLabel.Text = "Przesłano obraz. Oczekiwanie na restart CYD i potwierdzenie telemetrii…";
            progressBar.Style = ProgressBarStyle.Marquee;
            var verifiedVersion = await FirmwareTelemetryVerifier.WaitForVersionVerificationAsync(state, updateStartedAtUtc, expectedVersion, previousVersion, TimeSpan.FromMinutes(2)).ConfigureAwait(true);
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Value = 100;

            if (verifiedVersion is not null)
            {
                statusLabel.ForeColor = SuccessColor;
                statusLabel.Text = $"Aktualizacja zakończona i zweryfikowana pomyślnie. Nowa wersja: {verifiedVersion}.";
            }
            else
            {
                statusLabel.ForeColor = Danger;
                statusLabel.Text = !string.IsNullOrWhiteSpace(expectedVersion)
                    ? $"Obraz został przesłany, lecz w wyznaczonym czasie urządzenie nie zgłosiło oczekiwanej wersji {expectedVersion} w telemetrii."
                    : "Obraz został przesłany, lecz w wyznaczonym czasie nie odebrano potwierdzenia zmiany wersji z urządzenia.";
            }
        }
        catch (OperationCanceledException)
        {
            statusLabel.ForeColor = Danger;
            statusLabel.Text = "Aktualizacja oprogramowania została anulowana.";
        }
        catch (Exception exception)
        {
            AgentDiagnosticLog.Write($"Firmware update failed in dialog: {exception.GetType().Name} - {exception.Message}");
            statusLabel.ForeColor = Danger;
            statusLabel.Text = "Wgrywanie nie powiodło się. Sprawdź połączenie Wi-Fi, hasło konfiguracyjne i spróbuj ponownie.";
        }
        finally
        {
            password = string.Empty;
            isUpdating = false;
            closeButton.Enabled = true;
        }
    }

    private void UpdateProgress(FirmwareUpdateProgress progress)
    {
        statusLabel.Text = $"{progress.Stage} ({progress.BytesSent} / {progress.TotalBytes} B)";
        if (progress.TotalBytes <= 0)
        {
            progressBar.Value = 0;
            return;
        }

        var percent = (int)Math.Clamp(progress.BytesSent * 100 / progress.TotalBytes, 0, 100);
        progressBar.Value = percent;
    }

    internal Task<string?> WaitForVersionVerificationAsync(
        DateTimeOffset updateStartedAtUtc,
        string? targetExpectedVersion,
        string? oldVersion,
        TimeSpan timeout,
        TimeSpan? pollInterval = null)
    {
        return FirmwareTelemetryVerifier.WaitForVersionVerificationAsync(
            state,
            updateStartedAtUtc,
            targetExpectedVersion,
            oldVersion,
            timeout,
            pollInterval);
    }

    private static Button CreateButton(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(130, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Accent,
            ForeColor = Color.White,
            Margin = new Padding(8, 6, 0, 0),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private static string FormatUnavailable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Brak informacji" : value;
    }
}

public static class FirmwareTelemetryVerifier
{
    public static async Task<string?> WaitForVersionVerificationAsync(
        AgentState state,
        DateTimeOffset updateStartedAtUtc,
        string? targetExpectedVersion,
        string? oldVersion,
        TimeSpan timeout,
        TimeSpan? pollInterval = null)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(500);
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var snapshot = state.Snapshot();
            var currentVersion = snapshot.DeviceInfo?.FirmwareVersion;
            var isFresh = snapshot.DeviceInfoUpdatedAtUtc is not null && snapshot.DeviceInfoUpdatedAtUtc >= updateStartedAtUtc;

            if (!string.IsNullOrWhiteSpace(currentVersion) && isFresh)
            {
                // Strict expected version match if expectedVersion is provided
                if (!string.IsNullOrWhiteSpace(targetExpectedVersion))
                {
                    if (string.Equals(currentVersion, targetExpectedVersion, StringComparison.Ordinal))
                    {
                        return currentVersion;
                    }
                }
                else if (!string.Equals(currentVersion, oldVersion, StringComparison.Ordinal))
                {
                    return currentVersion;
                }
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var delay = remaining < interval ? remaining : interval;
            await Task.Delay(delay).ConfigureAwait(false);
        }

        return null;
    }
}
