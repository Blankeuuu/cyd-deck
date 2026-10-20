using System.Runtime.InteropServices;

namespace StreamDeck.WindowsAgent.Shell;

internal sealed class ShortcutIconPicker : Form
{
    internal string IconKey { get; private set; }
    internal string? IconData { get; private set; }
    internal string? IconUrl { get; private set; }
    private readonly CancellationTokenSource lifetime = new();

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var dark = SettingsWindow.IsClientLightTheme ? 0 : 1;
        _ = DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
        var corners = 2;
        _ = DwmSetWindowAttribute(Handle, 33, ref corners, sizeof(int));
    }

    internal ShortcutIconPicker(string key, string? data, string? sourceUrl)
    {
        IconKey = key;
        IconData = data;
        IconUrl = sourceUrl;
        Text = UiText.Get("Deck.ChooseIconTitle");
        ClientSize = new Size(620, 640);
        MinimumSize = Size;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        var isLight = SettingsWindow.IsClientLightTheme;
        BackColor = isLight ? Color.FromArgb(246, 247, 249) : Color.FromArgb(18, 20, 25);
        ForeColor = isLight ? Color.FromArgb(29, 32, 37) : Color.FromArgb(238, 241, 245);
        var itemBg = isLight ? Color.FromArgb(236, 238, 242) : Color.FromArgb(28, 32, 40);
        var itemBorder = isLight ? Color.FromArgb(210, 214, 220) : Color.FromArgb(42, 46, 56);
        var accent = SettingsWindow.CurrentAccent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 11F);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 6 };
        foreach (var height in new[] { 32, 320, 30, 42, 90, 46 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));

        var builtInLabel = new Label
        {
            Text = UiText.Get("Deck.BuiltInIcons"),
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = isLight ? Color.FromArgb(40, 44, 52) : Color.FromArgb(215, 222, 232),
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(builtInLabel, 0, 0);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 5 };
        for (var i = 0; i < 5; ++i) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        for (var i = 0; i < 5; ++i) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 20));

        var preview = new Panel { Dock = DockStyle.Left, Width = 64 };
        preview.Paint += (_, e) => ShortcutIconCatalog.Draw(e.Graphics, IconKey, IconData, new Rectangle(16, (preview.Height - 32) / 2, 32, 32), ForeColor);

        var status = new Label
        {
            Dock = DockStyle.Fill,
            Text = UiText.Get("Deck.IconHelp"),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = isLight ? Color.FromArgb(93, 101, 112) : Color.FromArgb(180, 188, 200)
        };

        var selectedButtons = new List<Button>();
        foreach (var item in ShortcutIconCatalog.All)
        {
            var isInitiallySelected = ShortcutIconCatalog.Normalize(key) == item.Key && data is null;
            var button = new Button
            {
                Dock = DockStyle.Fill,
                Text = item.Key,
                TextAlign = ContentAlignment.BottomCenter,
                FlatStyle = FlatStyle.Flat,
                BackColor = itemBg,
                ForeColor = ForeColor,
                Font = new Font("Segoe UI", 10F),
                Padding = new Padding(0, 0, 0, 4),
                AccessibleName = item.Key
            };
            button.FlatAppearance.BorderSize = isInitiallySelected ? 2 : 1;
            button.FlatAppearance.BorderColor = isInitiallySelected ? accent : itemBorder;
            button.Paint += (_, e) => ShortcutIconCatalog.Draw(e.Graphics, item.Key, null, new Rectangle((button.Width - 24) / 2, 5, 24, 24), ForeColor);
            button.Click += (_, _) =>
            {
                IconKey = item.Key;
                IconData = null;
                IconUrl = null;
                foreach (var other in selectedButtons)
                {
                    var isCurrent = other == button;
                    other.FlatAppearance.BorderSize = isCurrent ? 2 : 1;
                    other.FlatAppearance.BorderColor = isCurrent ? accent : itemBorder;
                }
                status.Text = item.Key;
                preview.Invalidate();
            };
            selectedButtons.Add(button);
            grid.Controls.Add(button);
        }
        layout.Controls.Add(grid, 0, 1);

        var imageUrlLabel = new Label
        {
            Text = UiText.Get("Deck.ImageUrl"),
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = isLight ? Color.FromArgb(40, 44, 52) : Color.FromArgb(215, 222, 232),
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(imageUrlLabel, 0, 2);

        var downloadRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        downloadRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        downloadRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 114));

        var url = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = sourceUrl ?? "",
            PlaceholderText = "https://…/icon.png",
            MaxLength = 2048,
            BackColor = itemBg,
            ForeColor = ForeColor,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = UiText.Get("Deck.ImageUrl")
        };

        var load = new Button
        {
            Text = UiText.Get("Deck.LoadPreview"),
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = itemBg,
            ForeColor = ForeColor,
            AccessibleName = UiText.Get("Deck.LoadPreview")
        };
        load.FlatAppearance.BorderSize = 1;
        load.FlatAppearance.BorderColor = itemBorder;

        var use = new Button
        {
            Text = UiText.Get("Deck.UseIcon"),
            Width = 108,
            Height = 34,
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            BackColor = accent,
            ForeColor = Color.White,
            AccessibleName = UiText.Get("Deck.UseIcon")
        };
        use.FlatAppearance.BorderSize = 0;

        load.Click += async (_, _) =>
        {
            load.Enabled = false;
            use.Enabled = false;
            grid.Enabled = false;
            url.Enabled = false;
            status.Text = UiText.Get("Deck.Loading");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var downloaded = await ShortcutIconCatalog.DownloadAsync(url.Text, timeout.Token);
                if (IsDisposed) return;
                IconData = downloaded;
                IconKey = "custom";
                IconUrl = url.Text.Trim();
                foreach (var button in selectedButtons)
                {
                    button.FlatAppearance.BorderSize = 1;
                    button.FlatAppearance.BorderColor = Color.FromArgb(42, 46, 56);
                }
                status.Text = UiText.Get("Deck.IconReady");
                preview.Invalidate();
            }
            catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or ArgumentException)
            {
                if (!IsDisposed)
                    status.Text = error is InvalidDataException ? error.Message : UiText.Get("Deck.IconError");
            }
            finally
            {
                if (!IsDisposed)
                {
                    load.Enabled = true;
                    use.Enabled = true;
                    grid.Enabled = true;
                    url.Enabled = true;
                }
            }
        };

        downloadRow.Controls.Add(url, 0, 0);
        downloadRow.Controls.Add(load, 1, 0);
        layout.Controls.Add(downloadRow, 0, 3);

        var feedback = new Panel { Dock = DockStyle.Fill };
        feedback.Controls.Add(status);
        feedback.Controls.Add(preview);
        layout.Controls.Add(feedback, 0, 4);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button
        {
            Text = UiText.Get("Deck.Cancel"),
            Width = 108,
            Height = 34,
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            BackColor = itemBg,
            ForeColor = ForeColor,
            AccessibleName = UiText.Get("Deck.Cancel")
        };
        cancel.FlatAppearance.BorderSize = 1;
        cancel.FlatAppearance.BorderColor = itemBorder;

        actions.Controls.Add(use);
        actions.Controls.Add(cancel);
        layout.Controls.Add(actions, 0, 5);

        AcceptButton = use;
        CancelButton = cancel;
        Controls.Add(layout);
        FormClosed += (_, _) => { try { lifetime.Cancel(); } catch (ObjectDisposedException) { } };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { lifetime.Cancel(); } catch (ObjectDisposedException) { }
            lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
