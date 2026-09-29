using System.Net;

namespace StreamDeck.WindowsAgent.Updates;

public sealed class FirmwareUpdateDialog : Form
{
    private static readonly Color Background = Color.FromArgb(18, 20, 25);
    private static readonly Color Surface = Color.FromArgb(28, 31, 38);
    private static readonly Color TextPrimary = Color.FromArgb(238, 241, 245);
    private static readonly Color TextMuted = Color.FromArgb(150, 158, 170);
    private static readonly Color Accent = Color.FromArgb(212, 59, 72);

    private readonly AgentState state;
    private readonly string firmwarePath;
    private readonly IPAddress deviceAddress;
    private readonly string? previousVersion;
    private readonly Label statusLabel;
    private readonly ProgressBar progressBar;
    private readonly TextBox passwordBox;
    private readonly Button installButton;
    private readonly Button closeButton;

    public FirmwareUpdateDialog(AgentState state, string firmwarePath, string deviceIp, string? previousVersion)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(firmwarePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceIp);

        if (!IPAddress.TryParse(deviceIp, out var parsedAddress))
        {
            throw new ArgumentException("Device IP address is invalid.", nameof(deviceIp));
        }

        this.state = state;
        this.firmwarePath = firmwarePath;
        deviceAddress = parsedAddress;
        this.previousVersion = previousVersion;

        Text = "Install firmware";
        Width = 520;
        Height = 300;
        MinimumSize = new Size(520, 300);
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
            RowCount = 6,
            ColumnCount = 1,
            BackColor = Background
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        panel.Controls.Add(new Label
        {
            Text = $"Firmware: {Path.GetFileName(firmwarePath)}\nSelected firmware version: Unavailable\nDevice: {deviceAddress}\nCurrent firmware: {FormatUnavailable(previousVersion)}",
            AutoSize = true,
            ForeColor = TextPrimary,
            Padding = new Padding(0, 0, 0, 12)
        });

        panel.Controls.Add(new Label
        {
            Text = "Setup password",
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
            BorderStyle = BorderStyle.FixedSingle
        };
        panel.Controls.Add(passwordBox);

        progressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 22,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous,
            Margin = new Padding(0, 14, 0, 8)
        };
        panel.Controls.Add(progressBar);

        statusLabel = new Label
        {
            Text = "Enter the setup password to start. The password is used once and is not saved.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = TextMuted
        };
        panel.Controls.Add(statusLabel);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Background
        };

        installButton = CreateButton("Update board");
        installButton.Click += async (_, _) => await RunUpdateAsync().ConfigureAwait(true);
        closeButton = CreateButton("Close");
        closeButton.Click += (_, _) => Close();
        actions.Controls.Add(closeButton);
        actions.Controls.Add(installButton);
        panel.Controls.Add(actions);

        Controls.Add(panel);
    }

    private async Task RunUpdateAsync()
    {
        var password = passwordBox.Text;
        passwordBox.Clear();
        if (string.IsNullOrWhiteSpace(password))
        {
            statusLabel.Text = "Setup password is required for firmware update.";
            return;
        }

        installButton.Enabled = false;
        closeButton.Enabled = false;
        passwordBox.Enabled = false;

        try
        {
            var updateStartedAtUtc = DateTimeOffset.UtcNow;
            var progress = new Progress<FirmwareUpdateProgress>(UpdateProgress);
            var updater = new ArduinoOtaFirmwareUpdater();
            statusLabel.Text = "Uploading firmware…";
            var result = await updater.UploadAsync(
                new FirmwareUpdateFileRequest(deviceAddress, password, firmwarePath, timeout: TimeSpan.FromSeconds(10)),
                progress).ConfigureAwait(true);

            password = string.Empty;
            if (!result.Success)
            {
                statusLabel.Text = result.SafeError ?? "Firmware update failed.";
                closeButton.Enabled = true;
                return;
            }

            statusLabel.Text = "Upload complete. Waiting for reconnect and fresh firmware telemetry…";
            progressBar.Style = ProgressBarStyle.Marquee;
            var verifiedVersion = await WaitForVersionChangeAsync(previousVersion, updateStartedAtUtc, TimeSpan.FromMinutes(2)).ConfigureAwait(true);
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Value = 100;

            statusLabel.Text = verifiedVersion is null
                ? "Upload finished, but no changed firmware version telemetry was received before timeout. A physical device check may be required."
                : $"Firmware update verified. Reported version changed to {verifiedVersion}.";
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "Firmware update cancelled.";
        }
        catch (Exception exception)
        {
            AgentDiagnosticLog.Write($"Firmware update failed in dialog: {exception.GetType().Name}");
            statusLabel.Text = "Firmware update failed. Check the selected image, network connection, and setup password, then try again.";
        }
        finally
        {
            password = string.Empty;
            closeButton.Enabled = true;
        }
    }

    private void UpdateProgress(FirmwareUpdateProgress progress)
    {
        statusLabel.Text = progress.Stage;
        if (progress.TotalBytes <= 0)
        {
            progressBar.Value = 0;
            return;
        }

        var percent = (int)Math.Clamp(progress.BytesSent * 100 / progress.TotalBytes, 0, 100);
        progressBar.Value = percent;
    }

    private async Task<string?> WaitForVersionChangeAsync(string? oldVersion, DateTimeOffset updateStartedAtUtc, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var snapshot = state.Snapshot();
            var version = snapshot.DeviceInfo?.FirmwareVersion;
            if (!string.IsNullOrWhiteSpace(version)
                && !string.Equals(version, oldVersion, StringComparison.Ordinal)
                && snapshot.DeviceInfoUpdatedAtUtc is not null
                && snapshot.DeviceInfoUpdatedAtUtc >= updateStartedAtUtc)
            {
                return version;
            }

            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(true);
        }

        return null;
    }

    private static Button CreateButton(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(110, 34),
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
        return string.IsNullOrWhiteSpace(value) ? "Unavailable" : value;
    }
}
