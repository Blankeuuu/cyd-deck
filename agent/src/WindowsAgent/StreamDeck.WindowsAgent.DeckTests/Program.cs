using System.Reflection;
using StreamDeck.Configuration;
using StreamDeck.Shortcuts;
using StreamDeck.WindowsAgent.Shell;

// No SettingsWindow/runtime creation, real configuration store, registry, serial or action execution.
internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        assertions++;
    }

    [STAThread]
    private static void Main()
    {
        var a = new ShortcutDefinition { Id = "action-a", Name = "Browser", Icon = "browser", Action = new() { Type = "url", Url = "https://example.com" } };
        var b = a with { Id = "action-b", Name = "Other" };
        var config = new AgentConfiguration { Shortcuts = [a, b], Profiles = [new("Default", [a, b], new string?[6]), new("Work", [b], [b.Id])] };
        var draft = new DeckEditSession(config);
        var created = a with { Id = "created", Name = "Created" };
        draft.Shortcuts.Add(created);
        draft.SelectLibrary(created.Id);
        Check(draft.SelectedSlot == 0 && draft.SelectedActionId == created.Id, "Library selection must not select another tile");
        draft.Assign(created.Id, 2);
        Check(draft.SelectedSlot == 2 && draft.Slots[2] == created.Id, "Assign slot 3");
        var index = draft.Shortcuts.FindIndex(s => s.Id == created.Id);
        draft.Shortcuts[index] = created with { Name = "Edited", Color = "#1D4ED8", Icon = "audio" };
        draft.SelectSlot(4); draft.SelectSlot(2);
        Check(draft.SelectedActionId == created.Id && draft.Shortcuts[index].Name == "Edited" && draft.Shortcuts[index].Icon == "audio" && draft.Shortcuts[index].Color == "#1D4ED8", "Move selection and return preserves edits");
        draft.SelectLibrary(b.Id);
        Check(draft.SelectedSlot == 2 && draft.SelectedActionId == b.Id && draft.SelectedLibraryActionId == b.Id, "Independent selection");
        draft.SelectSlot(2);
        Check(draft.SelectedActionId == created.Id && draft.SelectedLibraryActionId == b.Id, "Tile inspector follows clicked slot");
        var saved = AgentConfigurationJson.Deserialize(AgentConfigurationJson.Serialize(draft.ApplyTo(config)));
        Check(saved.Shortcuts!.Single(s => s.Id == created.Id).Name == "Edited" && saved.Profiles[0].AssignedShortcutIds![2] == created.Id, "Save fixture only");
        Check(config.Shortcuts!.Count == 2 && config.Profiles[0].AssignedShortcutIds![2] is null, "Source configuration unchanged");
        draft.Detach(2);
        Check(draft.Shortcuts.Any(s => s.Id == created.Id) && draft.Slots[2] is null, "Detach retains definition");
        draft.UndoAssignment();
        Check(draft.Slots[2] == created.Id, "Undo detach");
        draft.Assign(b.Id, 2); draft.UndoAssignment();
        Check(draft.Slots[2] == created.Id, "Undo replace");
        draft.Assign(b.Id, 0); draft.Swap(0, 2);
        Check(draft.Slots[0] == created.Id && draft.Slots[2] == b.Id && draft.SelectedActionId == b.Id, "Swap follows destination");
        var beforeCancel = draft.ApplyTo(config);
        // Cancel means no profile transition; shell policy is tested by the Lead.
        Check(draft.ApplyTo(beforeCancel).ActiveProfile == "Default" && draft.Shortcuts[index].Name == "Edited", "Cancel retains current draft");
        try { draft.ApplyTo(beforeCancel with { ActiveProfile = "Work" }); throw new Exception("Expected stale editor rejection"); }
        catch (InvalidOperationException) { assertions++; }
        var historical = new AgentConfiguration { Shortcuts = [a, b] };
        var migrated = new DeckEditSession(historical);
        Check(migrated.Slots.SequenceEqual(new string?[] { a.Id, b.Id, null, null, null, null }) && historical.Profiles.Count == 0, "Old format migrates only in memory");
        var explicitEmpty = new DeckEditSession(config with { Profiles = [new("Default", [a, b], [])] });
        Check(explicitEmpty.Slots.All(s => s is null), "Explicit empty assignments stay empty");
        var duplicate = new DeckEditSession(config with { Profiles = [new("Default", [a, b], [a.Id, a.Id, b.Id])] });
        duplicate.SelectSlot(1);
        Check(duplicate.SelectedSlot == 1 && duplicate.SelectedActionId == a.Id, "Duplicate IDs do not redirect clicked slot");
        duplicate.Assign(a.Id, 4);
        Check(duplicate.Slots.Count(id => id == a.Id) == 1 && duplicate.Slots[4] == a.Id, "Reassignment removes duplicate assignment");
        duplicate.DeleteSelected(); duplicate.UndoAssignment();
        Check(!duplicate.Shortcuts.Any(s => s.Id == a.Id) && !duplicate.Slots.Contains(a.Id), "Delete never resurrects dangling assignment");
        try { duplicate.SelectSlot(6); throw new Exception("Expected bounds rejection"); }
        catch (ArgumentOutOfRangeException) { assertions++; }
        Check(draft.ApplyTo(config).Profiles.Single(p => p.Name == "Work").Shortcuts[0] == b, "Other profile unchanged");

        var assembly = typeof(SettingsWindow).Assembly;
        var editor = assembly.GetType("StreamDeck.WindowsAgent.Shell.ShortcutEditorDialog", true)!;
        object Call(string method, params object?[] args) => editor.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;
        string[] argsFixture = ["--profile", "two words", "", "C:\\trailing path\\", "quoted\"value"];
        var joined = (string)Call("JoinArguments", argsFixture as object);
        Check(((IReadOnlyList<string>)Call("SplitArguments", joined)).SequenceEqual(argsFixture), "Arguments round-trip quotes, empty values and trailing slashes");
        foreach (var entry in new[] { ("launch", "notepad.exe"), ("url", "https://example.com"), ("hotkey", "Ctrl+Shift+R"), ("script", "C:\\Scripts\\safe.ps1"), ("predefined", "audio:toggle-mic") })
        {
            var action = (ShortcutActionDefinition)Call("CreateActionForInputs", entry.Item1, entry.Item2, "", null);
            Check(new ShortcutConfigurationValidator().Validate(new() { Shortcuts = [a with { Action = action }] }).Count == 0, "Valid action: " + entry.Item1);
            var fake = new FakeActions();
            var executor = new ShortcutExecutor(ShortcutCatalog.Create(new() { Shortcuts = [a with { Action = action }] }), fake, fake, fake, fake, fake);
            var result = executor.ExecuteAsync(new(a.Id)).GetAwaiter().GetResult();
            Check(result.Status == ShortcutExecutionStatus.Executed && fake.Type == entry.Item1, "Routes action to isolated runner: " + entry.Item1);
        }
        var text = assembly.GetType("StreamDeck.WindowsAgent.Shell.UiText", true)!;
        string Get(string key) => (string)text.GetMethod("Get")!.Invoke(null, [key])!;
        foreach (var lang in new[] { "pl", "en" })
        {
            text.GetMethod("SetLanguage")!.Invoke(null, [lang]);
            Check(!Get("Deck.Library").StartsWith("Deck."), "Localized Deck resources: " + lang);
            using var dialog = (Form)Activator.CreateInstance(editor, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [null], null)!;
            Check(dialog.Font.Size >= 11 && dialog.AutoScaleMode == AutoScaleMode.Dpi, "Dialog typography and DPI: " + lang);
            dialog.ShowInTaskbar = false;
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(-20000, -20000);
            dialog.Show();
            Application.DoEvents();
            using var bitmap = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, $"editor-{lang}.png"));
            dialog.Hide();
        }
        var canvasType = assembly.GetType("StreamDeck.WindowsAgent.Shell.DesktopCanvas", true)!;
        using var canvas = (Control)Activator.CreateInstance(canvasType, true)!;
        canvas.Size = new Size(640, 480);
        canvasType.GetField("ShortcutsLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(canvas, "grid-6");
        canvasType.GetField("Slots", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(canvas, new ShortcutDefinition?[] { a, b, a, null, null, null });
        var clicked = -1;
        canvasType.GetField("SelectSlot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(canvas, (Action<int>)(slot => clicked = slot));
        using var previewImage = new Bitmap(canvas.Width, canvas.Height);
        canvas.DrawToBitmap(previewImage, new Rectangle(Point.Empty, previewImage.Size));
        Check(canvas.AccessibilityObject.GetChildCount() == 6, "Six accessible slots");
        canvas.AccessibilityObject.GetChild(2)!.DoDefaultAction();
        Check(clicked == 2, "Accessible duplicate selects exact slot 3");
        Check(canvas.AccessibilityObject.GetChild(2)!.Bounds.Width > 0, "Accessible slot has screen bounds");
        canvasType.GetField("Page", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(canvas, "Home");
        Check(canvas.AccessibilityObject.GetChildCount() == 4, "Home has four accessible slots");
        Console.WriteLine($"DeckTests PASS: {assertions} assertions; no live storage or system actions.");
    }

    private sealed class FakeActions : IApplicationLauncher, IUrlOpener, IHotkeySender, IScriptRunner, IPredefinedShortcutAdapter
    {
        internal string? Type;
        public Task LaunchAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken) { Type = "launch"; return Task.CompletedTask; }
        public Task OpenAsync(Uri url, CancellationToken cancellationToken) { Type = "url"; return Task.CompletedTask; }
        public Task SendAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken) { Type = "hotkey"; return Task.CompletedTask; }
        public Task RunScriptAsync(string scriptPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken) { Type = "script"; return Task.CompletedTask; }
        public Task RunPowerShellAsync(string scriptPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken) { Type = "powershell"; return Task.CompletedTask; }
        public Task ExecuteAsync(string adapterKey, CancellationToken cancellationToken) { Type = "predefined"; return Task.CompletedTask; }
    }
}
