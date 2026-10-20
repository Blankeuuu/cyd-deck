using StreamDeck.Configuration;
using StreamDeck.Shortcuts;
using Shortcut = StreamDeck.Shortcuts.ShortcutDefinition;

namespace StreamDeck.WindowsAgent.Shell;

public sealed partial class SettingsWindow
{
    private Control CreateDesktopDeckPage()
    {
        var page = CreatePage("Shortcuts"); var configuration = LoadConfiguration();
        var draft = new DeckEditSession(configuration);
        var shortcuts = draft.Shortcuts;
        var slots = draft.Slots;
        bool loading = false;
        Action loadInspector = () => {}, rebuildLibrary = () => {}, refreshIcon = () => {}, edit = () => {};
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Name = "DeckBuilder", Font = new Font("Segoe UI", 11F) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

        var previewCard = DesktopCard(UiText.Get("Deck.Preview"));
        var preview = new DesktopCanvas { Name = "DeckPreview", Dock = DockStyle.Fill, BackColor = Surface, Page = "Deck" };
        CompactField(previewCard, "", Segments([(UiText.Get("Deck.DeckPage"), "Deck"), (UiText.Get("Deck.HomePage"), "Home")], "Deck", v => { preview.Page = v; preview.Invalidate(); }), 42);
        previewCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewCard.Controls.Add(preview, 0, previewCard.RowCount++);


        var libraryCard = DesktopCard(UiText.Get("Deck.Library"));
        var header = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, Dock = DockStyle.Fill, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Dip(grid, 74)));
        var search = new TextBox { Name = "DeckSearch", PlaceholderText = UiText.Get("Deck.SearchPlaceholder"), AccessibleName = UiText.Get("Deck.Search"), BackColor = SurfaceAlt, ForeColor = TextPrimary, Dock = DockStyle.Fill };
        var add = CompactButton(UiText.Get("Deck.New"), () => {
            using var dialog = new ShortcutEditorDialog();
            if (dialog.ShowDialog(this) == DialogResult.OK) {
                shortcuts.Add(dialog.Shortcut);
                draft.SelectLibrary(dialog.Shortcut.Id);
                Store();
                rebuildLibrary();
                loadInspector();
            }
            return Task.CompletedTask;
        });
        add.Name = "DeckNewAction";
        add.Dock = DockStyle.Fill;
        add.AccessibleName = UiText.Get("Deck.NewAction");
        desktopTips.SetToolTip(add, UiText.Get("Deck.NewHint"));
        add.Enabled = shortcuts.Count < 64;

        header.RowStyles.Add(new RowStyle(SizeType.Absolute, Dip(grid, 30)));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var libraryTitle = new Label { Text = UiText.Get("Deck.Library"), Dock = DockStyle.Fill, ForeColor = TextPrimary, Font = new Font(Font.FontFamily, 11F, FontStyle.Bold) };
        header.Controls.Add(libraryTitle, 0, 0); header.SetColumnSpan(libraryTitle, 2);
        header.Controls.Add(search, 0, 1);
        header.Controls.Add(add, 1, 1);

        var oldHeader = libraryCard.Controls[0];
        libraryCard.Controls.Remove(oldHeader);
        oldHeader.Dispose();
        libraryCard.RowStyles[0].Height = Dip(grid, 76);
        libraryCard.Controls.Add(header, 0, 0);

        var library = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, AccessibleName = UiText.Get("Deck.LibraryScroll"), Padding = new Padding(0, 0, 4, 0) };
        libraryCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        libraryCard.Controls.Add(library, 0, libraryCard.RowCount++);


        var inspector = DesktopCard(UiText.Get("Deck.Inspector"));
        var inspectorTitleLabel = (Label)inspector.Controls[0];

        var slotChoice = new DarkComboBox { AccessibleName = "Slot" };
        slotChoice.Items.AddRange(new object[] { UiText.Get("Deck.LibraryOnly"), UiText.Format("Deck.Slot", 1), UiText.Format("Deck.Slot", 2), UiText.Format("Deck.Slot", 3), UiText.Format("Deck.Slot", 4), UiText.Format("Deck.Slot", 5), UiText.Format("Deck.Slot", 6) });

        var typeMap = new (string Key, string Label)[]
        {
            ("launch", UiText.Get("Deck.Launch")),
            ("script", UiText.Get("Deck.Script")),
            ("url", UiText.Get("Deck.Url")),
            ("hotkey", UiText.Get("Deck.Hotkey")),
            ("predefined", UiText.Get("Deck.BuiltIn"))
        };
        var type = new DarkComboBox { AccessibleName = UiText.Get("Deck.Type") };
        type.Items.AddRange(typeMap.Select(t => t.Label).Cast<object>().ToArray());

        string GetTypeKey() => typeMap.ElementAtOrDefault(Math.Max(0, type.SelectedIndex)).Key ?? "launch";
        void SetTypeKey(string key)
        {
            var idx = Array.FindIndex(typeMap, t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase) || (key == "powershell" && t.Key == "script"));
            type.SelectedIndex = idx >= 0 ? idx : 0;
        }

        var title = new TextBox { BackColor = SurfaceAlt, ForeColor = TextPrimary };
        title.Name = "DeckActionName";
        var target = new TextBox { BackColor = SurfaceAlt, ForeColor = TextPrimary };
        target.Name = "DeckActionTarget";
        var arguments = new TextBox { BackColor = SurfaceAlt, ForeColor = TextPrimary };

        arguments.Name = "DeckActionArguments";
        type.Name = "DeckActionType";
        slotChoice.Name = "DeckAssignment";
        var chooseIcon = CompactButton(UiText.Get("Deck.ChooseIcon"), () => {
            var index = shortcuts.FindIndex(s => s.Id == draft.SelectedActionId);
            if (index < 0) return Task.CompletedTask;
            var original = shortcuts[index];
            using var picker = new ShortcutIconPicker(original.Icon, original.IconData, original.IconUrl);
            if (picker.ShowDialog(this) == DialogResult.OK) {
                shortcuts[index] = original with { Icon = picker.IconKey, IconData = picker.IconData, IconUrl = picker.IconUrl };
                Store();
                refreshIcon();
            }
            return Task.CompletedTask;
        });

        void Pair(string a, Control first, string b, Control second) {
            var row = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var one = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Margin = Padding.Empty };
            one.RowStyles.Add(new RowStyle(SizeType.Absolute, Dip(grid, 22)));
            one.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            first.Margin = new Padding(0, 0, 6, 0);
            first.Dock = DockStyle.Fill;
            first.AccessibleName = a;
            one.Controls.Add(new Label { Text = a, Dock = DockStyle.Fill, ForeColor = TextMuted }, 0, 0);
            one.Controls.Add(first, 0, 1);
            var two = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Margin = Padding.Empty };
            two.RowStyles.Add(new RowStyle(SizeType.Absolute, Dip(grid, 22)));
            two.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            second.Margin = Padding.Empty;
            second.Dock = DockStyle.Fill;
            second.AccessibleName = b;
            two.Controls.Add(new Label { Text = b, Dock = DockStyle.Fill, ForeColor = TextMuted }, 0, 0);
            two.Controls.Add(second, 0, 1);
            row.Controls.Add(one, 0, 0);
            row.Controls.Add(two, 1, 0);
            inspector.RowStyles.Add(new RowStyle(SizeType.Absolute, Dip(grid, 54)));
            inspector.Controls.Add(row, 0, inspector.RowCount++);
        }

        Pair("Slot", slotChoice, UiText.Get("Deck.ActionType"), type);
        slotChoice.AccessibleName = "Slot";
        type.AccessibleName = UiText.Get("Deck.Type");
        Pair(UiText.Get("Deck.Title"), title, UiText.Get("Deck.Icon"), chooseIcon);
        title.AccessibleName = UiText.Get("Deck.Title");
        chooseIcon.AccessibleName = UiText.Get("Deck.Icon");

        var targetLabel = new Label { Text = UiText.Get("Deck.ExecutablePath"), Dock = DockStyle.Fill, ForeColor = TextMuted };
        var targetRow = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, Margin = Padding.Empty };
        targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Dip(grid, 86)));
        target.Dock = DockStyle.Fill;
        target.AccessibleName = UiText.Get("Deck.TargetAccessible");
        var browse = CompactButton(UiText.Get("Deck.Browse"), () => {
            var k = GetTypeKey();
            using var dlg = new OpenFileDialog();
            if (k == "script") {
                dlg.Title = UiText.Get("Deck.ChooseScript");
                dlg.Filter = UiText.Get("Deck.ScriptFilter");
            } else {
                dlg.Title = UiText.Get("Deck.ChooseApp");
                dlg.Filter = UiText.Get("Deck.AppFilter");
            }
            if (dlg.ShowDialog(this) == DialogResult.OK) {
                target.Text = dlg.FileName;
                edit();
            }
            return Task.CompletedTask;
        });
        browse.Dock = DockStyle.Fill;
        browse.AccessibleName = UiText.Get("Deck.BrowseHint");
        var targetHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        targetHost.Controls.Add(target);
        targetRow.Controls.Add(targetHost, 0, 0);
        targetRow.Controls.Add(browse, 1, 0);
        var builtIn = new DarkComboBox { Dock = DockStyle.Fill, Name = "DeckBuiltInAction", AccessibleName = UiText.Get("Deck.BuiltIn") };
        builtIn.Items.Add(UiText.Get("Deck.ToggleMic"));
        targetHost.Controls.Add(builtIn);
        builtIn.Visible = false;
        builtIn.SelectedIndexChanged += (_, _) => { if (!loading && builtIn.SelectedIndex == 0) target.Text = ShortcutEditorDialog.ToggleMicrophoneAdapterKey; };

        var targetFieldRow = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Margin = Padding.Empty };
        targetFieldRow.RowStyles.Add(new RowStyle(SizeType.Absolute, Dip(grid, 22)));
        targetFieldRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        targetFieldRow.Controls.Add(targetLabel, 0, 0);
        targetFieldRow.Controls.Add(targetRow, 0, 1);
        inspector.RowStyles.Add(new RowStyle(SizeType.Absolute, Dip(grid, 54)));
        inspector.Controls.Add(targetFieldRow, 0, inspector.RowCount++);

        CompactField(inspector, UiText.Get("Deck.ArgumentsOptional"), arguments);
        desktopTips.SetToolTip(arguments, UiText.Get("Deck.ArgumentsHint"));

        var swatches = Swatches([("Graphite", "#303640"), ("Ocean", "#155E75"), ("Cobalt", "#1D4ED8"), ("Ember", "#9A3412")], shortcuts.FirstOrDefault(s => s.Id == draft.SelectedActionId)?.Color ?? "#303640", v => { var i = shortcuts.FindIndex(s => s.Id == draft.SelectedActionId); if (i >= 0) { shortcuts[i] = shortcuts[i] with { Color = v }; Store(); } });
        var colors = new TableLayoutPanel { ColumnCount = 2 };
        colors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        colors.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Dip(grid, 94)));
        swatches.Dock = DockStyle.Fill;
        colors.Controls.Add(swatches, 0, 0);
        var inherit = CompactButton(UiText.Get("Deck.Default"), () => { var i = shortcuts.FindIndex(s => s.Id == draft.SelectedActionId); if (i >= 0) { shortcuts[i] = shortcuts[i] with { Color = null }; swatches.Tag = null; swatches.Invalidate(true); Store(); } return Task.CompletedTask; });
        inherit.Dock = DockStyle.Fill;
        inherit.AccessibleName = UiText.Get("Deck.Default");
        desktopTips.SetToolTip(inherit, UiText.Get("Deck.DefaultHint"));
        colors.Controls.Add(inherit, 1, 0);
        CompactField(inspector, UiText.Get("Deck.Color"), colors, 60);

        var displayMap = new (string Key, string Label)[]
        {
            ("both", UiText.Get("Deck.Both")),
            ("icon", UiText.Get("Deck.IconOnly")),
            ("label", UiText.Get("Deck.LabelOnly"))
        };
        var display = new DarkComboBox { AccessibleName = UiText.Get("Deck.Display") };
        display.Items.AddRange(displayMap.Select(d => d.Label).Cast<object>().ToArray());
        string GetDisplayKey() => displayMap.ElementAtOrDefault(Math.Max(0, display.SelectedIndex)).Key ?? "both";
        void SetDisplayKey(string key)
        {
            var idx = Array.FindIndex(displayMap, d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
            display.SelectedIndex = idx >= 0 ? idx : 0;
        }
        CompactField(inspector, UiText.Get("Deck.DisplayStyle"), display);
        desktopTips.SetToolTip(display, UiText.Get("Deck.DisplayHint"));

        var status = new Label { Dock = DockStyle.Fill, ForeColor = TextMuted, AutoEllipsis = true };
        CompactField(inspector, "Status", status, 44);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28)); // Zastosuj
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34)); // Wyczyść slot
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38)); // Usuń z biblioteki

        var apply = CompactButton(UiText.Get("Deck.Validate"), () => {
            edit();
            var item = shortcuts.FirstOrDefault(s => s.Id == draft.SelectedActionId);
            status.Text = item is not null && ShortcutEditorDialog.TryValidateShortcutDraft(item, out var error) ? UiText.Get("Deck.Ready") : UiText.Get("Deck.Required");
            return Task.CompletedTask;
        });
        desktopTips.SetToolTip(apply, UiText.Get("Deck.ApplyHint"));

        var clearSlot = CompactButton(UiText.Get("Deck.Detach"), () => {
            if (draft.SelectedSlot >= 0) {
                draft.Detach(draft.SelectedSlot);
                Store();
                loadInspector();
                rebuildLibrary();
            }
            return Task.CompletedTask;
        });
        desktopTips.SetToolTip(clearSlot, UiText.Get("Deck.DetachHint"));

        var delete = CompactButton(UiText.Get("Deck.Delete"), () => {
            if (draft.SelectedActionId is null) return Task.CompletedTask;
            if (MessageBox.Show(this, UiText.Get("Deck.DeleteConfirm"), UiText.Get("Deck.DeleteTitle"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return Task.CompletedTask;
            draft.DeleteSelected();
            Store();
            rebuildLibrary();
            loadInspector();
            return Task.CompletedTask;
        });
        delete.Name = "DeckDeleteDefinition";
        clearSlot.Name = "DeckDetachSlot";
        apply.Name = "DeckValidateAction";
        delete.AccessibleName = UiText.Get("Deck.Delete");
        desktopTips.SetToolTip(delete, UiText.Get("Deck.DeleteHint"));

        apply.Dock = clearSlot.Dock = delete.Dock = DockStyle.Fill;
        actions.Controls.Add(apply, 0, 0);
        actions.Controls.Add(clearSlot, 1, 0);
        actions.Controls.Add(delete, 2, 0);
        CompactField(inspector, "", actions, 42);
        var undo = CompactButton(UiText.Get("Deck.Undo"), () => {
            draft.UndoAssignment(); Store(); loadInspector(); rebuildLibrary(); return Task.CompletedTask;
        });
        undo.Name = "DeckUndoAssignment";
        CompactField(inspector, "", undo, 42);
        FinishCard(inspector);

        chooseIcon.Paint += (_, e) => {
            var item = shortcuts.FirstOrDefault(s => s.Id == draft.SelectedActionId);
            if (item is not null) ShortcutIconCatalog.Draw(e.Graphics, item.Icon, item.IconData, new Rectangle(5, (chooseIcon.Height - 24) / 2, 24, 24), TextPrimary);
        };
        chooseIcon.Name = "DeckActionIcon";
        display.Name = "DeckDisplayMode";
        swatches.Name = "DeckActionColor";
        chooseIcon.TextAlign = ContentAlignment.MiddleRight;
        chooseIcon.Padding = new Padding(28, 0, 0, 0);

        void chooseIconRefresh() { chooseIcon.Invalidate(); rebuildLibrary(); RefreshPreview(); }
        void Store() {
            configurationDraft = draft.ApplyTo(LoadConfiguration());
            MarkDraft();
            RefreshPreview();
            rebuildLibrary();
        }
        void RefreshPreview() {
            var appearance = pendingAppearance ?? LoadAppearanceConfiguration();
            preview.ShortcutsLayout = "grid-6";
            preview.Slots = slots.Select(id => shortcuts.FirstOrDefault(s => s.Id == id)).ToArray();
            preview.SelectedSlot = draft.SelectedSlot;
            preview.Snapshot = state.Snapshot();
            preview.Accent = ColorTranslator.FromHtml(appearance.Accent);
            preview.DisplayBackground = ColorTranslator.FromHtml(appearance.Background);
            preview.TileTheme = appearance.TileColorScheme;
            preview.Clock24Hour = (pendingDesktopAppearance ?? LoadDesktopAppearanceConfiguration()).Clock24Hour;
            preview.Artwork = desktopArtwork;
            preview.Invalidate();
            _ = LoadDesktopArtworkAsync(state.Snapshot(), desktopViewLifetime!.Token);
        }
        void LoadInspector() {
            loading = true;
            var item = shortcuts.FirstOrDefault(s => s.Id == draft.SelectedActionId);
            var assignedSlot = item is null ? -1 : slots[draft.SelectedSlot] == item.Id ? draft.SelectedSlot : Array.IndexOf(slots, item.Id);
            slotChoice.SelectedIndex = assignedSlot + 1;
            title.Text = item?.Name ?? "";
            SetTypeKey(item?.Action.Type ?? "hotkey");
            target.Text = item?.Action.Url ?? item?.Action.ExecutablePath ?? item?.Action.ScriptPath ?? item?.Action.AdapterKey ?? (item is null ? "" : string.Join("+", item.Action.Keys));
            arguments.Text = item is null ? "" : ShortcutEditorDialog.JoinArguments(item.Action.Arguments);
            arguments.Modified = false;
            SetDisplayKey(item?.DisplayMode ?? "both");

            var typeKey = GetTypeKey();
            targetLabel.Text = typeKey switch {
                "launch" => UiText.Get("Deck.ExecutablePath"),
                "script" => UiText.Get("Deck.ScriptPath"),
                "url" => UiText.Get("Deck.UrlHint"),
                "hotkey" => UiText.Get("Deck.KeysHint"),
                "predefined" => UiText.Get("Deck.SystemCommand"),
                _ => UiText.Get("Deck.Target")
            };
            browse.Visible = typeKey is "launch" or "script";
            builtIn.Visible = typeKey == "predefined";
            target.Visible = typeKey != "predefined";
            builtIn.SelectedIndex = item?.Action.AdapterKey == ShortcutEditorDialog.ToggleMicrophoneAdapterKey ? 0 : -1;

            if (item is not null && slots[draft.SelectedSlot] == draft.SelectedActionId) {
                inspectorTitleLabel.Text = UiText.Format("Deck.SlotInspector", draft.SelectedSlot + 1);
                clearSlot.Enabled = true;
            } else {
                inspectorTitleLabel.Text = draft.SelectedActionId is not null ? UiText.Get("Deck.LibraryEdit") : UiText.Get("Deck.Inspector");
                clearSlot.Enabled = false;
            }

            foreach (var control in new Control[] { title, type, target, arguments, chooseIcon, display, swatches, apply, delete }) {
                control.Enabled = item is not null;
            }
            slotChoice.Enabled = item is not null;
            arguments.Enabled = item is not null && typeKey is "launch" or "script";

            status.Text = item is null ? UiText.Get("Deck.SelectHint") : assignedSlot < 0 ? UiText.Get("Deck.Unassigned") : UiText.Format("Deck.Assigned", assignedSlot + 1);
            add.Enabled = shortcuts.Count < 64;
            inherit.Enabled = item is not null;
            undo.Enabled = draft.CanUndo;
            swatches.Tag = item?.Color;
            swatches.Invalidate(true);
            loading = false;
            chooseIcon.Invalidate();
            RefreshPreview();
        }
        void Edit() {
            if (loading) return;
            var i = shortcuts.FindIndex(s => s.Id == draft.SelectedActionId);
            if (i < 0) return;
            var original = shortcuts[i];
            var value = target.Text.Trim();
            var typeKey = GetTypeKey();
            var action = typeKey switch {
                "url" => new ShortcutActionDefinition { Type = "url", Url = value },
                "launch" => new ShortcutActionDefinition { Type = "launch", ExecutablePath = value, Arguments = original.Action.Arguments },
                "script" => new ShortcutActionDefinition { Type = original.Action.Type == "powershell" ? "powershell" : "script", ScriptPath = value, Arguments = original.Action.Arguments },
                "predefined" => new ShortcutActionDefinition { Type = "predefined", AdapterKey = value },
                _ => new ShortcutActionDefinition { Type = "hotkey", Keys = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) }
            };
            if (arguments.Modified) action = action with { Arguments = ShortcutEditorDialog.SplitArguments(arguments.Text) };
            shortcuts[i] = original with { Name = title.Text, Action = action, DisplayMode = GetDisplayKey() };
            Store();
            status.Text = ShortcutEditorDialog.TryValidateShortcutDraft(shortcuts[i], out var error) ? UiText.Get("Deck.Updated") : error;
        }
        void Assign(string id, int slot) {
            draft.Assign(id, slot);
            Store();
            loadInspector();
            rebuildLibrary();
        }
        void RebuildLibrary() {
            foreach (Control control in library.Controls.Cast<Control>().ToArray()) control.Dispose();
            library.Controls.Clear();
            var filtered = shortcuts.Where(s => s.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase)
                || s.Action.Type.Contains(search.Text, StringComparison.OrdinalIgnoreCase)
                || typeMap.FirstOrDefault(t => t.Key == s.Action.Type).Label?.Contains(search.Text, StringComparison.OrdinalIgnoreCase) == true).ToArray();
            var width = Math.Max(Dip(grid, 145), library.ClientSize.Width - Dip(grid, 28));
            foreach (var item in filtered) {
                var row = new Panel { Width = width, Height = Dip(grid, 72), BackColor = Surface, Margin = new Padding(3), AccessibleName = item.Name };
                var select = new RoundedButton { Dock = DockStyle.Fill, Text = "", FlatStyle = FlatStyle.Flat, BackColor = SurfaceAlt, AccessibleName = UiText.Get("Deck.EditPrefix") + item.Name };
                select.FlatAppearance.BorderColor = item.Id == draft.SelectedLibraryActionId ? Accent : Border;
                select.Paint += (_, e) => {
                    ShortcutIconCatalog.Draw(e.Graphics, item.Icon, item.IconData, new Rectangle(Dip(select, 8), Dip(select, 12), Dip(select, 24), Dip(select, 24)), TextPrimary);
                    TextRenderer.DrawText(e.Graphics, item.Name, select.Font, new Rectangle(Dip(select, 40), Dip(select, 7), Math.Max(1, select.Width - Dip(select, 44)), Dip(select, 25)), TextPrimary, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                    using var small = new Font(Font.FontFamily, 10);
                    var typeLabel = item.Action.Type switch {
                        "script" => UiText.Get("Deck.ScriptShort"),
                        "launch" => UiText.Get("Deck.Program"),
                        "predefined" => UiText.Get("Deck.BuiltIn"),
                        "url" => UiText.Get("Deck.Url"),
                        "hotkey" => item.Action.Keys?.Count > 0 ? string.Join("+", item.Action.Keys) : UiText.Get("Deck.Hotkey"),
                        _ => item.Action.Type
                    };
                    TextRenderer.DrawText(e.Graphics, typeLabel, small, new Rectangle(Dip(select, 40), Dip(select, 34), Math.Max(1, select.Width - Dip(select, 44)), Dip(select, 24)), TextMuted, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                };
                select.Click += (_, _) => { draft.SelectLibrary(item.Id); loadInspector(); rebuildLibrary(); };

                var assign = CompactButton("+", () => {
                    var slot = draft.SelectedSlot >= 0 ? draft.SelectedSlot : Array.FindIndex(slots, id => id is null);
                    if (slot < 0) { status.Text = UiText.Get("Deck.ChooseSlot"); return Task.CompletedTask; }
                    Assign(item.Id, slot);
                    return Task.CompletedTask;
                });
                assign.Dock = DockStyle.Right;
                assign.Width = Dip(grid, 32);
                assign.AccessibleName = UiText.Get("Deck.AssignPrefix") + item.Name;
                var targetSlotName = draft.SelectedSlot >= 0 ? UiText.Format("Deck.Slot", draft.SelectedSlot + 1) : UiText.Get("Deck.FirstFree");
                desktopTips.SetToolTip(assign, UiText.Format("Deck.AssignHint", item.Name, targetSlotName));

                row.Controls.Add(select);
                row.Controls.Add(assign);
                library.Controls.Add(row);
            }
            if (filtered.Length == 0) library.Controls.Add(new Label { Text = shortcuts.Count == 0 ? UiText.Get("Deck.EmptyLibrary") : UiText.Get("Deck.NoMatches"), AutoSize = true, ForeColor = TextMuted });
        }

        loadInspector = LoadInspector; rebuildLibrary = RebuildLibrary; refreshIcon = chooseIconRefresh; edit = Edit;
        title.TextChanged += (_, _) => edit();
        target.TextChanged += (_, _) => { edit(); desktopTips.SetToolTip(target, target.Text); };
        arguments.TextChanged += (_, _) => { edit(); desktopTips.SetToolTip(arguments, arguments.Text); };
        type.SelectedIndexChanged += (_, _) => {
            if (loading) return;
            var typeKey = GetTypeKey();
            targetLabel.Text = typeKey switch {
                "launch" => UiText.Get("Deck.ExecutablePath"),
                "script" => UiText.Get("Deck.ScriptPath"),
                "url" => UiText.Get("Deck.UrlHint"),
                "hotkey" => UiText.Get("Deck.KeysHint"),
                "predefined" => UiText.Get("Deck.SystemCommand"),
                _ => UiText.Get("Deck.Target")
            };
            browse.Visible = typeKey is "launch" or "script";
            builtIn.Visible = typeKey == "predefined";
            target.Visible = typeKey != "predefined";
            if (typeKey == "predefined") { builtIn.SelectedIndex = 0; target.Text = ShortcutEditorDialog.ToggleMicrophoneAdapterKey; }
            arguments.Enabled = typeKey is "launch" or "script";
            edit();
        };
        display.SelectedIndexChanged += (_, _) => edit();
        slotChoice.SelectedIndexChanged += (_, _) => {
            if (loading || draft.SelectedActionId is null) return;
            var slot = slotChoice.SelectedIndex - 1;
            if (slot >= 0) Assign(draft.SelectedActionId, slot);
            else {
                var assigned = Array.IndexOf(slots, draft.SelectedActionId);
                if (assigned >= 0) draft.Detach(assigned);
                Store(); loadInspector(); rebuildLibrary();
            }
        };

        preview.SelectSlot = slot => { draft.SelectSlot(slot); loadInspector(); rebuildLibrary(); };
        preview.UnassignSlot = slot => { draft.Detach(slot); Store(); loadInspector(); rebuildLibrary(); };
        preview.MoveSlot = (source, destination) => { draft.Swap(source, destination); Store(); loadInspector(); rebuildLibrary(); };
        search.TextChanged += (_, _) => rebuildLibrary();
        library.SizeChanged += (_, _) => {
            foreach (Control row in library.Controls) row.Width = Math.Max(Dip(grid, 145), library.ClientSize.Width - Dip(grid, 28));
        };
        desktopRefresh = RefreshPreview;
        var inspectorScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Name = "DeckInspectorScroll" };
        inspector.Dock = DockStyle.Top;
        inspector.Height = Dip(grid, 720);
        inspectorScroll.Controls.Add(inspector);
        grid.Controls.Add(libraryCard, 0, 0);
        grid.Controls.Add(previewCard, 1, 0);
        grid.Controls.Add(inspectorScroll, 2, 0);
        AddPageBodyControl(page, grid);
        loadInspector();
        rebuildLibrary();
        return page;
    }
}
