using StreamDeck.Shortcuts;

namespace StreamDeck.WindowsAgent.Shell;

internal sealed class ShortcutEditorDialog : Form
{
    private const int DialogWidth = 620;
    private const int DialogHeight = 540;
    private const int FieldLabelWidth = 126;
    private const int FieldRowHeight = 46;
    private const int PrimaryButtonWidth = 104;
    private const int PrimaryButtonHeight = 38;
    internal const string ObsToggleRecordingChoice = "OBS: toggle recording (Ctrl+Shift+R hotkey)";
    internal const string LaunchApplicationChoice = "Launch application";
    internal const string OpenUrlChoice = "Open URL";
    internal const string SendHotkeyChoice = "Send hotkey";
    internal const string RunScriptChoice = "Run script";
    internal const string RunPowerShellChoice = "Run PowerShell script";
    internal const string BuiltInActionChoice = "Built-in action";
    internal const string ToggleMicrophoneChoice = "Toggle microphone";
    internal const string ToggleMicrophoneAdapterKey = "audio:toggle-mic";

    private readonly TextBox idText = new();
    private readonly TextBox nameText = new();
    private readonly TextBox iconText = new();
    private readonly ComboBox typeCombo = new();
    private readonly TextBox targetText = new();
    private readonly TextBox argumentsText = new();
    private readonly ComboBox builtInActionCombo = new();
    private readonly Label hintLabel = new();
    private readonly Button browseButton = new();
    private readonly TableLayoutPanel actionPanel = new();

    public ShortcutEditorDialog(ShortcutDefinition? shortcut = null)
    {
        Text = shortcut is null ? "Add shortcut" : "Edit shortcut";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Width = DialogWidth;
        Height = DialogHeight;
        MinimumSize = new Size(DialogWidth, DialogHeight);
        BackColor = Color.FromArgb(18, 20, 25);
        ForeColor = Color.FromArgb(238, 241, 245);
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        AccessibleName = Text;
        AccessibleDescription = "Configure an Agent-side shortcut action. Firmware receives only shortcut IDs and labels.";

        typeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        typeCombo.AccessibleName = "Shortcut action";
        typeCombo.Items.AddRange(GetActionChoices().Cast<object>().ToArray());
        typeCombo.SelectedIndexChanged += (_, _) => ApplySelectedActionChoice();
        builtInActionCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        builtInActionCombo.AccessibleName = "Built-in action";
        builtInActionCombo.Items.AddRange(GetBuiltInActionChoices().Cast<object>().ToArray());
        idText.AccessibleName = "Shortcut ID";
        nameText.AccessibleName = "Shortcut name";
        iconText.AccessibleName = "Shortcut icon";
        targetText.AccessibleName = "Shortcut target";
        argumentsText.AccessibleName = "Shortcut arguments";
        browseButton.Text = "Browse…";
        browseButton.AccessibleName = "Browse for shortcut target";
        browseButton.Click += (_, _) => BrowseForActionTarget();
        Controls.Add(BuildLayout());

        if (shortcut is null)
        {
            idText.Text = CreateNewShortcutId();
            nameText.Text = "New Shortcut";
            iconText.Text = "shortcut";
            typeCombo.SelectedItem = LaunchApplicationChoice;
        }
        else
        {
            LoadShortcut(shortcut);
        }

        UpdateActionPanel();
    }

    public ShortcutDefinition Shortcut { get; private set; } = new();

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            var candidate = BuildShortcut();
            if (!TryValidateShortcutDraft(candidate, out var error))
            {
                MessageBox.Show(this, error, "Shortcut validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                DialogResult = DialogResult.None;
                return;
            }

            Shortcut = candidate;
        }

