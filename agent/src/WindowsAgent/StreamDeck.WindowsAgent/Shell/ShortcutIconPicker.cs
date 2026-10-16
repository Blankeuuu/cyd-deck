namespace StreamDeck.WindowsAgent.Shell;

internal sealed class ShortcutIconPicker : Form
{
    internal string IconKey { get; private set; }
    internal string? IconData { get; private set; }
    internal string? IconUrl { get; private set; }
    private readonly CancellationTokenSource lifetime = new();

    internal ShortcutIconPicker(string key, string? data, string? sourceUrl)
    {
        IconKey = key;
        IconData = data;
        IconUrl = sourceUrl;
        Text = "Choose icon";
        ClientSize = new Size(560, 550);
        MinimumSize = Size;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(18, 20, 25);
        ForeColor = Color.FromArgb(238, 241, 245);
        Font = new Font("Segoe UI", 9F);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 6 };
        foreach (var height in new[] { 28, 280, 26, 36, 64, 46 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));

        var builtInLabel = new Label
        {
            Text = "Built-in icons",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(215, 222, 232),
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
            Text = "Select an icon, or load an image from a URL.",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(180, 188, 200)
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
                BackColor = Color.FromArgb(28, 32, 40),
                ForeColor = ForeColor,
                Font = new Font("Segoe UI", 8F),
                Padding = new Padding(0, 0, 0, 4),
                AccessibleName = item.Key
            };
            button.FlatAppearance.BorderSize = isInitiallySelected ? 2 : 1;
            button.FlatAppearance.BorderColor = isInitiallySelected ? Color.FromArgb(82, 153, 224) : Color.FromArgb(42, 46, 56);
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
                    other.FlatAppearance.BorderColor = isCurrent ? Color.FromArgb(82, 153, 224) : Color.FromArgb(42, 46, 56);
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
            Text = "Image URL",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(215, 222, 232),
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
            BackColor = Color.FromArgb(28, 32, 40),
            ForeColor = ForeColor,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "Image URL"
        };

        var load = new Button
        {
            Text = "Load preview",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(39, 43, 52),
            ForeColor = Color.White,
            AccessibleName = "Load preview"
        };
        load.FlatAppearance.BorderSize = 1;
        load.FlatAppearance.BorderColor = Color.FromArgb(58, 64, 76);

        var use = new Button
        {
            Text = "Use icon",
            Width = 108,
            Height = 34,
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(82, 153, 224),
            ForeColor = Color.White,
            AccessibleName = "Use icon"
        };
        use.FlatAppearance.BorderSize = 0;

        load.Click += async (_, _) =>
        {
            load.Enabled = false;
            use.Enabled = false;
            grid.Enabled = false;
            url.Enabled = false;
            status.Text = "Loading image…";
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
                status.Text = "Ready · 24 × 24 · saved for offline use";
                preview.Invalidate();
            }
            catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or ArgumentException)
            {
                if (!IsDisposed)
                    status.Text = error is InvalidDataException ? error.Message : "Could not load image. Check the direct image URL.";
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
            Text = "Cancel",
            Width = 108,
            Height = 34,
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(39, 43, 52),
            ForeColor = Color.White,
            AccessibleName = "Cancel"
        };
        cancel.FlatAppearance.BorderSize = 1;
        cancel.FlatAppearance.BorderColor = Color.FromArgb(58, 64, 76);

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
