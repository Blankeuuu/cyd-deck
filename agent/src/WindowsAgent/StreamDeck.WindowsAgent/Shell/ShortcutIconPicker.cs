namespace StreamDeck.WindowsAgent.Shell;

internal sealed class ShortcutIconPicker : Form
{
    internal string IconKey { get; private set; }
    internal string? IconData { get; private set; }
    internal string? IconUrl { get; private set; }
    private readonly CancellationTokenSource lifetime = new();
    internal ShortcutIconPicker(string key, string? data, string? sourceUrl)
    {
        IconKey = key; IconData = data; IconUrl = sourceUrl;
        Text = "Choose icon"; ClientSize = new Size(560, 540); MinimumSize = Size;
        StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false;
        BackColor = Color.FromArgb(18, 20, 25); ForeColor = Color.FromArgb(238, 241, 245); Font = new Font("Segoe UI", 9F);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 6 };
        foreach (var height in new[] { 30, 280, 24, 36, 64, 46 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.Controls.Add(new Label { Text = "Built-in icons", Dock = DockStyle.Fill }, 0, 0);
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 5 };
        for (var i = 0; i < 5; ++i) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        for (var i = 0; i < 5; ++i) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        var preview = new Panel { Dock = DockStyle.Left, Width = 64 };
        preview.Paint += (_, e) => ShortcutIconCatalog.Draw(e.Graphics, IconKey, IconData, new Rectangle(16, 12, 32, 32), ForeColor);
        var status = new Label { Dock = DockStyle.Fill, Text = "Select an icon, or load an image from a URL.", TextAlign = ContentAlignment.MiddleLeft };
        var selectedButtons = new List<Button>();
        foreach (var item in ShortcutIconCatalog.All)
        {
            var button = new Button { Dock = DockStyle.Fill, Text = item.Key, TextAlign = ContentAlignment.BottomCenter, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(30, 34, 42), ForeColor = ForeColor, AccessibleName = item.Key };
            button.FlatAppearance.BorderSize = ShortcutIconCatalog.Normalize(key) == item.Key && data is null ? 2 : 0;
            button.FlatAppearance.BorderColor = Color.FromArgb(126, 175, 227);
            button.Paint += (_, e) => ShortcutIconCatalog.Draw(e.Graphics, item.Key, null, new Rectangle((button.Width - 24) / 2, 7, 24, 24), ForeColor);
            button.Click += (_, _) => { IconKey = item.Key; IconData = null; IconUrl = null; foreach (var other in selectedButtons) other.FlatAppearance.BorderSize = other == button ? 2 : 0; status.Text = item.Key; preview.Invalidate(); };
            selectedButtons.Add(button); grid.Controls.Add(button);
        }
        layout.Controls.Add(grid, 0, 1);
        layout.Controls.Add(new Label { Text = "Image URL", Dock = DockStyle.Fill }, 0, 2);
        var downloadRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        downloadRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); downloadRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        var url = new TextBox { Dock = DockStyle.Fill, Text = sourceUrl ?? "", PlaceholderText = "https://…/icon.png", MaxLength = 2048, BackColor = Color.FromArgb(30, 34, 42), ForeColor = ForeColor, AccessibleName = "Image URL" };
        var load = new Button { Text = "Load preview", Dock = DockStyle.Fill };
        var use = new Button { Text = "Use icon", Width = 108, Height = 34, DialogResult = DialogResult.OK };
        load.Click += async (_, _) =>
        {
            load.Enabled = false; use.Enabled = false; grid.Enabled = false; url.Enabled = false; status.Text = "Loading image…";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try { var downloaded = await ShortcutIconCatalog.DownloadAsync(url.Text, timeout.Token); if (IsDisposed) return; IconData = downloaded; IconKey = "custom"; IconUrl = url.Text.Trim(); foreach (var button in selectedButtons) button.FlatAppearance.BorderSize = 0; status.Text = "Ready · 24 × 24 · saved for offline use"; preview.Invalidate(); }
            catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or ArgumentException) { if (!IsDisposed) status.Text = error is InvalidDataException ? error.Message : "Could not load image. Check the direct image URL."; }
            finally { if (!IsDisposed) { load.Enabled = true; use.Enabled = true; grid.Enabled = true; url.Enabled = true; } }
        };
        downloadRow.Controls.Add(url, 0, 0); downloadRow.Controls.Add(load, 1, 0); layout.Controls.Add(downloadRow, 0, 3);
        var feedback = new Panel { Dock = DockStyle.Fill }; feedback.Controls.Add(status); feedback.Controls.Add(preview); layout.Controls.Add(feedback, 0, 4);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Cancel", Width = 108, Height = 34, DialogResult = DialogResult.Cancel };
        actions.Controls.Add(use); actions.Controls.Add(cancel); layout.Controls.Add(actions, 0, 5);
        AcceptButton = use; CancelButton = cancel; Controls.Add(layout);
        FormClosed += (_, _) => lifetime.Cancel();
    }
    protected override void Dispose(bool disposing) { if (disposing) { lifetime.Cancel(); lifetime.Dispose(); } base.Dispose(disposing); }
}