        base.OnFormClosing(e);
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, Padding = new Padding(24, 22, 24, 18), BackColor = BackColor };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, FieldLabelWidth));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(root, 0, "ID", idText);
        AddField(root, 1, "Name", nameText);
        AddField(root, 2, "Icon", iconText);
        AddField(root, 3, "Action", typeCombo);
        actionPanel.Dock = DockStyle.Fill;
        actionPanel.ColumnCount = 2;
        actionPanel.BackColor = BackColor;
        actionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, FieldLabelWidth));
        actionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.Controls.Add(actionPanel, 0, 4);
        root.SetColumnSpan(actionPanel, 2);
        hintLabel.Dock = DockStyle.Fill;
        hintLabel.ForeColor = Color.FromArgb(177, 184, 195);
        hintLabel.TextAlign = ContentAlignment.MiddleLeft;
        hintLabel.AccessibleName = "Shortcut action help";
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.Controls.Add(hintLabel, 0, 5);
        root.SetColumnSpan(hintLabel, 2);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Height = 52, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(CreateDialogButton("Save", DialogResult.OK, isDefault: true));
        buttons.Controls.Add(CreateDialogButton("Cancel", DialogResult.Cancel, isDefault: false));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(buttons, 0, 7);
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

    private static Label CreateFieldLabel(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(177, 184, 195) };

    private static Button CreateDialogButton(string text, DialogResult result, bool isDefault) => new()
    {
        Text = text,
        DialogResult = result,
        Width = PrimaryButtonWidth,
        Height = PrimaryButtonHeight,
        Margin = new Padding(8, 0, 0, 0),
        BackColor = isDefault ? Color.FromArgb(212, 59, 72) : Color.FromArgb(39, 43, 52),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        AccessibleName = text
    };

    private void LoadShortcut(ShortcutDefinition shortcut)
    {
        idText.Text = shortcut.Id;
        nameText.Text = shortcut.Name;
        iconText.Text = shortcut.Icon;
        typeCombo.SelectedItem = ToActionChoice(shortcut.Action.Type);
        targetText.Text = shortcut.Action.Type switch
        {
            ShortcutActionTypes.LaunchApplication => shortcut.Action.ExecutablePath,
            ShortcutActionTypes.OpenUrl => shortcut.Action.Url,
            ShortcutActionTypes.Hotkey => string.Join("+", shortcut.Action.Keys),
            ShortcutActionTypes.Script or ShortcutActionTypes.PowerShell => shortcut.Action.ScriptPath,
            _ => string.Empty
        } ?? string.Empty;
        argumentsText.Text = shortcut.Action.Type is ShortcutActionTypes.LaunchApplication or ShortcutActionTypes.Script or ShortcutActionTypes.PowerShell ? string.Join(" ", shortcut.Action.Arguments) : string.Empty;
        if (shortcut.Action.Type == ShortcutActionTypes.PredefinedAdapter)
        {
            builtInActionCombo.SelectedItem = ToggleMicrophoneChoice;
        }
    }

    private ShortcutDefinition BuildShortcut() => new()
    {
        Id = idText.Text.Trim(),
        Name = nameText.Text.Trim(),
        Icon = iconText.Text.Trim(),
        Action = CreateActionForInputs(typeCombo.SelectedItem?.ToString() ?? OpenUrlChoice, targetText.Text, argumentsText.Text, builtInActionCombo.SelectedItem?.ToString())
    };

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
                AddPathActionRow("Executable");
                AddTextActionRow("Arguments", argumentsText);
                break;
            case ShortcutActionTypes.OpenUrl:
                AddTextActionRow("URL", targetText);
                argumentsText.Text = string.Empty;
                break;
            case ShortcutActionTypes.Hotkey:
                AddTextActionRow("Keys", targetText);
                argumentsText.Text = string.Empty;
                break;
            case ShortcutActionTypes.Script:
            case ShortcutActionTypes.PowerShell:
                AddPathActionRow(NormalizeActionChoice(choice) == ShortcutActionTypes.PowerShell ? "PowerShell" : "Script");
                AddTextActionRow("Arguments", argumentsText);
                break;
            case ShortcutActionTypes.PredefinedAdapter:
                AddTextActionRow("Built-in", builtInActionCombo);
                targetText.Text = string.Empty;
                argumentsText.Text = string.Empty;
                builtInActionCombo.SelectedItem ??= ToggleMicrophoneChoice;
                break;
        }

        hintLabel.Text = GetActionFieldHints(choice).HelpText;
        actionPanel.ResumeLayout();
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
        else if (string.Equals(choice, ObsToggleRecordingChoice, StringComparison.Ordinal))
        {
            targetText.Text = "Ctrl+Shift+R";
            argumentsText.Text = string.Empty;
            nameText.Text = nameText.Text == "New Shortcut" ? "OBS Toggle Recording" : nameText.Text;
            iconText.Text = iconText.Text == "shortcut" ? "record" : iconText.Text;
        }

        UpdateActionPanel();
    }

    internal static string NormalizeActionChoice(string? choice) => choice switch
    {
        LaunchApplicationChoice => ShortcutActionTypes.LaunchApplication,
        OpenUrlChoice => ShortcutActionTypes.OpenUrl,
        SendHotkeyChoice => ShortcutActionTypes.Hotkey,
        RunScriptChoice => ShortcutActionTypes.Script,
        RunPowerShellChoice => ShortcutActionTypes.PowerShell,
        BuiltInActionChoice => ShortcutActionTypes.PredefinedAdapter,
        ObsToggleRecordingChoice => ShortcutActionTypes.Hotkey,
        _ => choice ?? ShortcutActionTypes.OpenUrl
    };

    internal static IReadOnlyList<string> GetActionChoices() => [LaunchApplicationChoice, OpenUrlChoice, SendHotkeyChoice, RunScriptChoice, RunPowerShellChoice, BuiltInActionChoice, ObsToggleRecordingChoice];
    internal static IReadOnlyList<string> GetBuiltInActionChoices() => [ToggleMicrophoneChoice];
    internal static string CreateNewShortcutId() => $"shortcut-{Guid.NewGuid():N}";

    internal static ActionFieldHints GetActionFieldHints(string? actionType) => NormalizeActionChoice(actionType) switch
    {
        ShortcutActionTypes.LaunchApplication => new("Executable", "Arguments", true, true, "Agent-side shortcut. Choose a full executable path or enter a bare .exe name; firmware does not preload shortcuts and displays only the first 6 labels/IDs."),
        ShortcutActionTypes.OpenUrl => new("URL", string.Empty, true, false, "URL action uses only an absolute http/https URL; arguments are not used."),
        ShortcutActionTypes.Hotkey => new("Keys", string.Empty, true, false, "Hotkey action uses one keys field, e.g. Ctrl+Shift+R."),
        ShortcutActionTypes.Script => new("Script", "Arguments", true, true, "Full local script path required; optional arguments are passed by the Agent."),
        ShortcutActionTypes.PowerShell => new("PowerShell", "Arguments", true, true, "Full local .ps1 path required; optional arguments are passed by the Agent."),
        ShortcutActionTypes.PredefinedAdapter => new("Built-in", string.Empty, true, false, "Choose a known Agent built-in action. The device still receives only shortcut labels/IDs."),
        _ => new("Target", string.Empty, true, false, "Choose an action type.")
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
        ShortcutActionTypes.PowerShell => RunPowerShellChoice,
        ShortcutActionTypes.PredefinedAdapter => BuiltInActionChoice,
        _ => OpenUrlChoice
    };

    private void BrowseForActionTarget()
    {
        var type = NormalizeActionChoice(typeCombo.SelectedItem?.ToString());
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Title = type switch
            {
                ShortcutActionTypes.LaunchApplication => "Choose executable",
                ShortcutActionTypes.PowerShell => "Choose PowerShell script",
                _ => "Choose script"
            },
            Filter = type switch
            {
                ShortcutActionTypes.LaunchApplication => "Programs (*.exe)|*.exe|All files (*.*)|*.*",
                ShortcutActionTypes.PowerShell => "PowerShell scripts (*.ps1)|*.ps1|All files (*.*)|*.*",
                _ => "Scripts (*.bat;*.cmd;*.ps1;*.exe)|*.bat;*.cmd;*.ps1;*.exe|All files (*.*)|*.*"
            }
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            targetText.Text = dialog.FileName;
        }
    }

    internal static IReadOnlyList<string> SplitArguments(string value)
    {
        var arguments = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        var backslashes = 0;

        foreach (var character in value)
        {
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
                AddCurrentArgument(arguments, current);
                continue;
            }

            current.Append(character);
        }

        if (backslashes > 0)
        {
            current.Append('\\', backslashes);
        }

        AddCurrentArgument(arguments, current);
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
