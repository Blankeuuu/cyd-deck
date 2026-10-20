using System.Runtime.InteropServices;
using StreamDeck.Shortcuts;

namespace StreamDeck.WindowsAgent.Shell;

internal sealed class ShortcutEditorDialog : Form
{
    private const int DialogWidth = 620;
    private const int DialogHeight = 620;
    private const int FieldLabelWidth = 146;
    private const int FieldRowHeight = 46;
    private const int PrimaryButtonWidth = 150;
    private const int PrimaryButtonHeight = 38;
    internal const string LaunchApplicationChoice = "Launch application";
    internal const string OpenUrlChoice = "Open URL";
    internal const string SendHotkeyChoice = "Send hotkey";
    internal const string RunScriptChoice = "Run script";
    internal const string BuiltInActionChoice = "Built-in action";
    internal const string DisplayLabelChoice = "Label";
    internal const string DisplayIconChoice = "Icon";
    internal const string DisplayBothChoice = "Label and icon";
    internal const string ToggleMicrophoneChoice = "Toggle microphone";
    internal const string ToggleMicrophoneAdapterKey = "audio:toggle-mic";

    private readonly TextBox idText = new();
    private readonly TextBox nameText = new();
    private readonly TextBox iconText = new();
    private readonly ComboBox displayModeCombo = new();
    private readonly Button chooseIconButton = new();
    private string? loadedIconData;
    private string? loadedIconUrl;
    private readonly ComboBox typeCombo = new();
    private readonly TextBox targetText = new();
    private readonly TextBox argumentsText = new();
    private readonly ComboBox builtInActionCombo = new();
    private readonly Label hintLabel = new();
    private readonly Button browseButton = new();
    private readonly Button clearHotkeyButton = new();
    private readonly TableLayoutPanel actionPanel = new();
    private readonly Panel previewBadge = new();
    private readonly Label previewBadgeLabel = new();
    private readonly Button testActionButton = new();
    private string? loadedActionType;
    private string? loadedTileColor;

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

    public ShortcutEditorDialog(ShortcutDefinition? shortcut = null)
    {
        var isLight = SettingsWindow.IsClientLightTheme;
        BackColor = isLight ? Color.FromArgb(246, 247, 249) : Color.FromArgb(18, 20, 25);
        ForeColor = isLight ? Color.FromArgb(29, 32, 37) : Color.FromArgb(238, 241, 245);
        var inputBg = isLight ? Color.FromArgb(236, 238, 242) : Color.FromArgb(28, 32, 40);
        var inputBorder = isLight ? Color.FromArgb(210, 214, 220) : Color.FromArgb(42, 46, 56);

        Text = shortcut is null ? UiText.Get("Deck.AddDialog") : UiText.Get("Deck.EditDialog");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Width = DialogWidth;
        Height = DialogHeight;
        MinimumSize = new Size(DialogWidth, DialogHeight);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point);
        AccessibleName = Text;
        AccessibleDescription = UiText.Get("Deck.DialogHelp");

        foreach (var combo in new[] { typeCombo, displayModeCombo, builtInActionCombo }) {
            combo.FormattingEnabled = true;
            combo.Format += (_, e) => e.Value = LocalizedChoice(e.ListItem?.ToString());
        }
        typeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        typeCombo.AccessibleName = UiText.Get("Deck.Action");
        typeCombo.BackColor = inputBg;
        typeCombo.ForeColor = ForeColor;
        typeCombo.FlatStyle = FlatStyle.Flat;
        typeCombo.Items.AddRange(GetActionChoices().Cast<object>().ToArray());
        typeCombo.SelectedIndexChanged += (_, _) => ApplySelectedActionChoice();

        builtInActionCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        builtInActionCombo.AccessibleName = UiText.Get("Deck.BuiltIn");
        builtInActionCombo.BackColor = inputBg;
        builtInActionCombo.ForeColor = ForeColor;
        builtInActionCombo.FlatStyle = FlatStyle.Flat;
        builtInActionCombo.Items.AddRange(GetBuiltInActionChoices().Cast<object>().ToArray());

        displayModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        displayModeCombo.AccessibleName = UiText.Get("Deck.DisplayMode");
        displayModeCombo.BackColor = inputBg;
        displayModeCombo.ForeColor = ForeColor;
        displayModeCombo.FlatStyle = FlatStyle.Flat;
        displayModeCombo.Items.AddRange(GetDisplayModeChoices().Cast<object>().ToArray());
        displayModeCombo.SelectedIndexChanged += (_, _) => UpdatePreview();

        chooseIconButton.Text = UiText.Get("Deck.ChooseIcon");
        chooseIconButton.AccessibleName = UiText.Get("Deck.IconAccessible");
        chooseIconButton.BackColor = isLight ? Color.FromArgb(236, 238, 242) : Color.FromArgb(39, 43, 52);
        chooseIconButton.ForeColor = ForeColor;
        chooseIconButton.FlatStyle = FlatStyle.Flat;
        chooseIconButton.FlatAppearance.BorderColor = inputBorder;
        chooseIconButton.Click += (_, _) => {
            using var picker = new ShortcutIconPicker(iconText.Text, loadedIconData, loadedIconUrl);
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            loadedIconData = picker.IconData; loadedIconUrl = picker.IconUrl; iconText.Text = picker.IconKey; UpdatePreview();
        };

        idText.ReadOnly = true;
        idText.TabStop = false;
        idText.AccessibleName = UiText.Get("Deck.Id");
        idText.PlaceholderText = "e.g. shortcut-browser";
        idText.BackColor = inputBg;
        idText.ForeColor = ForeColor;
        idText.BorderStyle = BorderStyle.FixedSingle;

        nameText.AccessibleName = UiText.Get("Deck.NameAccessible");
        nameText.PlaceholderText = UiText.Get("Deck.NamePlaceholder");
        nameText.BackColor = inputBg;
        nameText.ForeColor = ForeColor;
        nameText.BorderStyle = BorderStyle.FixedSingle;
        nameText.TextChanged += (_, _) => UpdatePreview();

        iconText.ReadOnly = true;
        iconText.AccessibleName = UiText.Get("Deck.IconAccessible2");
        iconText.PlaceholderText = "e.g. browser, app, audio";
        iconText.BackColor = inputBg;
        iconText.ForeColor = ForeColor;
        iconText.BorderStyle = BorderStyle.FixedSingle;
        iconText.TextChanged += (_, _) => UpdatePreview();

        targetText.AccessibleName = UiText.Get("Deck.TargetName");
        targetText.BackColor = inputBg;
        targetText.ForeColor = ForeColor;
        targetText.BorderStyle = BorderStyle.FixedSingle;

        argumentsText.AccessibleName = UiText.Get("Deck.ArgumentsName");
        argumentsText.PlaceholderText = "e.g. --profile Default";
        argumentsText.BackColor = inputBg;
        argumentsText.ForeColor = ForeColor;
        argumentsText.BorderStyle = BorderStyle.FixedSingle;

