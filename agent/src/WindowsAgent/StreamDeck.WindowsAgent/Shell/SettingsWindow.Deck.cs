using StreamDeck.Configuration;
using StreamDeck.Shortcuts;
using Shortcut = StreamDeck.Shortcuts.ShortcutDefinition;

namespace StreamDeck.WindowsAgent.Shell;

public sealed partial class SettingsWindow
{
    private Control CreateDesktopDeckPage()
    {
        var page = CreatePage("Shortcuts"); var configuration = LoadConfiguration();
        var shortcuts = (configuration.Shortcuts ?? []).ToList();
        var profile = GetProfiles(configuration).First(p => p.Name == configuration.ActiveProfile);
        var slots = profile.AssignedShortcutIds?.ToArray() ?? Enumerable.Range(0, 6).Select(i => shortcuts.ElementAtOrDefault(i)?.Id).ToArray();
        string? selectedId = slots.FirstOrDefault(id => id is not null) ?? shortcuts.FirstOrDefault()?.Id;
        var selectedSlot = Array.IndexOf(slots, selectedId); if (selectedId is null) selectedSlot = 0;
        bool loading = false;
        Action loadInspector = () => {}, rebuildLibrary = () => {}, refreshIcon = () => {}, edit = () => {};
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }; grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 354));
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 }; left.RowStyles.Add(new RowStyle(SizeType.Absolute, 338)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var previewCard = DesktopCard("CYD Display Preview");
        var preview = new DesktopCanvas { Dock = DockStyle.Fill, BackColor = Surface, Page = "Deck" };
        CompactField(previewCard, "", Segments([("Shortcuts Page", "Deck"), ("Home Screen (4)", "Home")], "Deck", v => { preview.Page = v; preview.Invalidate(); }), 42);
        previewCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); previewCard.Controls.Add(preview, 0, previewCard.RowCount++);
        left.Controls.Add(previewCard, 0, 0);
        var libraryCard = DesktopCard("Shortcut Library"); var header = new TableLayoutPanel { ColumnCount = 3, Dock = DockStyle.Fill, Margin = Padding.Empty }; header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        var search = new TextBox { PlaceholderText = "Search shortcuts", AccessibleName = "Search Shortcut Library", BackColor = SurfaceAlt, ForeColor = TextPrimary, Dock = DockStyle.Fill };
        var add = CompactButton("+", () => {
            using var dialog = new ShortcutEditorDialog(); if (dialog.ShowDialog(this) == DialogResult.OK) { shortcuts.Add(dialog.Shortcut); selectedId = dialog.Shortcut.Id; Store(); rebuildLibrary(); loadInspector(); } return Task.CompletedTask;
        }); add.Dock = DockStyle.Fill; add.AccessibleName = "New Shortcut"; desktopTips.SetToolTip(add, "New Shortcut"); add.Enabled = shortcuts.Count < 64;
        header.Controls.Add(new Label { Text = "Shortcut Library", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font.FontFamily, 10, FontStyle.Bold) }, 0, 0); header.Controls.Add(search, 1, 0); header.Controls.Add(add, 2, 0);
        var oldHeader = libraryCard.Controls[0]; libraryCard.Controls.Remove(oldHeader); oldHeader.Dispose(); libraryCard.RowStyles[0].Height = 38; libraryCard.Controls.Add(header, 0, 0);
        var library = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, AccessibleName = "Shortcut Library Scroll", Padding = new Padding(0, 0, 4, 0) };
        libraryCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); libraryCard.Controls.Add(library, 0, libraryCard.RowCount++); left.Controls.Add(libraryCard, 0, 1);
        var inspector = DesktopCard("Tile Inspector");
        var slotChoice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = SurfaceAlt, ForeColor = TextPrimary, FlatStyle = FlatStyle.Flat };
        slotChoice.Items.AddRange(new object[] { "Library only", "Slot 1", "Slot 2", "Slot 3", "Slot 4", "Slot 5", "Slot 6" });
        var type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = SurfaceAlt, ForeColor = TextPrimary, FlatStyle = FlatStyle.Flat }; type.Items.AddRange(new object[] { "hotkey", "url", "launch", "script", "powershell", "predefined" });
        var title = new TextBox { BackColor = SurfaceAlt, ForeColor = TextPrimary }; var target = new TextBox { BackColor = SurfaceAlt, ForeColor = TextPrimary }; var arguments = new TextBox { BackColor = SurfaceAlt, ForeColor = TextPrimary };
        var chooseIcon = CompactButton("Choose Icon…", () => {
            var index = shortcuts.FindIndex(s => s.Id == selectedId); if (index < 0) return Task.CompletedTask; var original = shortcuts[index]; using var picker = new ShortcutIconPicker(original.Icon, original.IconData, original.IconUrl);
            if (picker.ShowDialog(this) == DialogResult.OK) { shortcuts[index] = original with { Icon = picker.IconKey, IconData = picker.IconData, IconUrl = picker.IconUrl }; Store(); refreshIcon(); } return Task.CompletedTask;
        });
        void Pair(string a, Control first, string b, Control second) {
            var row = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty }; row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var one = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Margin = Padding.Empty }; one.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); one.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); first.Margin = new Padding(0, 0, 6, 0); first.Dock = DockStyle.Fill; first.AccessibleName = a; one.Controls.Add(new Label { Text = a, Dock = DockStyle.Fill, ForeColor = TextMuted }, 0, 0); one.Controls.Add(first, 0, 1);
            var two = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Margin = Padding.Empty }; two.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); two.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); second.Margin = Padding.Empty; second.Dock = DockStyle.Fill; second.AccessibleName = b; two.Controls.Add(new Label { Text = b, Dock = DockStyle.Fill, ForeColor = TextMuted }, 0, 0); two.Controls.Add(second, 0, 1);
            row.Controls.Add(one, 0, 0); row.Controls.Add(two, 1, 0); inspector.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); inspector.Controls.Add(row, 0, inspector.RowCount++);
        }
        Pair("Slot", slotChoice, "Type", type); Pair("Title", title, "Icon", chooseIcon);
        CompactField(inspector, "Shortcut / URL / Path / Adapter", target); CompactField(inspector, "Arguments", arguments);
        var swatches = Swatches([("Graphite", "#303640"), ("Ocean", "#155E75"), ("Cobalt", "#1D4ED8"), ("Ember", "#9A3412")], shortcuts.FirstOrDefault(s => s.Id == selectedId)?.Color ?? "#303640", v => { var i = shortcuts.FindIndex(s => s.Id == selectedId); if (i >= 0) { shortcuts[i] = shortcuts[i] with { Color = v }; Store(); } });
        var colors = new TableLayoutPanel { ColumnCount = 2 }; colors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); colors.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84)); swatches.Dock = DockStyle.Fill; colors.Controls.Add(swatches, 0, 0);
        var inherit = CompactButton("Default", () => { var i = shortcuts.FindIndex(s => s.Id == selectedId); if (i >= 0) { shortcuts[i] = shortcuts[i] with { Color = null }; Store(); } return Task.CompletedTask; }); inherit.Dock = DockStyle.Fill; colors.Controls.Add(inherit, 1, 0); CompactField(inspector, "Tile Color", colors);
        var display = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = SurfaceAlt, ForeColor = TextPrimary, FlatStyle = FlatStyle.Flat }; display.Items.AddRange(new object[] { "both", "icon", "label" }); CompactField(inspector, "Display", display);
        var status = new Label { Dock = DockStyle.Fill, ForeColor = TextMuted, AutoEllipsis = true }; CompactField(inspector, "Status", status, 48);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }; actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var apply = CompactButton("Save Shortcut", () => { edit(); var item = shortcuts.FirstOrDefault(s => s.Id == selectedId); status.Text = item is not null && ShortcutEditorDialog.TryValidateShortcutDraft(item, out var error) ? "Ready to Save & Sync" : "Complete the shortcut fields"; return Task.CompletedTask; }); desktopTips.SetToolTip(apply, "Apply to Configuration Draft; Save & Sync commits the configuration");
        var delete = CompactButton("Delete", () => { if (selectedId is null) return Task.CompletedTask; if (MessageBox.Show(this, "Delete this Shortcut from the library?", "Delete Shortcut", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return Task.CompletedTask; shortcuts.RemoveAll(s => s.Id == selectedId); for (var i = 0; i < 6; ++i) if (slots[i] == selectedId) slots[i] = null; selectedId = shortcuts.FirstOrDefault()?.Id; Store(); rebuildLibrary(); loadInspector(); return Task.CompletedTask; }); apply.Dock = delete.Dock = DockStyle.Fill; actions.Controls.Add(apply, 0, 0); actions.Controls.Add(delete, 1, 0); CompactField(inspector, "", actions, 42); FinishCard(inspector);
        chooseIcon.Paint += (_, e) => { var item = shortcuts.FirstOrDefault(s => s.Id == selectedId); if (item is not null) ShortcutIconCatalog.Draw(e.Graphics, item.Icon, item.IconData, new Rectangle(5, (chooseIcon.Height - 24) / 2, 24, 24), TextPrimary); };
        chooseIcon.TextAlign = ContentAlignment.MiddleRight; chooseIcon.Padding = new Padding(28, 0, 0, 0);
        void chooseIconRefresh() { chooseIcon.Invalidate(); rebuildLibrary(); RefreshPreview(); }
        void Store() {
            var current = LoadConfiguration() with { Shortcuts = shortcuts.ToArray() }; var all = GetProfiles(current).Select(p => p.Name == current.ActiveProfile ? p with { AssignedShortcutIds = slots.ToArray() } : p).ToArray(); configurationDraft = current with { Profiles = all }; MarkDraft(); RefreshPreview();
        }
        void RefreshPreview() { var appearance = pendingAppearance ?? LoadAppearanceConfiguration(); preview.ShortcutsLayout = appearance.ShortcutsLayout; preview.Slots = slots.Select(id => shortcuts.FirstOrDefault(s => s.Id == id)).ToArray(); preview.SelectedSlot = selectedSlot; preview.Snapshot = state.Snapshot(); preview.Accent = ColorTranslator.FromHtml(appearance.Accent); preview.DisplayBackground = ColorTranslator.FromHtml(appearance.Background); preview.TileTheme = appearance.TileColorScheme; preview.Clock24Hour = (pendingDesktopAppearance ?? LoadDesktopAppearanceConfiguration()).Clock24Hour; preview.Artwork = desktopArtwork; preview.Invalidate(); _ = LoadDesktopArtworkAsync(state.Snapshot(), desktopViewLifetime!.Token); }
        void LoadInspector() {
            loading = true; var item = shortcuts.FirstOrDefault(s => s.Id == selectedId); selectedSlot = item is null ? selectedSlot : Array.IndexOf(slots, item.Id); slotChoice.SelectedIndex = selectedSlot + 1;
            title.Text = item?.Name ?? ""; type.SelectedItem = item?.Action.Type ?? "hotkey"; target.Text = item?.Action.Url ?? item?.Action.ExecutablePath ?? item?.Action.ScriptPath ?? item?.Action.AdapterKey ?? (item is null ? "" : string.Join("+", item.Action.Keys)); arguments.Text = item is null ? "" : string.Join(" ", item.Action.Arguments); arguments.Modified = false; display.SelectedItem = item?.DisplayMode ?? "both";
            foreach (var control in new Control[] { title, type, target, arguments, chooseIcon, display, swatches, apply, delete }) control.Enabled = item is not null;
            status.Text = item is null ? "Select a Shortcut in the library" : selectedSlot < 0 ? "Library only" : $"Assigned to slot {selectedSlot + 1}"; swatches.Tag = item?.Color; swatches.Invalidate(true); loading = false; chooseIcon.Invalidate(); RefreshPreview();
        }
        void Edit() {
            if (loading) return; var i = shortcuts.FindIndex(s => s.Id == selectedId); if (i < 0) return; var original = shortcuts[i]; var value = target.Text.Trim(); var action = type.SelectedItem?.ToString() switch {
                "url" => new ShortcutActionDefinition { Type = "url", Url = value }, "launch" => new ShortcutActionDefinition { Type = "launch", ExecutablePath = value, Arguments = original.Action.Arguments }, "script" or "powershell" => new ShortcutActionDefinition { Type = type.SelectedItem.ToString()!, ScriptPath = value, Arguments = original.Action.Arguments }, "predefined" => new ShortcutActionDefinition { Type = "predefined", AdapterKey = value }, _ => new ShortcutActionDefinition { Type = "hotkey", Keys = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) }
            };
            // Keep existing arguments until the user edits them, preserving quoted argument boundaries.
            if (arguments.Modified) action = action with { Arguments = ShortcutEditorDialog.SplitArguments(arguments.Text) };
            shortcuts[i] = original with { Name = title.Text, Action = action, DisplayMode = display.SelectedItem?.ToString() ?? "both" }; Store(); status.Text = ShortcutEditorDialog.TryValidateShortcutDraft(shortcuts[i], out var error) ? "Configuration Draft" : error;
        }
        void Assign(string id, int slot) { for (var i = 0; i < 6; ++i) if (slots[i] == id) slots[i] = null; slots[slot] = id; selectedId = id; selectedSlot = slot; Store(); loadInspector(); rebuildLibrary(); }
        void RebuildLibrary() {
            foreach (Control control in library.Controls.Cast<Control>().ToArray()) control.Dispose(); library.Controls.Clear(); var filtered = shortcuts.Where(s => s.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || s.Action.Type.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            var columns = Math.Max(2, library.ClientSize.Width / 150); var width = Math.Max(145, (library.ClientSize.Width - 22) / columns - 6);
            foreach (var item in filtered) {
                var row = new Panel { Width = width, Height = 66, BackColor = SurfaceAlt, Margin = new Padding(3), AccessibleName = item.Name };
                var select = new RoundedButton { Dock = DockStyle.Fill, Text = "", FlatStyle = FlatStyle.Flat, BackColor = SurfaceAlt, AccessibleName = "Edit " + item.Name }; select.FlatAppearance.BorderColor = item.Id == selectedId ? Accent : Border;
                select.Paint += (_, e) => { ShortcutIconCatalog.Draw(e.Graphics, item.Icon, item.IconData, new Rectangle(8, 12, 24, 24), TextPrimary); TextRenderer.DrawText(e.Graphics, item.Name, Font, new Rectangle(40, 7, Math.Max(1, select.Width - 44), 25), TextPrimary, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis); using var small = new Font(Font.FontFamily, 9); TextRenderer.DrawText(e.Graphics, item.Action.Type == "hotkey" ? string.Join("+", item.Action.Keys) : item.Action.Type, small, new Rectangle(40, 34, Math.Max(1, select.Width - 44), 20), TextMuted, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis); };
                select.Click += (_, _) => { selectedId = item.Id; loadInspector(); rebuildLibrary(); };
                var assign = CompactButton("+", () => { var slot = selectedSlot >= 0 ? selectedSlot : Array.FindIndex(slots, id => id is null); if (slot < 0) { status.Text = "Select a Deck Tile to replace"; return Task.CompletedTask; } Assign(item.Id, slot); return Task.CompletedTask; }); assign.Dock = DockStyle.Right; assign.Width = 28; assign.AccessibleName = "Assign " + item.Name; desktopTips.SetToolTip(assign, "Assign to selected Deck Tile"); row.Controls.Add(select); row.Controls.Add(assign); row.SizeChanged += (_, _) => { using var path = CreateRoundedRectPath(new Rectangle(0, 0, row.Width, row.Height), 9); var old = row.Region; row.Region = new Region(path); old?.Dispose(); }; using (var path = CreateRoundedRectPath(new Rectangle(0, 0, row.Width, row.Height), 9)) row.Region = new Region(path); library.Controls.Add(row);
            }
            if (filtered.Length == 0) library.Controls.Add(new Label { Text = shortcuts.Count == 0 ? "Add your first Shortcut" : "No matching Shortcuts", AutoSize = true, ForeColor = TextMuted });
        }
        loadInspector = LoadInspector; rebuildLibrary = RebuildLibrary; refreshIcon = chooseIconRefresh; edit = Edit;
        title.TextChanged += (_, _) => edit(); target.TextChanged += (_, _) => edit(); arguments.TextChanged += (_, _) => edit(); type.SelectedIndexChanged += (_, _) => edit(); display.SelectedIndexChanged += (_, _) => edit();
        slotChoice.SelectedIndexChanged += (_, _) => { if (loading || selectedId is null) return; var slot = slotChoice.SelectedIndex - 1; if (slot >= 0) Assign(selectedId, slot); else { for (var i = 0; i < 6; ++i) if (slots[i] == selectedId) slots[i] = null; selectedSlot = -1; Store(); rebuildLibrary(); } };
        preview.SelectSlot = slot => { selectedSlot = slot; selectedId = slots[slot]; loadInspector(); rebuildLibrary(); };
        preview.UnassignSlot = slot => { slots[slot] = null; selectedSlot = slot; selectedId = null; Store(); loadInspector(); rebuildLibrary(); };
        preview.MoveSlot = (source, destination) => { (slots[source], slots[destination]) = (slots[destination], slots[source]); selectedSlot = destination; selectedId = slots[destination]; Store(); loadInspector(); rebuildLibrary(); };
        search.TextChanged += (_, _) => rebuildLibrary(); library.SizeChanged += (_, _) => rebuildLibrary();
        desktopRefresh = RefreshPreview; grid.Controls.Add(left, 0, 0); grid.Controls.Add(inspector, 1, 0); AddPageBodyControl(page, grid); loadInspector(); rebuildLibrary(); return page;
    }
}
