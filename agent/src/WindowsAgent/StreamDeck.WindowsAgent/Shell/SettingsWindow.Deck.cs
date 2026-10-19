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
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 390));

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 338));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var previewCard = DesktopCard("CYD Display Preview");
        var preview = new DesktopCanvas { Dock = DockStyle.Fill, BackColor = Surface, Page = "Deck" };
        CompactField(previewCard, "", Segments([("Shortcuts Page", "Deck"), ("Home Screen (4)", "Home")], "Deck", v => { preview.Page = v; preview.Invalidate(); }), 42);
        previewCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewCard.Controls.Add(preview, 0, previewCard.RowCount++);
        left.Controls.Add(previewCard, 0, 0);

        var libraryCard = DesktopCard("Shortcut Library");
        var header = new TableLayoutPanel { ColumnCount = 3, Dock = DockStyle.Fill, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
        var search = new TextBox { PlaceholderText = "Szukaj skrótów...", AccessibleName = "Search Shortcut Library", BackColor = SurfaceAlt, ForeColor = TextPrimary, Dock = DockStyle.Fill };
        var add = CompactButton("+ Nowy", () => {
            using var dialog = new ShortcutEditorDialog();
            if (dialog.ShowDialog(this) == DialogResult.OK) {
                shortcuts.Add(dialog.Shortcut);
                selectedId = dialog.Shortcut.Id;
                Store();
                rebuildLibrary();
                loadInspector();
            }
            return Task.CompletedTask;
        });
        add.Dock = DockStyle.Fill;
        add.AccessibleName = "New Shortcut";
        desktopTips.SetToolTip(add, "Dodaj nowy skrót do biblioteki");
        add.Enabled = shortcuts.Count < 64;

        header.Controls.Add(new Label { Text = "Biblioteka skrótów", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font.FontFamily, 10, FontStyle.Bold) }, 0, 0);
        header.Controls.Add(search, 1, 0);
        header.Controls.Add(add, 2, 0);

        var oldHeader = libraryCard.Controls[0];
        libraryCard.Controls.Remove(oldHeader);
        oldHeader.Dispose();
        libraryCard.RowStyles[0].Height = 38;
        libraryCard.Controls.Add(header, 0, 0);

        var library = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, AccessibleName = "Shortcut Library Scroll", Padding = new Padding(0, 0, 4, 0) };
        libraryCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        libraryCard.Controls.Add(library, 0, libraryCard.RowCount++);
        left.Controls.Add(libraryCard, 0, 1);

        var inspector = DesktopCard("Inspektor kafelka");
        var inspectorTitleLabel = (Label)inspector.Controls[0];

        var slotChoice = new DarkComboBox { AccessibleName = "Slot" };
        slotChoice.Items.AddRange(new object[] { "Tylko w bibliotece", "Slot 1", "Slot 2", "Slot 3", "Slot 4", "Slot 5", "Slot 6" });

        var typeMap = new (string Key, string Label)[]
        {
            ("launch", "Aplikacja (.exe)"),
            ("script", "Skrypt (.ps1, .bat)"),
            ("url", "Adres URL"),
            ("hotkey", "Skrót klawiszowy"),
            ("predefined", "Akcja systemowa")
        };
        var type = new DarkComboBox { AccessibleName = "Type" };
        type.Items.AddRange(typeMap.Select(t => t.Label).Cast<object>().ToArray());

        string GetTypeKey() => typeMap.ElementAtOrDefault(Math.Max(0, type.SelectedIndex)).Key ?? "launch";
        void SetTypeKey(string key)
        {
            var idx = Array.FindIndex(typeMap, t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase) || (key == "powershell" && t.Key == "script"));
            type.SelectedIndex = idx >= 0 ? idx : 0;
        }

        var title = new TextBox { BackColor = SurfaceAlt, ForeColor = TextPrimary };
        var target = new TextBox { BackColor = SurfaceAlt, ForeColor = TextPrimary };
        var arguments = new TextBox { BackColor = SurfaceAlt, ForeColor = TextPrimary };

        var chooseIcon = CompactButton("Wybierz ikonę…", () => {
            var index = shortcuts.FindIndex(s => s.Id == selectedId);
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
            one.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            one.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            first.Margin = new Padding(0, 0, 6, 0);
            first.Dock = DockStyle.Fill;
            first.AccessibleName = a;
            one.Controls.Add(new Label { Text = a, Dock = DockStyle.Fill, ForeColor = TextMuted }, 0, 0);
            one.Controls.Add(first, 0, 1);
            var two = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Margin = Padding.Empty };
            two.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            two.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            second.Margin = Padding.Empty;
            second.Dock = DockStyle.Fill;
            second.AccessibleName = b;
            two.Controls.Add(new Label { Text = b, Dock = DockStyle.Fill, ForeColor = TextMuted }, 0, 0);
            two.Controls.Add(second, 0, 1);
            row.Controls.Add(one, 0, 0);
            row.Controls.Add(two, 1, 0);
            inspector.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            inspector.Controls.Add(row, 0, inspector.RowCount++);
        }

        Pair("Slot", slotChoice, "Typ akcji", type);
        slotChoice.AccessibleName = "Slot";
        type.AccessibleName = "Type";
        Pair("Tytuł / Etykieta", title, "Ikona", chooseIcon);
        title.AccessibleName = "Title";
        chooseIcon.AccessibleName = "Icon";

        var targetLabel = new Label { Text = "Ścieżka do aplikacji (.exe)", Dock = DockStyle.Fill, ForeColor = TextMuted };
        var targetRow = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, Margin = Padding.Empty };
        targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        target.Dock = DockStyle.Fill;
        target.AccessibleName = "Shortcut / URL / Path / Adapter";
        var browse = CompactButton("Przeglądaj…", () => {
            var k = GetTypeKey();
            using var dlg = new OpenFileDialog();
            if (k == "script") {
                dlg.Title = "Wybierz skrypt";
                dlg.Filter = "Skrypty (*.ps1;*.bat;*.cmd)|*.ps1;*.bat;*.cmd|Wszystkie pliki (*.*)|*.*";
            } else {
                dlg.Title = "Wybierz aplikację";
                dlg.Filter = "Aplikacje (*.exe)|*.exe|Wszystkie pliki (*.*)|*.*";
            }
            if (dlg.ShowDialog(this) == DialogResult.OK) {
                target.Text = dlg.FileName;
                edit();
            }
            return Task.CompletedTask;
        });
        browse.Dock = DockStyle.Fill;
        browse.AccessibleName = "Browse for application or script";
        targetRow.Controls.Add(target, 0, 0);
        targetRow.Controls.Add(browse, 1, 0);

        var targetFieldRow = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Margin = Padding.Empty };
        targetFieldRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        targetFieldRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        targetFieldRow.Controls.Add(targetLabel, 0, 0);
        targetFieldRow.Controls.Add(targetRow, 0, 1);
        inspector.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        inspector.Controls.Add(targetFieldRow, 0, inspector.RowCount++);

        CompactField(inspector, "Argumenty (opcjonalne)", arguments);
        desktopTips.SetToolTip(arguments, "Dodatkowe parametry polecenia przekazywane do programu lub skryptu.");

        var swatches = Swatches([("Graphite", "#303640"), ("Ocean", "#155E75"), ("Cobalt", "#1D4ED8"), ("Ember", "#9A3412")], shortcuts.FirstOrDefault(s => s.Id == selectedId)?.Color ?? "#303640", v => { var i = shortcuts.FindIndex(s => s.Id == selectedId); if (i >= 0) { shortcuts[i] = shortcuts[i] with { Color = v }; Store(); } });
        var colors = new TableLayoutPanel { ColumnCount = 2 };
        colors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        colors.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        swatches.Dock = DockStyle.Fill;
        colors.Controls.Add(swatches, 0, 0);
        var inherit = CompactButton("Domyślny", () => { var i = shortcuts.FindIndex(s => s.Id == selectedId); if (i >= 0) { shortcuts[i] = shortcuts[i] with { Color = null }; Store(); } return Task.CompletedTask; });
        inherit.Dock = DockStyle.Fill;
        inherit.AccessibleName = "Default";
        desktopTips.SetToolTip(inherit, "Użyj domyślnego motywu kafelków (bez nadpisania koloru)");
        colors.Controls.Add(inherit, 1, 0);
        CompactField(inspector, "Kolor kafelka", colors, 60);

        var displayMap = new (string Key, string Label)[]
        {
            ("both", "Ikona i etykieta"),
            ("icon", "Tylko ikona"),
            ("label", "Tylko etykieta")
        };
        var display = new DarkComboBox { AccessibleName = "Display" };
        display.Items.AddRange(displayMap.Select(d => d.Label).Cast<object>().ToArray());
        string GetDisplayKey() => displayMap.ElementAtOrDefault(Math.Max(0, display.SelectedIndex)).Key ?? "both";
        void SetDisplayKey(string key)
        {
            var idx = Array.FindIndex(displayMap, d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
            display.SelectedIndex = idx >= 0 ? idx : 0;
        }
        CompactField(inspector, "Styl wyświetlania (CYD)", display);
        desktopTips.SetToolTip(display, "Określa sposób prezentacji skrótu na ekranie CYD (Shortcuts Page oraz Home Screen).");

        var status = new Label { Dock = DockStyle.Fill, ForeColor = TextMuted, AutoEllipsis = true };
        CompactField(inspector, "Status", status, 44);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28)); // Zastosuj
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34)); // Wyczyść slot
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38)); // Usuń z biblioteki

        var apply = CompactButton("Zastosuj", () => {
            edit();
            var item = shortcuts.FirstOrDefault(s => s.Id == selectedId);
            status.Text = item is not null && ShortcutEditorDialog.TryValidateShortcutDraft(item, out var error) ? "Gotowe do zapisu" : "Uzupełnij wymagane pola";
            return Task.CompletedTask;
        });
        desktopTips.SetToolTip(apply, "Aktualizuje definicję skrótu w bieżącym szkicu. Kliknij 'Zapisz i synchronizuj', aby zapisać na dysku i wysłać do CYD.");

        var clearSlot = CompactButton("Wyczyść slot", () => {
            if (selectedSlot >= 0) {
                slots[selectedSlot] = null;
                Store();
                loadInspector();
                rebuildLibrary();
            }
            return Task.CompletedTask;
        });
        desktopTips.SetToolTip(clearSlot, "Odczepia skrót z wybranego kafelka, zachowując go w bibliotece.");

        var delete = CompactButton("Usuń z biblioteki", () => {
            if (selectedId is null) return Task.CompletedTask;
            if (MessageBox.Show(this, "Czy na pewno chcesz trwale usunąć ten skrót z biblioteki?\n\nZostanie on również odczepiony ze wszystkich kafelków.", "Usuń skrót", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return Task.CompletedTask;
            shortcuts.RemoveAll(s => s.Id == selectedId);
            for (var i = 0; i < 6; ++i) if (slots[i] == selectedId) slots[i] = null;
            selectedId = shortcuts.FirstOrDefault()?.Id;
            Store();
            rebuildLibrary();
            loadInspector();
            return Task.CompletedTask;
        });
        delete.AccessibleName = "Delete";
        desktopTips.SetToolTip(delete, "Usuwa skrót z biblioteki i odczepia go ze wszystkich przypisanych kafelków.");

        apply.Dock = clearSlot.Dock = delete.Dock = DockStyle.Fill;
        actions.Controls.Add(apply, 0, 0);
        actions.Controls.Add(clearSlot, 1, 0);
        actions.Controls.Add(delete, 2, 0);
        CompactField(inspector, "", actions, 42);
        FinishCard(inspector);

        chooseIcon.Paint += (_, e) => {
            var item = shortcuts.FirstOrDefault(s => s.Id == selectedId);
            if (item is not null) ShortcutIconCatalog.Draw(e.Graphics, item.Icon, item.IconData, new Rectangle(5, (chooseIcon.Height - 24) / 2, 24, 24), TextPrimary);
        };
        chooseIcon.TextAlign = ContentAlignment.MiddleRight;
        chooseIcon.Padding = new Padding(28, 0, 0, 0);

        void chooseIconRefresh() { chooseIcon.Invalidate(); rebuildLibrary(); RefreshPreview(); }
        void Store() {
            var current = LoadConfiguration() with { Shortcuts = shortcuts.ToArray() };
            var all = GetProfiles(current).Select(p => p.Name == current.ActiveProfile ? p with { AssignedShortcutIds = slots.ToArray() } : p).ToArray();
            configurationDraft = current with { Profiles = all };
            MarkDraft();
            RefreshPreview();
        }
        void RefreshPreview() {
            var appearance = pendingAppearance ?? LoadAppearanceConfiguration();
            preview.ShortcutsLayout = appearance.ShortcutsLayout;
            preview.Slots = slots.Select(id => shortcuts.FirstOrDefault(s => s.Id == id)).ToArray();
            preview.SelectedSlot = selectedSlot;
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
            var item = shortcuts.FirstOrDefault(s => s.Id == selectedId);
            selectedSlot = item is null ? selectedSlot : Array.IndexOf(slots, item.Id);
            slotChoice.SelectedIndex = selectedSlot + 1;
            title.Text = item?.Name ?? "";
            SetTypeKey(item?.Action.Type ?? "hotkey");
            target.Text = item?.Action.Url ?? item?.Action.ExecutablePath ?? item?.Action.ScriptPath ?? item?.Action.AdapterKey ?? (item is null ? "" : string.Join("+", item.Action.Keys));
            arguments.Text = item is null ? "" : string.Join(" ", item.Action.Arguments);
            arguments.Modified = false;
            SetDisplayKey(item?.DisplayMode ?? "both");

            var typeKey = GetTypeKey();
            targetLabel.Text = typeKey switch {
                "launch" => "Ścieżka do aplikacji (.exe)",
                "script" => "Ścieżka do skryptu (.ps1, .bat)",
                "url" => "Adres URL (np. https://...)",
                "hotkey" => "Kombinacja klawiszy (np. Ctrl+Shift+Esc)",
                "predefined" => "Polecenie systemowe",
                _ => "Cel / Ścieżka"
            };
            browse.Visible = typeKey is "launch" or "script";

            if (selectedSlot >= 0 && slots[selectedSlot] == selectedId) {
                inspectorTitleLabel.Text = $"Inspektor kafelka (Kafelek {selectedSlot + 1})";
                clearSlot.Enabled = true;
            } else {
                inspectorTitleLabel.Text = selectedId is not null ? "Edycja skrótu w bibliotece" : "Inspektor kafelka";
                clearSlot.Enabled = false;
            }

            foreach (var control in new Control[] { title, type, target, arguments, chooseIcon, display, swatches, apply, delete }) {
                control.Enabled = item is not null;
            }

            status.Text = item is null ? "Wybierz skrót w bibliotece lub kafelek" : selectedSlot < 0 ? "Tylko w bibliotece (nieprzypisany)" : $"Przypisany do kafelka {selectedSlot + 1}";
            swatches.Tag = item?.Color;
            swatches.Invalidate(true);
            loading = false;
            chooseIcon.Invalidate();
            RefreshPreview();
        }
        void Edit() {
            if (loading) return;
            var i = shortcuts.FindIndex(s => s.Id == selectedId);
            if (i < 0) return;
            var original = shortcuts[i];
            var value = target.Text.Trim();
            var typeKey = GetTypeKey();
            var action = typeKey switch {
                "url" => new ShortcutActionDefinition { Type = "url", Url = value },
                "launch" => new ShortcutActionDefinition { Type = "launch", ExecutablePath = value, Arguments = original.Action.Arguments },
                "script" => new ShortcutActionDefinition { Type = "script", ScriptPath = value, Arguments = original.Action.Arguments },
                "predefined" => new ShortcutActionDefinition { Type = "predefined", AdapterKey = value },
                _ => new ShortcutActionDefinition { Type = "hotkey", Keys = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) }
            };
            if (arguments.Modified) action = action with { Arguments = ShortcutEditorDialog.SplitArguments(arguments.Text) };
            shortcuts[i] = original with { Name = title.Text, Action = action, DisplayMode = GetDisplayKey() };
            Store();
            status.Text = ShortcutEditorDialog.TryValidateShortcutDraft(shortcuts[i], out var error) ? "Zaktualizowano w szkicu" : error;
        }
        void Assign(string id, int slot) {
            for (var i = 0; i < 6; ++i) if (slots[i] == id) slots[i] = null;
            slots[slot] = id;
            selectedId = id;
            selectedSlot = slot;
            Store();
            loadInspector();
            rebuildLibrary();
        }
        void RebuildLibrary() {
            foreach (Control control in library.Controls.Cast<Control>().ToArray()) control.Dispose();
            library.Controls.Clear();
            var filtered = shortcuts.Where(s => s.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || s.Action.Type.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            var columns = Math.Max(2, library.ClientSize.Width / 150);
            var width = Math.Max(145, (library.ClientSize.Width - 22) / columns - 6);
            foreach (var item in filtered) {
                var row = new Panel { Width = width, Height = 66, BackColor = SurfaceAlt, Margin = new Padding(3), AccessibleName = item.Name };
                var select = new RoundedButton { Dock = DockStyle.Fill, Text = "", FlatStyle = FlatStyle.Flat, BackColor = SurfaceAlt, AccessibleName = "Edit " + item.Name };
                select.FlatAppearance.BorderColor = item.Id == selectedId ? Accent : Border;
                select.Paint += (_, e) => {
                    ShortcutIconCatalog.Draw(e.Graphics, item.Icon, item.IconData, new Rectangle(8, 12, 24, 24), TextPrimary);
                    TextRenderer.DrawText(e.Graphics, item.Name, Font, new Rectangle(40, 7, Math.Max(1, select.Width - 44), 25), TextPrimary, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                    using var small = new Font(Font.FontFamily, 9);
                    var typeLabel = item.Action.Type switch {
                        "script" => "Skrypt",
                        "launch" => "Program",
                        "predefined" => "Akcja systemowa",
                        "url" => "Adres URL",
                        "hotkey" => item.Action.Keys?.Count > 0 ? string.Join("+", item.Action.Keys) : "Skrót klawiszowy",
                        _ => item.Action.Type
                    };
                    TextRenderer.DrawText(e.Graphics, typeLabel, small, new Rectangle(40, 34, Math.Max(1, select.Width - 44), 20), TextMuted, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                };
                select.Click += (_, _) => { selectedId = item.Id; loadInspector(); rebuildLibrary(); };

                var assign = CompactButton("+", () => {
                    var slot = selectedSlot >= 0 ? selectedSlot : Array.FindIndex(slots, id => id is null);
                    if (slot < 0) { status.Text = "Wybierz kafelek do zastąpienia"; return Task.CompletedTask; }
                    Assign(item.Id, slot);
                    return Task.CompletedTask;
                });
                assign.Dock = DockStyle.Right;
                assign.Width = 32;
                assign.AccessibleName = "Assign " + item.Name;
                var targetSlotName = selectedSlot >= 0 ? $"Kafelka {selectedSlot + 1}" : "pierwszego wolnego kafelka";
                desktopTips.SetToolTip(assign, $"Przypisz '{item.Name}' do {targetSlotName}");

                row.Controls.Add(select);
                row.Controls.Add(assign);
                row.SizeChanged += (_, _) => {
                    using var path = CreateRoundedRectPath(new Rectangle(0, 0, row.Width, row.Height), 9);
                    var old = row.Region;
                    row.Region = new Region(path);
                    old?.Dispose();
                };
                using (var path = CreateRoundedRectPath(new Rectangle(0, 0, row.Width, row.Height), 9)) row.Region = new Region(path);
                library.Controls.Add(row);
            }
            if (filtered.Length == 0) library.Controls.Add(new Label { Text = shortcuts.Count == 0 ? "Brak skrótów. Dodaj swój pierwszy skrót." : "Brak pasujących skrótów", AutoSize = true, ForeColor = TextMuted });
        }

        loadInspector = LoadInspector; rebuildLibrary = RebuildLibrary; refreshIcon = chooseIconRefresh; edit = Edit;
        title.TextChanged += (_, _) => edit();
        target.TextChanged += (_, _) => { edit(); desktopTips.SetToolTip(target, target.Text); };
        arguments.TextChanged += (_, _) => { edit(); desktopTips.SetToolTip(arguments, arguments.Text); };
        type.SelectedIndexChanged += (_, _) => {
            var typeKey = GetTypeKey();
            targetLabel.Text = typeKey switch {
                "launch" => "Ścieżka do aplikacji (.exe)",
                "script" => "Ścieżka do skryptu (.ps1, .bat)",
                "url" => "Adres URL (np. https://...)",
                "hotkey" => "Kombinacja klawiszy (np. Ctrl+Shift+Esc)",
                "predefined" => "Polecenie systemowe",
                _ => "Cel / Ścieżka"
            };
            browse.Visible = typeKey is "launch" or "script";
            edit();
        };
        display.SelectedIndexChanged += (_, _) => edit();
        slotChoice.SelectedIndexChanged += (_, _) => {
            if (loading || selectedId is null) return;
            var slot = slotChoice.SelectedIndex - 1;
            if (slot >= 0) Assign(selectedId, slot);
            else {
                for (var i = 0; i < 6; ++i) if (slots[i] == selectedId) slots[i] = null;
                selectedSlot = -1;
                Store();
                rebuildLibrary();
            }
        };

        preview.SelectSlot = slot => { selectedSlot = slot; selectedId = slots[slot]; loadInspector(); rebuildLibrary(); };
        preview.UnassignSlot = slot => { slots[slot] = null; selectedSlot = slot; selectedId = null; Store(); loadInspector(); rebuildLibrary(); };
        preview.MoveSlot = (source, destination) => { (slots[source], slots[destination]) = (slots[destination], slots[source]); selectedSlot = destination; selectedId = slots[destination]; Store(); loadInspector(); rebuildLibrary(); };
        search.TextChanged += (_, _) => rebuildLibrary();
        library.SizeChanged += (_, _) => rebuildLibrary();
        desktopRefresh = RefreshPreview;
        grid.Controls.Add(left, 0, 0);
        grid.Controls.Add(inspector, 1, 0);
        AddPageBodyControl(page, grid);
        loadInspector();
        rebuildLibrary();
        return page;
    }
}