        targetText.KeyDown += (s, e) =>
        {
            if (string.Equals(typeCombo.SelectedItem?.ToString(), SendHotkeyChoice, StringComparison.Ordinal))
            {
                if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
                var keys = new List<string>();
                if (e.Control) keys.Add("CTRL");
                if (e.Alt) keys.Add("ALT");
                if (e.Shift) keys.Add("SHIFT");
                keys.Add(e.KeyCode.ToString().ToUpperInvariant());
                targetText.Text = string.Join("+", keys);
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        };

        browseButton.Text = UiText.Get("Deck.Browse");
        browseButton.AccessibleName = UiText.Get("Deck.BrowseTarget");
        browseButton.BackColor = isLight ? Color.FromArgb(236, 238, 242) : Color.FromArgb(39, 43, 52);
        browseButton.ForeColor = ForeColor;
        browseButton.FlatStyle = FlatStyle.Flat;
        browseButton.FlatAppearance.BorderColor = inputBorder;
        browseButton.Click += (_, _) => BrowseForActionTarget();

        clearHotkeyButton.Text = UiText.Get("Deck.Clear");
        clearHotkeyButton.AccessibleName = UiText.Get("Deck.ClearKeys");
        clearHotkeyButton.BackColor = isLight ? Color.FromArgb(236, 238, 242) : Color.FromArgb(39, 43, 52);
        clearHotkeyButton.ForeColor = ForeColor;
        clearHotkeyButton.FlatStyle = FlatStyle.Flat;
        clearHotkeyButton.FlatAppearance.BorderColor = inputBorder;
        clearHotkeyButton.Click += (_, _) => ClearHotkey();

        previewBadge.BackColor = isLight ? Color.FromArgb(255, 255, 255) : Color.FromArgb(30, 34, 42);
        previewBadge.BorderStyle = BorderStyle.FixedSingle;
        previewBadge.Dock = DockStyle.Fill;
        previewBadgeLabel.Dock = DockStyle.Fill;
        previewBadgeLabel.TextAlign = ContentAlignment.MiddleCenter;
        previewBadgeLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        previewBadgeLabel.ForeColor = isLight ? Color.FromArgb(40, 100, 180) : Color.FromArgb(82, 153, 224);
        previewBadge.Paint += (_, e) => {
            var mode = NormalizeDisplayModeChoice(displayModeCombo.SelectedItem?.ToString());
            if (mode != ShortcutDisplayModes.Label) ShortcutIconCatalog.Draw(e.Graphics, iconText.Text, loadedIconData, new Rectangle(mode == ShortcutDisplayModes.Icon ? (previewBadge.Width - 24) / 2 : 8, (previewBadge.Height - 24) / 2, 24, 24), ForeColor);
            if (mode != ShortcutDisplayModes.Icon) TextRenderer.DrawText(e.Graphics, nameText.Text.Length == 0 ? UiText.Get("Deck.Shortcut") : nameText.Text, Font, new Rectangle(mode == ShortcutDisplayModes.Both ? 40 : 4, 0, previewBadge.Width - (mode == ShortcutDisplayModes.Both ? 44 : 8), previewBadge.Height), ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };

        testActionButton.Text = UiText.Get("Deck.TestAction");
        testActionButton.AccessibleName = UiText.Get("Deck.TestAccessible");
        testActionButton.BackColor = isLight ? Color.FromArgb(236, 238, 242) : Color.FromArgb(39, 43, 52);
        testActionButton.ForeColor = isLight ? Color.FromArgb(29, 32, 37) : Color.White;
        testActionButton.FlatStyle = FlatStyle.Flat;
        testActionButton.FlatAppearance.BorderColor = inputBorder;
        testActionButton.Click += (_, _) => TestCurrentAction();

        Controls.Add(BuildLayout());

        if (shortcut is null)
        {
            idText.Text = CreateNewShortcutId();
            nameText.Text = UiText.Get("Deck.NewAction");
            iconText.Text = "shortcut";
            displayModeCombo.SelectedItem = DisplayLabelChoice;
            typeCombo.SelectedItem = LaunchApplicationChoice;
        }
        else
        {
            LoadShortcut(shortcut);
        }

        UpdateActionPanel();
        UpdatePreview();
    }

    public ShortcutDefinition Shortcut { get; private set; } = new();

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            var candidate = BuildShortcut();
            if (!TryValidateShortcutDraft(candidate, out var error))
            {
                MessageBox.Show(this, error, UiText.Get("Deck.Validation"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                DialogResult = DialogResult.None;
                return;
            }

            Shortcut = candidate;
        }

        base.OnFormClosing(e);
    }

    private static string LocalizedChoice(string? value) => value switch
    {
        LaunchApplicationChoice => UiText.Get("Deck.Launch"),
        OpenUrlChoice => UiText.Get("Deck.Url"),
        SendHotkeyChoice => UiText.Get("Deck.Hotkey"),
        RunScriptChoice => UiText.Get("Deck.Script"),
        BuiltInActionChoice => UiText.Get("Deck.BuiltIn"),
        DisplayLabelChoice => UiText.Get("Deck.LabelOnly"),
        DisplayIconChoice => UiText.Get("Deck.Icon"),
        DisplayBothChoice => UiText.Get("Deck.Both"),
        ToggleMicrophoneChoice => UiText.Get("Deck.ToggleMic"),
        _ => value ?? string.Empty
    };

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9, Padding = new Padding(24, 22, 24, 18), BackColor = BackColor };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, FieldLabelWidth));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(root, 0, "ID", idText);
        AddField(root, 1, UiText.Get("Deck.Name"), nameText);

        var iconPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        iconPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        iconPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        iconText.Dock = DockStyle.Fill;
        iconText.Margin = new Padding(0, 4, 4, 4);
        chooseIconButton.Dock = DockStyle.Fill;
        chooseIconButton.Margin = new Padding(4, 4, 0, 4);
        iconPanel.Controls.Add(iconText, 0, 0);
        iconPanel.Controls.Add(chooseIconButton, 1, 0);
        AddField(root, 2, UiText.Get("Deck.Icon"), iconPanel);

        var displayPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        displayPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        displayPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        displayModeCombo.Dock = DockStyle.Fill;
        displayModeCombo.Margin = new Padding(0, 4, 4, 4);
        previewBadge.Dock = DockStyle.Fill;
        previewBadge.Margin = new Padding(4, 4, 0, 4);
        displayPanel.Controls.Add(displayModeCombo, 0, 0);
        displayPanel.Controls.Add(previewBadge, 1, 0);
        AddField(root, 3, UiText.Get("Deck.Display"), displayPanel);

        AddField(root, 4, UiText.Get("Deck.ActionLabel"), typeCombo);
        actionPanel.Dock = DockStyle.Fill;
        actionPanel.ColumnCount = 2;
        actionPanel.BackColor = BackColor;
        actionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, FieldLabelWidth));
        actionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.Controls.Add(actionPanel, 0, 5);
        root.SetColumnSpan(actionPanel, 2);
        hintLabel.Dock = DockStyle.Fill;
        hintLabel.ForeColor = Color.FromArgb(177, 184, 195);
        hintLabel.TextAlign = ContentAlignment.MiddleLeft;
        hintLabel.AccessibleName = UiText.Get("Deck.ActionHelp");
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.Controls.Add(hintLabel, 0, 6);
        root.SetColumnSpan(hintLabel, 2);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Height = 52, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(CreateDialogButton(UiText.Get("Deck.Save"), DialogResult.OK, isDefault: true));
        buttons.Controls.Add(CreateDialogButton(UiText.Get("Deck.Cancel"), DialogResult.Cancel, isDefault: false));
        testActionButton.Width = PrimaryButtonWidth + 8;
        testActionButton.Height = PrimaryButtonHeight;
        testActionButton.Margin = new Padding(8, 0, 0, 0);
        testActionButton.BackColor = Color.FromArgb(39, 43, 52);
        testActionButton.ForeColor = Color.White;
        testActionButton.FlatStyle = FlatStyle.Flat;
        buttons.Controls.Add(testActionButton);
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(buttons, 0, 8);
        root.SetColumnSpan(buttons, 2);
        AcceptButton = (IButtonControl)buttons.Controls[0];
        CancelButton = (IButtonControl)buttons.Controls[1];
        return root;
    }

    private static void AddField(TableLayoutPanel root, int row, string label, Control control)
    {
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        root.Controls.Add(CreateFieldLabel(label), 0, row);
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 4, 0, 4);
        root.Controls.Add(control, 1, row);
    }

    private static Label CreateFieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = SettingsWindow.IsClientLightTheme ? Color.FromArgb(93, 101, 112) : Color.FromArgb(177, 184, 195)
    };

    private static Button CreateDialogButton(string text, DialogResult result, bool isDefault) => new()
    {
        Text = text,
        DialogResult = result,
        Width = PrimaryButtonWidth,
        Height = PrimaryButtonHeight,
        Margin = new Padding(8, 0, 0, 0),
        BackColor = isDefault ? SettingsWindow.CurrentAccent : (SettingsWindow.IsClientLightTheme ? Color.FromArgb(236, 238, 242) : Color.FromArgb(39, 43, 52)),
        ForeColor = isDefault ? Color.White : (SettingsWindow.IsClientLightTheme ? Color.FromArgb(29, 32, 37) : Color.White),
        FlatStyle = FlatStyle.Flat,
        AccessibleName = text
    };

    private void LoadShortcut(ShortcutDefinition shortcut)
    {
        loadedActionType = shortcut.Action.Type;
        loadedTileColor = shortcut.Color;
        idText.Text = shortcut.Id;
        nameText.Text = shortcut.Name;
        loadedIconData = shortcut.IconData; loadedIconUrl = shortcut.IconUrl;
        iconText.Text = shortcut.Icon;
        displayModeCombo.SelectedItem = ToDisplayModeChoice(shortcut.DisplayMode);
        typeCombo.SelectedItem = ToActionChoice(shortcut.Action.Type);
        targetText.Text = shortcut.Action.Type switch
        {
            ShortcutActionTypes.LaunchApplication => shortcut.Action.ExecutablePath,
            ShortcutActionTypes.OpenUrl => shortcut.Action.Url,
            ShortcutActionTypes.Hotkey => string.Join("+", shortcut.Action.Keys),
            ShortcutActionTypes.Script or ShortcutActionTypes.PowerShell => shortcut.Action.ScriptPath,
            _ => string.Empty
        } ?? string.Empty;
        argumentsText.Text = shortcut.Action.Type is ShortcutActionTypes.LaunchApplication or ShortcutActionTypes.Script or ShortcutActionTypes.PowerShell ? JoinArguments(shortcut.Action.Arguments) : string.Empty;
        if (shortcut.Action.Type == ShortcutActionTypes.PredefinedAdapter)
        {
            builtInActionCombo.SelectedItem = ToggleMicrophoneChoice;
        }
    }

    private ShortcutDefinition BuildShortcut() => new()
    {
        Color = loadedTileColor,
        Id = idText.Text.Trim(),
        Name = nameText.Text.Trim(),
        Icon = iconText.Text.Trim(),
        IconData = loadedIconData,
        IconUrl = loadedIconUrl,
        DisplayMode = NormalizeDisplayModeChoice(displayModeCombo.SelectedItem?.ToString()),
        Action = CreateActionForInputs(GetActionChoiceForSave(), targetText.Text, argumentsText.Text, builtInActionCombo.SelectedItem?.ToString())
    };

    private string GetActionChoiceForSave()
    {
        var choice = typeCombo.SelectedItem?.ToString() ?? OpenUrlChoice;
        return string.Equals(loadedActionType, ShortcutActionTypes.PowerShell, StringComparison.Ordinal)
            && string.Equals(choice, RunScriptChoice, StringComparison.Ordinal)
                ? ShortcutActionTypes.PowerShell
                : choice;
    }

    private void UpdateActionPanel()
    {
        var choice = typeCombo.SelectedItem?.ToString() ?? OpenUrlChoice;
        actionPanel.SuspendLayout();
        actionPanel.Controls.Clear();
        actionPanel.RowStyles.Clear();
        actionPanel.RowCount = 0;
        switch (NormalizeActionChoice(choice))
        {
            case ShortcutActionTypes.LaunchApplication:
                targetText.PlaceholderText = UiText.Get("Deck.PathPlaceholder");
                AddPathActionRow(UiText.Get("Deck.Executable"));
                AddTextActionRow(UiText.Get("Deck.Arguments"), argumentsText);
                break;
            case ShortcutActionTypes.OpenUrl:
                targetText.PlaceholderText = "e.g. https://example.com";
                AddTextActionRow("URL", targetText);
                argumentsText.Text = string.Empty;
                break;
            case ShortcutActionTypes.Hotkey:
                targetText.PlaceholderText = UiText.Get("Deck.KeysPlaceholder");
                AddHotkeyActionRow(UiText.Get("Deck.Keys"));
                argumentsText.Text = string.Empty;
                break;
            case ShortcutActionTypes.Script:
            case ShortcutActionTypes.PowerShell:
                targetText.PlaceholderText = UiText.Get("Deck.ScriptPlaceholder");
                AddPathActionRow(UiText.Get("Deck.ScriptLabel"));
                AddTextActionRow(UiText.Get("Deck.Arguments"), argumentsText);
                break;
            case ShortcutActionTypes.PredefinedAdapter:
                AddTextActionRow(UiText.Get("Deck.BuiltInLabel"), builtInActionCombo);
                targetText.Text = string.Empty;
                argumentsText.Text = string.Empty;
                builtInActionCombo.SelectedItem ??= ToggleMicrophoneChoice;
                break;
        }

        hintLabel.Text = GetActionFieldHints(choice).HelpText;
        actionPanel.ResumeLayout();
    }

    private void UpdatePreview()
    {
        previewBadge.Invalidate();
    }

    internal void ClearHotkey()
    {
        targetText.Text = string.Empty;
        UpdatePreview();
    }

    private async void TestCurrentAction()
    {
        var candidate = BuildShortcut();
        if (!TryValidateShortcutDraft(candidate, out var error))
        {
            MessageBox.Show(this, UiText.Format("Deck.TestInvalid", error), UiText.Get("Deck.TestAction"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(this, UiText.Get("Deck.TestConfirm"), UiText.Get("Deck.TestAction"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try
        {
            var catalog = ShortcutCatalog.Create(new ShortcutConfiguration { Shortcuts = [candidate] });
            var executor = new ShortcutExecutor(catalog, new WindowsProcessApplicationLauncher(), new WindowsShellUrlOpener(),
                new Runtime.WindowsHotkeySender(), new WindowsProcessScriptRunner(), new Runtime.WindowsPredefinedShortcutAdapter(() => {
                    using var endpoint = StreamDeck.Audio.CoreAudioEndpoint.CreateDefaultCaptureEndpoint();
                    endpoint.SetMute(!endpoint.GetMute());
                    return true;
                }));
            await executor.ExecuteAsync(new ShortcutExecuteRequest(candidate.Id));
            MessageBox.Show(this, UiText.Get("Deck.TestExecuted"), UiText.Get("Deck.TestAction"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, UiText.Format("Deck.TestError", ex.Message), UiText.Get("Deck.TestErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AddPathActionRow(string label)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        targetText.Dock = DockStyle.Fill;
        targetText.Margin = new Padding(0, 4, 6, 4);
        panel.Controls.Add(targetText, 0, 0);
        browseButton.Dock = DockStyle.Fill;
        browseButton.Margin = new Padding(0, 4, 0, 4);
        panel.Controls.Add(browseButton, 1, 0);
        AddActionControlRow(label, panel);
    }

    private void AddHotkeyActionRow(string label)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        targetText.Dock = DockStyle.Fill;
        targetText.Margin = new Padding(0, 4, 6, 4);
        panel.Controls.Add(targetText, 0, 0);
        clearHotkeyButton.Dock = DockStyle.Fill;
        clearHotkeyButton.Margin = new Padding(0, 4, 0, 4);
        panel.Controls.Add(clearHotkeyButton, 1, 0);
        AddActionControlRow(label, panel);
    }

    private void AddTextActionRow(string label, Control control)
    {
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 4, 0, 4);
        AddActionControlRow(label, control);
    }

    private void AddActionControlRow(string label, Control control)
    {
        var row = actionPanel.RowCount++;
        actionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        actionPanel.Controls.Add(CreateFieldLabel(label), 0, row);
        actionPanel.Controls.Add(control, 1, row);
    }

    internal static Size GetMinimumDialogSizeForTests() => new(DialogWidth, DialogHeight);

    internal static int GetFieldLabelWidthForTests() => FieldLabelWidth;

    internal static Size GetPrimaryButtonSizeForTests() => new(PrimaryButtonWidth, PrimaryButtonHeight);

    private void ApplySelectedActionChoice()
    {
        var choice = typeCombo.SelectedItem?.ToString();
        if (!string.Equals(choice, RunScriptChoice, StringComparison.Ordinal))
        {
            loadedActionType = null;
        }
        if (string.Equals(choice, LaunchApplicationChoice, StringComparison.Ordinal))
        {
            targetText.Text = string.IsNullOrWhiteSpace(targetText.Text) || Uri.TryCreate(targetText.Text, UriKind.Absolute, out _) ? string.Empty : targetText.Text;
            argumentsText.Text = string.Empty;
        }
        else if (string.Equals(choice, OpenUrlChoice, StringComparison.Ordinal) || string.Equals(choice, SendHotkeyChoice, StringComparison.Ordinal))
        {
            argumentsText.Text = string.Empty;
        }
        else if (string.Equals(choice, BuiltInActionChoice, StringComparison.Ordinal))
        {
            builtInActionCombo.SelectedItem ??= ToggleMicrophoneChoice;
            targetText.Text = string.Empty;
            argumentsText.Text = string.Empty;
        }
        UpdateActionPanel();
    }

    internal static string NormalizeActionChoice(string? choice) => choice switch
    {
        LaunchApplicationChoice => ShortcutActionTypes.LaunchApplication,
        OpenUrlChoice => ShortcutActionTypes.OpenUrl,
        SendHotkeyChoice => ShortcutActionTypes.Hotkey,
        RunScriptChoice => ShortcutActionTypes.Script,
        BuiltInActionChoice => ShortcutActionTypes.PredefinedAdapter,
        _ => choice ?? ShortcutActionTypes.OpenUrl
    };

    internal static IReadOnlyList<string> GetActionChoices() => [LaunchApplicationChoice, OpenUrlChoice, SendHotkeyChoice, RunScriptChoice, BuiltInActionChoice];
    internal static IReadOnlyList<string> GetBuiltInActionChoices() => [ToggleMicrophoneChoice];
    internal static IReadOnlyList<string> GetDisplayModeChoices() => [DisplayLabelChoice, DisplayIconChoice, DisplayBothChoice];
    internal static string CreateNewShortcutId() => $"shortcut-{Guid.NewGuid():N}";

    internal static string NormalizeDisplayModeChoice(string? choice) => choice switch
    {
        DisplayIconChoice => ShortcutDisplayModes.Icon,
        DisplayBothChoice => ShortcutDisplayModes.Both,
        DisplayLabelChoice => ShortcutDisplayModes.Label,
        ShortcutDisplayModes.Icon => ShortcutDisplayModes.Icon,
        ShortcutDisplayModes.Both => ShortcutDisplayModes.Both,
        _ => ShortcutDisplayModes.Label
    };

    internal static ActionFieldHints GetActionFieldHints(string? actionType) => NormalizeActionChoice(actionType) switch
    {
        ShortcutActionTypes.LaunchApplication => new(UiText.Get("Deck.Executable"), UiText.Get("Deck.Arguments"), true, true, UiText.Get("Deck.LaunchHelp")),
        ShortcutActionTypes.OpenUrl => new("URL", string.Empty, true, false, UiText.Get("Deck.UrlHelp")),
        ShortcutActionTypes.Hotkey => new(UiText.Get("Deck.Keys"), string.Empty, true, false, UiText.Get("Deck.HotkeyHelp")),
        ShortcutActionTypes.Script => new(UiText.Get("Deck.ScriptLabel"), UiText.Get("Deck.Arguments"), true, true, UiText.Get("Deck.ScriptHelp")),
        ShortcutActionTypes.PowerShell => new(UiText.Get("Deck.ScriptLabel"), UiText.Get("Deck.Arguments"), true, true, UiText.Get("Deck.PowerShellHelp")),
        ShortcutActionTypes.PredefinedAdapter => new(UiText.Get("Deck.BuiltInLabel"), string.Empty, true, false, UiText.Get("Deck.BuiltInHelp")),
        _ => new(UiText.Get("Deck.TargetLabel"), string.Empty, true, false, UiText.Get("Deck.ChooseType"))
    };

    internal static ShortcutActionDefinition CreateActionForInputs(string choice, string target, string arguments, string? builtInChoice = null)
    {
        var type = NormalizeActionChoice(choice);
        var trimmedTarget = target.Trim();
        var parsedArguments = SplitArguments(arguments);
        return type switch
        {
            ShortcutActionTypes.LaunchApplication => new ShortcutActionDefinition { Type = type, ExecutablePath = trimmedTarget, Arguments = parsedArguments },
            ShortcutActionTypes.OpenUrl => new ShortcutActionDefinition { Type = type, Url = trimmedTarget, Arguments = [] },
            ShortcutActionTypes.Hotkey => new ShortcutActionDefinition { Type = type, Keys = SplitHotkey(trimmedTarget), Arguments = [] },
            ShortcutActionTypes.Script => new ShortcutActionDefinition { Type = type, ScriptPath = trimmedTarget, Arguments = parsedArguments },
            ShortcutActionTypes.PowerShell => new ShortcutActionDefinition { Type = type, ScriptPath = trimmedTarget, Arguments = parsedArguments },
            ShortcutActionTypes.PredefinedAdapter => new ShortcutActionDefinition { Type = type, AdapterKey = ToggleMicrophoneAdapterKey, Arguments = [] },
            _ => new ShortcutActionDefinition { Type = ShortcutActionTypes.OpenUrl, Url = trimmedTarget }
        };
    }

    internal static bool TryValidateShortcutDraft(ShortcutDefinition shortcut, out string error)
    {
        var issues = new ShortcutConfigurationValidator().Validate(new ShortcutConfiguration { Shortcuts = [shortcut] });
        if (issues.Count == 0)
        {
            error = string.Empty;
            return true;
        }

        error = string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Path}: {issue.Message}"));
        return false;
    }

    internal sealed record ActionFieldHints(string TargetLabel, string ArgumentsLabel, bool TargetEnabled, bool ArgumentsEnabled, string HelpText);

    private static string ToActionChoice(string? actionType) => actionType switch
    {
        ShortcutActionTypes.LaunchApplication => LaunchApplicationChoice,
        ShortcutActionTypes.OpenUrl => OpenUrlChoice,
        ShortcutActionTypes.Hotkey => SendHotkeyChoice,
        ShortcutActionTypes.Script => RunScriptChoice,
        ShortcutActionTypes.PowerShell => RunScriptChoice,
        ShortcutActionTypes.PredefinedAdapter => BuiltInActionChoice,
        _ => OpenUrlChoice
    };

    private static string ToDisplayModeChoice(string? displayMode) => displayMode switch
    {
        ShortcutDisplayModes.Icon => DisplayIconChoice,
        ShortcutDisplayModes.Both => DisplayBothChoice,
        _ => DisplayLabelChoice
    };

    private void BrowseForActionTarget()
    {
        var type = NormalizeActionChoice(typeCombo.SelectedItem?.ToString());
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Title = type switch
            {
                ShortcutActionTypes.LaunchApplication => UiText.Get("Deck.ChooseExe"),
                _ => UiText.Get("Deck.ChooseScript2")
            },
            Filter = type switch
            {
                ShortcutActionTypes.LaunchApplication => UiText.Get("Deck.AppFilter"),
                _ => UiText.Get("Deck.ScriptFilter")
            }
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            targetText.Text = dialog.FileName;
        }
    }

    internal static string JoinArguments(IReadOnlyList<string> values)
    {
        static string Quote(string value)
        {
            if (value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c == '"')) return value;
            var result = new System.Text.StringBuilder("\"");
            var slashes = 0;
            foreach (var c in value)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                slashes = 0; result.Append(c);
            }
            result.Append('\\', slashes * 2); result.Append('"');
            return result.ToString();
        }
        return string.Join(" ", values.Select(Quote));
    }

    internal static IReadOnlyList<string> SplitArguments(string value)
    {
        var arguments = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        var backslashes = 0;
        var started = false;

        foreach (var character in value)
        {
            if (!char.IsWhiteSpace(character) || inQuotes) started = true;
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                current.Append('\\', backslashes / 2);
                if (backslashes % 2 == 0)
                {
                    inQuotes = !inQuotes;
                }
                else
                {
                    current.Append('"');
                }

                backslashes = 0;
                continue;
            }

            if (backslashes > 0)
            {
                current.Append('\\', backslashes);
                backslashes = 0;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (started) { arguments.Add(current.ToString()); current.Clear(); started = false; }
                continue;
            }

            current.Append(character);
        }

        if (backslashes > 0)
        {
            current.Append('\\', backslashes);
        }

        if (started) arguments.Add(current.ToString());
        return arguments;
    }

    private static void AddCurrentArgument(List<string> arguments, System.Text.StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        arguments.Add(current.ToString());
        current.Clear();
    }

    private static IReadOnlyList<string> SplitHotkey(string value) => value.Split(['+', ',', ' '], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(key => key.ToUpperInvariant()).ToArray();
}
