using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using StreamDeck.Configuration;
using StreamDeck.Host;
using StreamDeck.Shortcuts;
using StreamDeck.WindowsAgent;
using StreamDeck.WindowsAgent.Runtime;
using StreamDeck.WindowsAgent.Shell;
using StreamDeck.WindowsAgent.Tray;
using Shortcut = StreamDeck.Shortcuts.ShortcutDefinition;

internal static class DesktopBugRegressionTests
{
    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint threadId, WindowCallback callback, IntPtr parameter);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    internal static void Run()
    {
        var errors = new List<string>();
        void Attempt(string name, Action action) { try { action(); Console.WriteLine("PASS " + name); } catch (Exception error) { errors.Add(name + ": " + error.GetType().Name + " " + error.Message); Console.WriteLine("FAIL " + errors[^1]); } }
        Attempt("Named color, Default and profile error path", TestColors);
        Exception? failure = null; var thread = new Thread(() => { try { TestMenu(); } catch (Exception error) { failure = error; } }); thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        Attempt("Device & Tools repeated open/close", () => { if (failure is not null) throw failure; });
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
    }
    private static void TestColors()
    {
        var path = Path.GetFullPath("artifacts/ui-review/color-regression.json"); var store = new AgentConfigurationStore(path);
        var shortcut = new Shortcut { Id = "color-test", Name = "Color Test", Icon = "app", DisplayMode = "both", Color = ColorTranslator.ToHtml(Color.Red), Action = new() { Type = "hotkey", Keys = ["CTRL", "M"] } };
        store.Save(AgentConfiguration.CreateDefault() with { Shortcuts = [shortcut], Profiles = [new("Default", [shortcut])] });
        foreach (var pair in new[] { ("#80FF0000", "#FF0000"), ("#f00", "#FF0000"), (" #ff0000 ", "#FF0000"), ("blue", "#0000FF"), (" default ", (string?)null), ("", (string?)null) }) if (ConfigurationColorNormalizer.Normalize(pair.Item1) != pair.Item2) throw new InvalidOperationException("Legacy color normalization failed");
        var loaded = store.Load(); if (loaded.Shortcuts![0].Color != "#FF0000" || loaded.Profiles[0].Shortcuts[0].Color != "#FF0000") throw new InvalidOperationException("Named colors are not normalized");
        store.Save(loaded with { Shortcuts = [shortcut with { Color = null }] });
        var issues = new AgentConfigurationValidator().Validate(loaded with { Profiles = [new("Default", [shortcut with { Color = "invalid" }])] });
        if (!issues.Any(i => i.Path == "$.profiles.Default.shortcuts[0].color")) throw new InvalidOperationException("Profile error path contains an extra $");
    }
    private static void TestMenu()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Directory.CreateDirectory("artifacts/ui-review"); var store = new AgentConfigurationStore(Path.GetFullPath("artifacts/ui-review/menu-regression.json")); store.Save(AgentConfiguration.CreateDefault());
        var state = new AgentState(); var pairing = new PairingApprovalService(Path.GetFullPath("artifacts/ui-review/menu-pairing.json")); var runtime = new WindowsAgentRuntime(state, pairing, new AgentOptions(true), listenPortOverride: 0);
        using var icon = AppIconFactory.CreateIcon(); using var window = new SettingsWindow(state, runtime, new WindowsStartupService(new AgentOptions(true)), pairing, icon, store);
        window.StartPosition = FormStartPosition.Manual; window.Location = new Point(-2500, -2500); window.ShowInTaskbar = false; window.Show(); Application.DoEvents();
        var tools = All(window).OfType<Button>().Single(b => b.AccessibleName == "Device & Tools" || b.Text is "Device & Tools" or "Urządzenie i narzędzia");
        for (var i = 0; i < 8; ++i) {
            tools.PerformClick(); Application.DoEvents(); ContextMenuStrip? active = null;
            EnumThreadWindows(GetCurrentThreadId(), (handle, _) => { if (Control.FromHandle(handle) is ContextMenuStrip menu && menu.Visible) active = menu; return true; }, IntPtr.Zero);
            if (active is null) throw new InvalidOperationException("Tools menu did not open");
            active.Close(); Application.DoEvents();
            All(window).OfType<Button>().Single(b => b.Text is "Settings" or "Ustawienia" || b.AccessibleName is "Settings" or "Ustawienia").PerformClick(); Application.DoEvents();
            All(window).OfType<RadioButton>().Single(b => b.Visible && b.Text == (i % 2 == 0 ? "Light" : "Dark")).PerformClick();
            if (!window.SaveConfigurationDraftAsync().GetAwaiter().GetResult()) throw new InvalidOperationException("Theme save failed during menu regression");
            All(window).OfType<Button>().Single(b => b.Text is "Deck Builder" or "Edytor Deck" || b.AccessibleName is "Deck Builder" or "Edytor Deck").PerformClick(); Application.DoEvents();
        }
        window.CloseForExit(); SynchronizationContext.SetSynchronizationContext(null); runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
    private static IEnumerable<Control> All(Control root) { foreach (Control child in root.Controls) { yield return child; foreach (var item in All(child)) yield return item; } }
}
