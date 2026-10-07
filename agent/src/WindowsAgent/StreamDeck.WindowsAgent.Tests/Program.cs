using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Drawing;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

using StreamDeck.Protocol;
using StreamDeck.Audio;
using StreamDeck.WindowsAgent.Runtime;
using StreamDeck.WindowsAgent;
using StreamDeck.Host;
using StreamDeck.WindowsAgent.Shell;
using StreamDeck.WindowsAgent.Tray;
using StreamDeck.Configuration;
using StreamDeck.Shortcuts;
using StreamDeck.WindowsAgent.Updates;
using ShortcutDefinition = StreamDeck.Shortcuts.ShortcutDefinition;

var state = new AgentState();
state.SetMode("mock");
state.SetHostListening(8765);
state.SetDashboardListening(5000);
state.MarkDeviceConnected("device-1", "CYD", "192.168.1.50");
var snapshot = state.Snapshot();
AssertEqual("connected", snapshot.HostStatus, "host status");
AssertEqual("CYD", snapshot.DeviceName, "device name");
AssertEqual(8765, snapshot.HostPort, "host port");
AssertEqual(5000, snapshot.DashboardPort, "dashboard port");
AssertEqual("mock", snapshot.Mode, "agent mode");
AssertEqual("Connected", AgentStatusFormatter.ToUserStatus(snapshot.HostStatus), "user status");
AssertEqual("Connected", AgentStatusFormatter.ToDeviceStatus(snapshot), "device status");
AssertEqual("not connected", AgentStatusFormatter.FormatLastSeen(snapshot), "last seen before diagnostics");

state.UpdateDeviceInfo(new DeviceInfoMessage(
    "test-firmware",
    12,
    new DeviceWifiInfo(true, "lab", -55, "192.168.1.51"),
    "192.168.1.10",
    new DeviceStorageInfo(true, true, 1024, 512),
    new DeviceHeapInfo(1000, 900, 800)));
snapshot = state.Snapshot();
AssertTrue(snapshot.DeviceInfoUpdatedAtUtc.HasValue, "device info timestamp populated");
AssertContains("seconds ago", AgentStatusFormatter.FormatLastSeen(snapshot), "last seen recency");

state.MarkDeviceDisconnected("device-1");
snapshot = state.Snapshot();
AssertEqual("listening", snapshot.HostStatus, "host status after disconnect");
AssertEqual("device-1", snapshot.DeviceId, "device id preserved after disconnect");
AssertEqual("CYD", snapshot.DeviceName, "device name preserved after disconnect");
AssertEqual(null, snapshot.RemoteAddress, "remote address after disconnect");
AssertEqual("test-firmware", snapshot.DeviceInfo?.FirmwareVersion, "device info preserved after disconnect");
AssertTrue(snapshot.DeviceInfoUpdatedAtUtc.HasValue, "device info timestamp preserved after disconnect");
AssertContains("seconds ago", AgentStatusFormatter.FormatLastSeen(snapshot), "last seen preserved after disconnect");
AssertEqual("Connecting", AgentStatusFormatter.ToUserStatus(snapshot.HostStatus), "listening user status");
AssertEqual("Disconnected", AgentStatusFormatter.ToDeviceStatus(snapshot), "listening device status");

var startupRaceState = new AgentState();
startupRaceState.MarkDeviceConnected("fast-device", "Fast CYD", "192.168.1.70");
startupRaceState.SetHostListening(9876);
var startupRaceSnapshot = startupRaceState.Snapshot();
AssertEqual("connected", startupRaceSnapshot.HostStatus, "listener setup preserves fast device connection");
AssertEqual(9876, startupRaceSnapshot.HostPort, "listener setup still records host port after fast device connection");
AssertEqual("fast-device", startupRaceSnapshot.DeviceId, "listener setup preserves fast device id");

var options = AgentOptions.FromArgs(["--mock"]);
AssertTrue(options.MockMode, "mock option");

var exported = ConfigurationExportService.Export(new AgentConfiguration
{
    Server = new ServerConfiguration { Port = 9123 },
    Shortcuts =
    [
        new ShortcutDefinition
        {
            Id = "docs",
            Name = "Docs",
            Icon = "browser",
            Action = new ShortcutActionDefinition
            {
                Type = ShortcutActionTypes.OpenUrl,
                Url = "https://example.com/docs"
            }
        }
    ]
});
AssertContains("\"formatVersion\": 1", exported, "export format version");
AssertDoesNotContain("pairing", exported, "export pairing secret");
AssertDoesNotContain("wifi", exported, "export wifi secret");
var imported = ConfigurationExportService.Import(exported);
AssertEqual(9123, imported.Server!.Port, "imported server port");
AssertEqual("docs", imported.Shortcuts![0].Id, "imported shortcut id");
var shortcutActionChoices = ShortcutEditorDialog.GetActionChoices();
var generatedShortcutId1 = ShortcutEditorDialog.CreateNewShortcutId();
var generatedShortcutId2 = ShortcutEditorDialog.CreateNewShortcutId();
AssertTrue(generatedShortcutId1.StartsWith("shortcut-", StringComparison.Ordinal), "new shortcut IDs use a generated prefix");
AssertTrue(!string.Equals(generatedShortcutId1, generatedShortcutId2, StringComparison.Ordinal), "new shortcut dialogs receive distinct IDs");
AssertEqual(1, shortcutActionChoices.Count(choice => ShortcutEditorDialog.NormalizeActionChoice(choice) == ShortcutActionTypes.LaunchApplication), "shortcut editor exposes one launch action choice");
AssertTrue(!shortcutActionChoices.Contains(ShortcutActionTypes.LaunchApplication), "shortcut editor hides raw launch action type from display choices");
AssertEqual(ShortcutActionTypes.LaunchApplication, ShortcutEditorDialog.NormalizeActionChoice(ShortcutEditorDialog.LaunchApplicationChoice), "shortcut editor launch choice maps to supported launch action");
AssertEqual(ShortcutActionTypes.Hotkey, ShortcutEditorDialog.NormalizeActionChoice(ShortcutEditorDialog.ObsToggleRecordingChoice), "shortcut editor OBS preset maps to supported hotkey action");
var launchHints = ShortcutEditorDialog.GetActionFieldHints(ShortcutEditorDialog.LaunchApplicationChoice);
AssertEqual("Executable", launchHints.TargetLabel, "launch editor labels executable path field");
AssertTrue(launchHints.ArgumentsEnabled, "launch editor has action-specific arguments field");
AssertContains("Agent-side", launchHints.HelpText, "shortcut editor explains Agent-side catalog");
AssertContains("first 6", launchHints.HelpText, "shortcut editor explains firmware visible shortcut limit");
var urlAction = ShortcutEditorDialog.CreateActionForInputs(ShortcutEditorDialog.OpenUrlChoice, "https://example.com", "--ignored");
AssertEqual(ShortcutActionTypes.OpenUrl, urlAction.Type, "URL panel creates URL action");
AssertEqual("https://example.com", urlAction.Url, "URL panel stores URL target");
AssertEqual(0, urlAction.Arguments.Count, "URL panel does not persist arguments");
var hotkeyAction = ShortcutEditorDialog.CreateActionForInputs(ShortcutEditorDialog.SendHotkeyChoice, "Ctrl+Shift+R", "duplicated");
AssertEqual(ShortcutActionTypes.Hotkey, hotkeyAction.Type, "hotkey panel creates hotkey action");
AssertEqual("CTRL+SHIFT+R", string.Join('+', hotkeyAction.Keys), "hotkey panel uses one keys field");
AssertEqual(0, hotkeyAction.Arguments.Count, "hotkey panel does not duplicate keys in arguments");
var predefinedAction = ShortcutEditorDialog.CreateActionForInputs(ShortcutEditorDialog.BuiltInActionChoice, "freeform", string.Empty, ShortcutEditorDialog.ToggleMicrophoneChoice);
AssertEqual(ShortcutEditorDialog.ToggleMicrophoneAdapterKey, predefinedAction.AdapterKey, "built-in panel maps known choice to adapter key");
AssertContains("Ctrl+Shift+R", ShortcutEditorDialog.ObsToggleRecordingChoice, "OBS preset clearly labels shortcut keys");
var quotedLaunchAction = ShortcutEditorDialog.CreateActionForInputs(ShortcutEditorDialog.LaunchApplicationChoice, "notepad.exe", "--name \"Two Words\" --flag");
AssertEqual(3, quotedLaunchAction.Arguments.Count, "quote-aware argument parser preserves argument count");
AssertEqual("--name", quotedLaunchAction.Arguments[0], "quote-aware argument parser keeps leading option");
AssertEqual("Two Words", quotedLaunchAction.Arguments[1], "quote-aware argument parser preserves quoted multi-word argument");
AssertEqual("--flag", quotedLaunchAction.Arguments[2], "quote-aware argument parser keeps trailing option");
var quotedPathAction = ShortcutEditorDialog.CreateActionForInputs(ShortcutEditorDialog.LaunchApplicationChoice, "notepad.exe", "--config \"C:\\Users\\Alice\\config.json\" C:\\Temp\\");
AssertEqual(3, quotedPathAction.Arguments.Count, "Windows argument parser preserves quoted path argument count");
AssertEqual("--config", quotedPathAction.Arguments[0], "Windows argument parser keeps path option");
AssertEqual("C:\\Users\\Alice\\config.json", quotedPathAction.Arguments[1], "Windows argument parser preserves backslashes in quoted paths");
AssertEqual("C:\\Temp\\", quotedPathAction.Arguments[2], "Windows argument parser preserves trailing backslash");
var escapedQuoteAction = ShortcutEditorDialog.CreateActionForInputs(ShortcutEditorDialog.LaunchApplicationChoice, "notepad.exe", "--title \"Alice said \\\"Hi\\\"\"");
AssertEqual(2, escapedQuoteAction.Arguments.Count, "Windows argument parser preserves escaped quote argument count");
AssertEqual("--title", escapedQuoteAction.Arguments[0], "Windows argument parser keeps escaped quote option");
AssertEqual("Alice said \"Hi\"", escapedQuoteAction.Arguments[1], "Windows argument parser preserves escaped quotes inside quoted values");
AssertTrue(ShortcutEditorDialog.TryValidateShortcutDraft(new ShortcutDefinition
{
    Id = "launch-notepad",
    Name = "Launch Notepad",
    Icon = "app",
    Action = new ShortcutActionDefinition
    {
        Type = ShortcutActionTypes.LaunchApplication,
        ExecutablePath = "notepad.exe"
    }
}, out var bareLaunchError), "shortcut editor accepts bare executable names before save");
AssertEqual(string.Empty, bareLaunchError, "bare executable validation does not report an error");
AssertContains("bare .exe name", launchHints.HelpText, "launch editor explains bare executable policy");
AssertTrue(!ShortcutEditorDialog.TryValidateShortcutDraft(new ShortcutDefinition
{
    Id = "bad-url",
    Name = "Bad URL",
    Action = new ShortcutActionDefinition
    {
        Type = ShortcutActionTypes.OpenUrl,
        Url = "not a url"
    }
}, out var badUrlError), "shortcut editor validates URL before save");
AssertContains("absolute http or https URLs", badUrlError, "URL validation explains absolute URL requirement");
var shortcutLoadFailure = SettingsWindow.LoadShortcutConfigurationState(() => throw new InvalidDataException("corrupt config"));
AssertTrue(!shortcutLoadFailure.CanWriteShortcuts, "shortcut page blocks writes after config load failure");
AssertTrue(!SettingsWindow.CanWriteShortcutsAfterLoad(shortcutLoadFailure), "shortcut save is disabled after config load failure");
AssertContains("InvalidDataException", shortcutLoadFailure.ErrorType, "shortcut load failure records error type");
AssertEqual(0, shortcutLoadFailure.Configuration.Shortcuts?.Count ?? 0, "failed shortcut load shows empty fallback without treating it as writable empty catalog");
var emptyShortcutLoad = SettingsWindow.LoadShortcutConfigurationState(() => AgentConfiguration.CreateDefault());
AssertTrue(emptyShortcutLoad.CanWriteShortcuts, "genuinely empty default config remains writable");
AssertTrue(SettingsWindow.CanWriteShortcutsAfterLoad(emptyShortcutLoad), "shortcut save remains enabled after successful empty load");
AssertContains("Agent-side", SettingsWindow.ShortcutsSourceOfTruthText, "shortcuts page explains Agent source of truth");
AssertContains("first 6", SettingsWindow.ShortcutsSourceOfTruthText, "shortcuts page explains device visibility limit");
AssertContains("No shortcuts configured on this PC", SettingsWindow.CreateShortcutsEmptyStateText(emptyShortcutLoad), "empty shortcut catalog has explicit empty state");
AssertContains("Click Add", SettingsWindow.CreateShortcutsEmptyStateText(emptyShortcutLoad), "empty shortcut catalog names next action");
AssertContains("no device preload", SettingsWindow.CreateShortcutsEmptyStateText(emptyShortcutLoad), "empty shortcut catalog distinguishes unavailable device preload data");
AssertContains("failed to load", SettingsWindow.CreateShortcutsEmptyStateText(shortcutLoadFailure), "load failure empty state is distinct from genuinely empty catalog");
AssertContains("Import full config", SettingsWindow.CreateShortcutsEmptyStateText(shortcutLoadFailure), "load failure empty state names recovery action");
AssertContains("inactive", SettingsWindow.PendingShortcutImportsText, "pending device shortcut imports are labeled inactive");
AssertContains("not executable or synced", SettingsWindow.PendingShortcutImportsText, "pending device shortcut imports are not executable or synced");
var pendingShortcutStore = new PendingShortcutImportStore();
pendingShortcutStore.UpdateFromDeviceReport(
    new ShortcutsReportMessage([
        new StreamDeck.Protocol.ShortcutDefinition("saved", "Saved", ShortcutActionTypes.LaunchApplication),
        new StreamDeck.Protocol.ShortcutDefinition("device-only", "Device only", ShortcutActionTypes.OpenUrl),
        new StreamDeck.Protocol.ShortcutDefinition("device-only", "Duplicate", ShortcutActionTypes.Hotkey)
    ]),
    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "saved" });
var pendingImports = pendingShortcutStore.Snapshot(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "saved" });
AssertEqual(1, pendingImports.Count, "pending import store de-dupes saved and repeated device shortcut IDs");
AssertEqual("device-only", pendingImports[0].Id, "pending import store preserves device-only shortcut ID as inactive draft metadata");
var filteredPendingImports = SettingsWindow.FilterPendingShortcutImports(
    [new PendingShortcutImport("saved", "Saved", ShortcutActionTypes.LaunchApplication), new PendingShortcutImport("device-only", "Device only", ShortcutActionTypes.OpenUrl)],
    [new ShortcutDefinition { Id = "saved", Name = "Saved" }]);
AssertEqual(1, filteredPendingImports.Count, "settings pending import list de-dupes against saved shortcut IDs");
AssertEqual("device-only", filteredPendingImports[0].Id, "settings pending import list keeps unsaved device-only metadata");
var pendingDraft = SettingsWindow.CreateShortcutDraftFromPendingImport(new PendingShortcutImport("device-url", "Device URL", ShortcutActionTypes.OpenUrl));
AssertEqual("device-url", pendingDraft.Id, "pending import draft preserves device shortcut ID");
AssertEqual("Device URL", pendingDraft.Name, "pending import draft pre-fills device label");
AssertEqual(ShortcutActionTypes.OpenUrl, pendingDraft.Action.Type, "pending import draft pre-fills reported action type");
AssertTrue(!ShortcutEditorDialog.TryValidateShortcutDraft(pendingDraft, out var pendingDraftError), "pending import draft is not valid/executable until action details are configured");
AssertContains("URL", pendingDraftError, "pending import draft validation asks for missing action details");
var configuredPendingDraft = pendingDraft with
{
    Icon = "app",
    Action = ShortcutEditorDialog.CreateActionForInputs(ShortcutEditorDialog.OpenUrlChoice, "https://example.com", string.Empty)
};
AssertTrue(ShortcutEditorDialog.TryValidateShortcutDraft(configuredPendingDraft, out var configuredPendingError), "pending import draft becomes valid after configuring action details");
AssertEqual(string.Empty, configuredPendingError, "configured pending import draft has no validation error");
var unknownPendingDraft = SettingsWindow.CreateShortcutDraftFromPendingImport(new PendingShortcutImport("device-unknown", "Unknown", "vendor-local"));
AssertEqual(ShortcutActionTypes.LaunchApplication, unknownPendingDraft.Action.Type, "unknown pending action types fall back to configurable launch action");
var emptyImportSignature = SettingsWindow.CreatePendingShortcutImportSignature([]);
AssertTrue(SettingsWindow.ShouldRefreshShortcutImports(emptyImportSignature, [new PendingShortcutImport("device-url", "Device URL", ShortcutActionTypes.OpenUrl)]), "open Shortcuts page refreshes when a device report adds a pending import");
var existingImportSignature = SettingsWindow.CreatePendingShortcutImportSignature([new PendingShortcutImport("device-url", "Device URL", ShortcutActionTypes.OpenUrl)]);
AssertTrue(!SettingsWindow.ShouldRefreshShortcutImports(existingImportSignature, [new PendingShortcutImport("device-url", "Device URL", ShortcutActionTypes.OpenUrl)]), "open Shortcuts page does not rebuild when pending imports are unchanged");
AssertTrue(SettingsWindow.ShouldRefreshShortcutImports(existingImportSignature, []), "open Shortcuts page refreshes when pending import disappears after configuration/save");
AssertEqual((ushort)0x11, WindowsHotkeySender.ResolveVirtualKey("CTRL"), "hotkey control key mapping");
AssertEqual((ushort)0x10, WindowsHotkeySender.ResolveVirtualKey("Shift"), "hotkey shift key mapping");
AssertEqual((ushort)0x52, WindowsHotkeySender.ResolveVirtualKey("R"), "hotkey letter mapping");
AssertEqual(IntPtr.Size == 8 ? 40 : 28, WindowsHotkeySender.NativeInputSize, "SendInput native structure size");
AssertThrows<ArgumentException>(() => WindowsHotkeySender.ResolveVirtualKey("not-a-key"), "unknown hotkey key rejected");
var microphoneToggleCount = 0;
var predefinedShortcuts = new WindowsPredefinedShortcutAdapter(() =>
{
    microphoneToggleCount++;
    return true;
});
await predefinedShortcuts.ExecuteAsync("audio:toggle-mic", CancellationToken.None);
AssertEqual(1, microphoneToggleCount, "predefined microphone shortcut toggles capture endpoint");
var unsupportedPredefinedWasRejected = false;
try
{
    await predefinedShortcuts.ExecuteAsync("unknown:action", CancellationToken.None);
}
catch (NotSupportedException)
{
    unsupportedPredefinedWasRejected = true;
}
AssertTrue(unsupportedPredefinedWasRejected, "unknown predefined shortcut is not silently reported as successful");
AssertTrue(!SettingsWindow.ShouldRebuildPageOnStateChanged("Overview"), "overview state updates do not rebuild the page");
AssertTrue(!SettingsWindow.ShouldRebuildPageOnStateChanged("Device"), "device state updates do not rebuild the page");
AssertTrue(!SettingsWindow.ShouldRebuildPageOnStateChanged("Shortcuts"), "shortcut page uses pending-import polling instead of unrelated state rebuilds");
AssertTrue(SettingsWindow.ShouldRebuildPageOnStateChanged("Pairing"), "pairing updates refresh the pending list");
var pendingLiveRefresh = 0;
AssertTrue(SettingsWindow.TryQueueLiveRefresh(ref pendingLiveRefresh), "first live refresh event is queued");
AssertTrue(!SettingsWindow.TryQueueLiveRefresh(ref pendingLiveRefresh), "duplicate live refresh event is coalesced while queued");
SettingsWindow.MarkLiveRefreshCompleted(ref pendingLiveRefresh);
AssertTrue(SettingsWindow.TryQueueLiveRefresh(ref pendingLiveRefresh), "live refresh can be queued after completion");
var lastConnectedState = 0;
AssertTrue(WindowsAgentRuntime.TryMarkHostConnected(ref lastConnectedState, isConnected: true), "connected transition requests an initial media update");
AssertTrue(!WindowsAgentRuntime.TryMarkHostConnected(ref lastConnectedState, isConnected: true), "startup checkpoint coalesces a media update already triggered by the connection event");
AssertTrue(!WindowsAgentRuntime.TryMarkHostConnected(ref lastConnectedState, isConnected: false), "disconnect does not request a media update");
AssertTrue(WindowsAgentRuntime.TryMarkHostConnected(ref lastConnectedState, isConnected: true), "reconnect requests a fresh initial media update");

AssertEqual(Color.FromArgb(246, 247, 249).ToArgb(), SettingsWindow.MapClientThemeColor(Color.FromArgb(13, 15, 20), "light").ToArgb(), "light theme maps the client background");
AssertEqual(Color.FromArgb(13, 15, 20).ToArgb(), SettingsWindow.MapClientThemeColor(Color.FromArgb(246, 247, 249), "dark").ToArgb(), "dark theme restores the client background");
AssertEqual(Color.FromArgb(246, 247, 249).ToArgb(), SettingsWindow.GetClientThemeBackground("light").ToArgb(), "light theme initializes the content host before attach");
AssertTrue(SettingsWindow.GetNavigationWidth() >= 250, "settings navigation uses a wider hierarchy rail");
AssertTrue(SettingsWindow.GetNavigationButtonSize().Height >= 42, "settings navigation buttons have comfortable target height");
AssertTrue(SettingsWindow.GetClientContentPadding().Left >= 32, "settings content uses spacious page margins");
AssertEqual(new Size(900, 620), SettingsWindow.GetMinimumWindowSizeForTests(), "settings window keeps the validated 900x620 minimum size");
AssertTrue(SettingsWindow.ShouldPreserveClientThemeBackColor(SettingsWindow.DeviceColorPreviewTagForTests), "device color preview is not remapped by client theme");
var minimumClientWidth = 900 - SettingsWindow.GetNavigationWidth() - SettingsWindow.GetClientContentPadding().Horizontal;
var shortcutActionButtonWidths = new[] { "Add", "Edit", "Delete", "Move up", "Move down", "Import full config", "Export full config" }
    .Select(SettingsWindow.MeasureSettingsActionButtonWidth)
    .ToArray();
AssertTrue(SettingsWindow.CalculateActionBarHeight(minimumClientWidth, shortcutActionButtonWidths) > SettingsWindow.GetActionButtonHeight(), "shortcuts action bar grows when controls wrap at minimum width");
AssertTrue(SettingsWindow.CalculateActionBarHeight(minimumClientWidth, shortcutActionButtonWidths) <= 104, "wrapped shortcuts action bar remains accessible at minimum height");
AssertTrue(SettingsWindow.GetAppearanceSliderTrackWidth(minimumClientWidth) < 300, "appearance sliders shrink below old fixed width at minimum window size");
AssertTrue(SettingsWindow.GetAppearanceSliderValueWidth("86400 sec") >= System.Windows.Forms.TextRenderer.MeasureText("86400 sec", SystemFonts.MessageBoxFont).Width + 16, "appearance slider value column fits the longest timeout label");
AssertTrue(SettingsWindow.GetAppearanceFormLabelWidth(minimumClientWidth) <= 180, "appearance labels leave room for controls at minimum width");
var previousColorPickerContext = SynchronizationContext.Current;
using (var colorPicker = SettingsWindow.CreateColorPickerControl("#12abef"))
{
    AssertEqual(SettingsWindow.DeviceColorPickerTagForTests, colorPicker.Tag, "appearance color control is a structured picker row");
    var colorSwatch = colorPicker.Controls.OfType<System.Windows.Forms.Panel>().Single();
    var colorValue = colorPicker.Controls.OfType<System.Windows.Forms.Label>().Single();
    var chooseColor = colorPicker.Controls.OfType<System.Windows.Forms.Button>().Single();
    AssertEqual(SettingsWindow.DeviceColorPreviewTagForTests, colorSwatch.Tag, "appearance color row includes a dedicated current-color swatch");
    AssertEqual("#12ABEF", colorValue.Text, "appearance color row shows the current hex value separately from the action");
    AssertEqual("Choose…", chooseColor.Text, "appearance color row has an explicit choose action");
    AssertTrue(!string.Equals(chooseColor.Text, colorValue.Text, StringComparison.Ordinal), "appearance color action is not a full-row color value button");
}
SynchronizationContext.SetSynchronizationContext(previousColorPickerContext);
var screenshotClientWidth = 1018 - SettingsWindow.GetNavigationWidth() - SettingsWindow.GetClientContentPadding().Horizontal;
var minimumPairingColumns = SettingsWindow.CalculatePairingColumnWidths(minimumClientWidth);
var screenshotPairingColumns = SettingsWindow.CalculatePairingColumnWidths(screenshotClientWidth);
AssertTrue(SettingsWindow.ColumnWidthsFitAvailableWidth(minimumClientWidth, minimumPairingColumns), "pairing columns fit without a horizontal scrollbar at minimum width");
AssertTrue(SettingsWindow.ColumnWidthsFitAvailableWidth(screenshotClientWidth, screenshotPairingColumns), "pairing columns fit without a horizontal scrollbar at screenshot width");
AssertTrue(minimumPairingColumns[3] >= System.Windows.Forms.TextRenderer.MeasureText("First seen", SystemFonts.MessageBoxFont).Width + 24, "pairing first-seen header fits at minimum width");
var previousPairingListContext = SynchronizationContext.Current;
using (var pairingList = new System.Windows.Forms.ListView { View = System.Windows.Forms.View.Details })
{
    SettingsWindow.ConfigurePairingColumns(pairingList, minimumClientWidth);
    SettingsWindow.AttachPairingColumnResizeHandler(pairingList);
    var firstSeenHeader = pairingList.Columns[3];
    AssertEqual("First seen", firstSeenHeader.Text, "pairing first-seen header is created once before handle resize");
    _ = pairingList.Handle;
    pairingList.Size = new Size(minimumClientWidth + 24, 240);
    System.Windows.Forms.Application.DoEvents();
    AssertTrue(ReferenceEquals(firstSeenHeader, pairingList.Columns[3]), "pairing resize keeps the existing native column header object");
    AssertEqual("First seen", pairingList.Columns[3].Text, "pairing resize preserves first-seen header text without resetting ColumnInfo");
}
SynchronizationContext.SetSynchronizationContext(previousPairingListContext);
previousPairingListContext = SynchronizationContext.Current;
using (var pairingForm = new System.Windows.Forms.Form { ClientSize = new Size(minimumClientWidth, 260), ShowInTaskbar = false, StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new Point(-2000, -2000) })
using (var livePairingList = new System.Windows.Forms.ListView { View = System.Windows.Forms.View.Details, Dock = System.Windows.Forms.DockStyle.Fill })
{
    pairingForm.Controls.Add(livePairingList);
    SettingsWindow.ConfigurePairingColumns(livePairingList, minimumClientWidth);
    SettingsWindow.AttachPairingColumnResizeHandler(livePairingList);
    pairingForm.Show();
    System.Windows.Forms.Application.DoEvents();
    pairingForm.ClientSize = new Size(minimumClientWidth + 48, 260);
    System.Windows.Forms.Application.DoEvents();
    AssertEqual("First seen", livePairingList.Columns[3].Text, "pairing tab live resize handler preserves first-seen header through WM_WINDOWPOSCHANGED");
}
SynchronizationContext.SetSynchronizationContext(previousPairingListContext);
var minimumShortcutColumns = SettingsWindow.CalculateShortcutsColumnWidths(minimumClientWidth);
var screenshotShortcutColumns = SettingsWindow.CalculateShortcutsColumnWidths(screenshotClientWidth);
AssertTrue(SettingsWindow.ColumnWidthsFitAvailableWidth(minimumClientWidth, minimumShortcutColumns), "shortcut columns fit without a horizontal scrollbar at minimum width");
AssertTrue(SettingsWindow.ColumnWidthsFitAvailableWidth(screenshotClientWidth, screenshotShortcutColumns), "shortcut columns fit without a horizontal scrollbar at screenshot width");
AssertTrue(minimumShortcutColumns[3] >= 120, "shortcut target column keeps a usable ellipsis area at minimum width");
var minimumRenderedShortcutListWidth = 538;
var previousShortcutListContext = SynchronizationContext.Current;
using (var shortcutListForm = new System.Windows.Forms.Form { ClientSize = new Size(minimumRenderedShortcutListWidth, 260), ShowInTaskbar = false, StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new Point(-2000, -2000) })
using (var liveShortcutList = new System.Windows.Forms.ListView { View = System.Windows.Forms.View.Details, Dock = System.Windows.Forms.DockStyle.Fill })
{
    shortcutListForm.Controls.Add(liveShortcutList);
    liveShortcutList.Items.Add(new ListViewItem(["Documentation", "docs", ShortcutActionTypes.OpenUrl, "https://example.com/docs"]));
    liveShortcutList.Items.Add(new ListViewItem(["OBS record", "obs-record", ShortcutActionTypes.Hotkey, "CTRL+SHIFT+R"]));
    shortcutListForm.Show();
    System.Windows.Forms.Application.DoEvents();
    SettingsWindow.ConfigureShortcutsColumns(liveShortcutList, liveShortcutList.ClientSize.Width);
    System.Windows.Forms.Application.DoEvents();
    var renderedShortcutColumnWidth = liveShortcutList.Columns.Cast<ColumnHeader>().Sum(column => column.Width);
    AssertTrue(liveShortcutList.ClientSize.Width <= minimumRenderedShortcutListWidth, "shortcut list uses the rendered client width, not the outer bounds width");
    AssertTrue(renderedShortcutColumnWidth < liveShortcutList.ClientSize.Width, "shortcut columns leave native ListView breathing room at the rendered minimum width");
    AssertTrue(!SettingsWindow.ControlHasHorizontalScrollbarForTests(liveShortcutList), "shown shortcut list does not expose a horizontal scrollbar at the rendered 900x620 minimum width");
}
SynchronizationContext.SetSynchronizationContext(previousShortcutListContext);
var previousSettingsShortcutsContext = SynchronizationContext.Current;
var settingsPairingStore = Path.Combine(Path.GetTempPath(), $"streamdeck-settings-shortcuts-{Guid.NewGuid():N}.json");
using (var settingsIcon = AppIconFactory.CreateIcon())
await using (var settingsRuntime = new WindowsAgentRuntime(new AgentState(), new PairingApprovalService(settingsPairingStore), AgentOptions.FromArgs(["--mock"]), listenPortOverride: 0))
await using (var settingsDashboard = new LocalDashboardServer(new AgentState()))
using (var settingsWindow = new SettingsWindow(new AgentState(), settingsRuntime, settingsDashboard, new WindowsStartupService(AgentOptions.FromArgs(["--mock"])), new PairingApprovalService(settingsPairingStore), settingsIcon))
{
    settingsWindow.StartPosition = FormStartPosition.Manual;
    settingsWindow.Location = new Point(-2000, -2000);
    settingsWindow.ShowInTaskbar = false;
    settingsWindow.Size = SettingsWindow.GetMinimumWindowSizeForTests();
    settingsWindow.Show();
    System.Windows.Forms.Application.DoEvents();
    FlattenControls(settingsWindow).OfType<Button>().Single(button => button.Text == "Shortcuts").PerformClick();
    System.Windows.Forms.Application.DoEvents();
    settingsWindow.PerformLayout();
    System.Windows.Forms.Application.DoEvents();
    var shortcutsPageControls = FlattenControls(settingsWindow).Where(control => control.Visible).ToArray();
    var shortcutsBody = shortcutsPageControls.OfType<Panel>().Single(panel => Equals(panel.Tag, "settings-page-body"));
    var shortcutsFooter = shortcutsPageControls.OfType<FlowLayoutPanel>().Single(panel => Equals(panel.Tag, "settings-page-footer"));
    var shortcutsLists = shortcutsPageControls.OfType<ListView>().ToArray();
    var shortcutFooterButtons = shortcutsFooter.Controls.OfType<Button>().Where(button => button.Visible).ToArray();
    AssertTrue(shortcutsLists.Any(list => string.Equals(list.AccessibleName, "Agent-side shortcuts", StringComparison.Ordinal)), "settings shortcuts page shows the Agent-side shortcuts list at minimum size");
    AssertTrue(shortcutsBody.Bounds.Bottom <= shortcutsFooter.Bounds.Top, "shown settings shortcuts page body stays above the footer at 900x620");
    AssertTrue(shortcutsFooter.Bounds.Bottom <= shortcutsFooter.Parent!.ClientSize.Height, "shown settings shortcuts page footer stays inside the page at 900x620");
    foreach (var expectedButton in new[] { "Add", "Edit", "Delete", "Move up", "Move down", "Import full config", "Export full config" })
    {
        var button = shortcutFooterButtons.Single(candidate => candidate.Text == expectedButton);
        var bounds = BoundsInClient(settingsWindow, button);
        AssertTrue(button.Width >= 128 && button.Height >= SettingsWindow.GetActionButtonHeight(), $"settings shortcuts footer keeps {expectedButton} accessible at 900x620");
        AssertTrue(bounds.Width > 0 && bounds.Height > 0 && settingsWindow.ClientRectangle.Contains(bounds), $"settings shortcuts footer renders {expectedButton} inside the shown window at 900x620");
    }

    AssertTrue(!SettingsWindow.ControlHasHorizontalScrollbarForTests(shortcutsBody), "shown settings shortcuts page body does not expose a horizontal scrollbar at 900x620");
    AssertTrue(shortcutsLists.All(list => !SettingsWindow.ControlHasHorizontalScrollbarForTests(list)), "shown settings shortcuts page lists do not expose horizontal scrollbars at 900x620");
    settingsWindow.CloseForExit();
}
SynchronizationContext.SetSynchronizationContext(previousSettingsShortcutsContext);
var minimumClientHeight = 620 - SettingsWindow.GetClientContentPadding().Vertical;
AssertTrue(SettingsWindow.PageLayoutHasScrollableBodyAndAccessibleFooterForTests(minimumClientWidth, minimumClientHeight, contentHeight: 980, shortcutActionButtonWidths), "shortcuts page keeps wrapped footer accessible while tall body scrolls at minimum size");
AssertTrue(SettingsWindow.PageLayoutHasScrollableBodyAndAccessibleFooterForTests(minimumClientWidth, minimumClientHeight, contentHeight: 860, new[] { "Reconnect", "Restart", "Factory reset", "Forget device" }.Select(SettingsWindow.MeasureSettingsActionButtonWidth).ToArray()), "device page keeps safety actions accessible while tall body scrolls at minimum size");
AssertTrue(SettingsWindow.PageLayoutHasScrollableBodyAndAccessibleFooterForTests(minimumClientWidth, minimumClientHeight, contentHeight: 760, Array.Empty<int>()), "appearance page keeps tall settings body independently scrollable at minimum size");
AssertTrue(ShortcutEditorDialog.GetMinimumDialogSizeForTests().Width >= 620, "shortcut editor keeps enough width for labels and Browse action at minimum size");
AssertTrue(ShortcutEditorDialog.GetMinimumDialogSizeForTests().Height >= 540, "shortcut editor keeps action help and Save/Cancel visible at minimum size");
AssertTrue(ShortcutEditorDialog.GetFieldLabelWidthForTests() >= 126, "shortcut editor labels reserve stable readable width");
AssertTrue(ShortcutEditorDialog.GetPrimaryButtonSizeForTests().Height >= 38, "shortcut editor Save and Cancel controls have comfortable keyboard focus targets");
var previousShortcutEditorContext = SynchronizationContext.Current;
using (var shortcutEditor = new ShortcutEditorDialog())
{
    AssertEqual(new Size(620, 540), shortcutEditor.MinimumSize, "shortcut editor enforces its tested minimum size");
    AssertEqual("Add shortcut", shortcutEditor.AccessibleName, "shortcut editor exposes an accessible dialog name");
    AssertContains("Agent-side shortcut", shortcutEditor.AccessibleDescription ?? string.Empty, "shortcut editor describes Agent-side shortcut semantics for assistive tech");
    var editorControls = FlattenControls(shortcutEditor).ToArray();
    AssertTrue(editorControls.OfType<System.Windows.Forms.TextBox>().All(control => !string.IsNullOrWhiteSpace(control.AccessibleName)), "shortcut editor text inputs have accessible names");
    AssertTrue(editorControls.OfType<System.Windows.Forms.ComboBox>().All(control => !string.IsNullOrWhiteSpace(control.AccessibleName)), "shortcut editor combo inputs have accessible names");
    AssertTrue(editorControls.OfType<System.Windows.Forms.Button>().Any(button => button.Text == "Save" && button.DialogResult == DialogResult.OK), "shortcut editor keeps Save as the OK action");
    AssertTrue(editorControls.OfType<System.Windows.Forms.Button>().Any(button => button.Text == "Cancel" && button.DialogResult == DialogResult.Cancel), "shortcut editor keeps Cancel as the cancel action");
    AssertTrue(ReferenceEquals(shortcutEditor.AcceptButton, editorControls.OfType<System.Windows.Forms.Button>().First(button => button.Text == "Save")), "shortcut editor Enter key activates Save");
    AssertTrue(ReferenceEquals(shortcutEditor.CancelButton, editorControls.OfType<System.Windows.Forms.Button>().First(button => button.Text == "Cancel")), "shortcut editor Escape key activates Cancel");
    AssertShortcutEditorLayout(shortcutEditor, new Size(620, 540), "minimum");
    AssertShortcutEditorLayout(shortcutEditor, new Size(720, 600), "regular");
}
SynchronizationContext.SetSynchronizationContext(previousShortcutEditorContext);
var liveOverview = SettingsWindow.CreateLiveInfoValues("Overview", new AgentStatusSnapshot(
    "connected", "device-1", "CYD", "192.168.1.8:1234", 8765, 5000, "live", ["192.168.1.10"]));
AssertEqual("Connected", liveOverview["CYD"], "overview live device state");
AssertEqual("Connected", liveOverview["WebSocket"], "overview live websocket state");
AssertEqual("192.168.1.10", liveOverview["LAN IP"], "overview live lan address");
AssertThrows<AgentConfigurationException>(
    () => ConfigurationExportService.Import("""{"formatVersion":999,"configuration":{}}"""),
    "unsupported export version");

using (var icon = AppIconFactory.CreateIcon())
{
    AssertTrue(icon.Handle != IntPtr.Zero, "app icon has a native handle");
    using var clone = (Icon)icon.Clone();
    AssertTrue(clone.Handle != IntPtr.Zero, "app icon clone owns a native handle");
}

using (var iconAfterDispose = AppIconFactory.CreateIcon())
{
    AssertTrue(iconAfterDispose.Handle != IntPtr.Zero, "app icon factory returns a usable icon after previous disposal");
}

using (var primary = SingleInstanceGuard.Acquire($"PCDock.WindowsAgent.Tests.{Guid.NewGuid():N}"))
{
    AssertTrue(primary.IsPrimaryInstance, "primary single instance");
    using var secondary = SingleInstanceGuard.Acquire($"PCDock.WindowsAgent.Tests.{Guid.NewGuid():N}");
    AssertTrue(secondary.IsPrimaryInstance, "different single instance name remains primary");
}

var instanceName = $"PCDock.WindowsAgent.Tests.{Guid.NewGuid():N}";
using (var primary = SingleInstanceGuard.Acquire(instanceName))
{
    using var secondary = SingleInstanceGuard.Acquire(instanceName);
    AssertTrue(primary.IsPrimaryInstance, "primary same-name single instance");
    AssertTrue(!secondary.IsPrimaryInstance, "secondary same-name single instance");
    secondary.SignalPrimaryInstance();
    AssertTrue(primary.ShowWindowWaitHandle.WaitOne(TimeSpan.FromSeconds(1)), "secondary signals primary window");
}

var pairingStore = Path.Combine(Path.GetTempPath(), $"streamdeck-pairing-{Guid.NewGuid():N}.json");
var dashboardPairing = new PairingApprovalService(pairingStore);
await dashboardPairing.AuthorizeAsync(new HelloMessage("native-device", "Native CYD", "esp32", []), bearerToken: null);
await using (var dashboard = new LocalDashboardServer(state))
{
    await dashboard.StartAsync();
    AssertTrue(dashboard.Port > 0, "dashboard port assigned");

    using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{dashboard.Port}/") };
    var dashboardHtml = await client.GetStringAsync("");
    AssertDoesNotContain("Pending pairing", dashboardHtml, "dashboard pairing UI removed");
    AssertDoesNotContain("/api/pairing", dashboardHtml, "dashboard pairing script removed");
    AssertContains("CYD WebSocket", dashboardHtml, "dashboard websocket UI");

    using var statusResponse = await client.GetAsync("api/status");
    AssertTrue(statusResponse.IsSuccessStatusCode, "dashboard status endpoint");

    using var pendingResponse = await client.GetAsync("api/pairing/pending");
    AssertEqual(HttpStatusCode.NotFound, pendingResponse.StatusCode, "dashboard pairing endpoint removed");

    var settingsConstructor = typeof(SettingsWindow).GetConstructor([
        typeof(AgentState),
        typeof(WindowsAgentRuntime),
        typeof(LocalDashboardServer),
        typeof(WindowsStartupService),
        typeof(IPairingApprovalService),
        typeof(Icon)]);
    AssertTrue(settingsConstructor is not null, "settings window is wired with native pairing service");
}

var listenerPairingStore = Path.Combine(Path.GetTempPath(), $"streamdeck-listener-pairing-{Guid.NewGuid():N}.json");
var listenerPairing = new PairingApprovalService(listenerPairingStore);
await using (var listener = new WindowsAgentWebSocketServer(
    new TransportOptions
    {
        BindAddress = IPAddress.Any,
        Port = 0,
        WebSocketPath = "/ws",
        AllowRemoteClients = true
    },
    CreateTestDispatcher(),
    listenerPairing,
    state,
    new AppearanceConfiguration()))
{
    await listener.StartAsync();
    AssertTrue(listener.Port > 0, "websocket listener port assigned");

    using var socket = new ClientWebSocket();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{listener.Port}/ws?token=diagnostic-test-token"), timeout.Token);
    await SendStringAsync(socket, ProtocolSerializer.Serialize(
        ProtocolConstants.MessageTypes.Hello,
        requestId: null,
        new HelloMessage("test-device", "Test CYD", "esp32", [])), timeout.Token);

    var reply = await ReceiveStringAsync(socket, timeout.Token);
    AssertContains("connection.state", reply, "websocket hello response");
    var pending = await listenerPairing.ListPendingAsync(timeout.Token);
    AssertEqual(1, pending.Count, "pending pairing count");
    AssertEqual("test-device", pending[0].DeviceId, "pending pairing device");
    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", timeout.Token);
}

var coverCacheDirectory = Path.Combine(Path.GetTempPath(), $"streamdeck-covers-{Guid.NewGuid():N}");
var coverId = new string('a', 64);
Directory.CreateDirectory(Path.Combine(coverCacheDirectory, "covers"));
await File.WriteAllBytesAsync(Path.Combine(coverCacheDirectory, "covers", $"{coverId}.jpg"), [0xFF, 0xD8, 0xFF, 0xD9]);
await using (var coverListener = new WindowsAgentWebSocketServer(
    new TransportOptions
    {
        BindAddress = IPAddress.Loopback,
        Port = 0,
        WebSocketPath = "/ws",
        AllowRemoteClients = false
    },
    CreateTestDispatcher(),
    new PairingApprovalService(Path.Combine(Path.GetTempPath(), $"streamdeck-cover-listener-pairing-{Guid.NewGuid():N}.json")),
    state,
    new AppearanceConfiguration(),
    shortcuts: null,
    coverCacheDirectory))
{
    await coverListener.StartAsync();
    using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{coverListener.Port}/") };
    using var coverResponse = await client.GetAsync($"api/covers/{coverId}.jpg");
    AssertTrue(coverResponse.IsSuccessStatusCode, "cover cache route returns cached jpeg");
    AssertEqual("image/jpeg", coverResponse.Content.Headers.ContentType?.MediaType, "cover cache route content type");
    AssertEqual(4, (await coverResponse.Content.ReadAsByteArrayAsync()).Length, "cover cache route bytes");

    using var missingResponse = await client.GetAsync($"api/covers/{new string('b', 64)}.jpg");
    AssertEqual(HttpStatusCode.NotFound, missingResponse.StatusCode, "missing cover returns 404");

    using var traversalResponse = await client.GetAsync("api/covers/../secret.jpg");
    AssertEqual(HttpStatusCode.NotFound, traversalResponse.StatusCode, "invalid cover id returns 404");
}
Directory.Delete(coverCacheDirectory, recursive: true);

var runtimeState = new AgentState();
var runtimePairingStore = Path.Combine(Path.GetTempPath(), $"streamdeck-runtime-pairing-{Guid.NewGuid():N}.json");
var runtimePairing = new PairingApprovalService(runtimePairingStore);
await runtimePairing.AuthorizeAsync(new HelloMessage("runtime-device", "Runtime CYD", "esp32", []), bearerToken: null);
AssertTrue(await runtimePairing.ApproveAsync("runtime-device"), "runtime device pairing approved");

var failedForgetState = new AgentState();
failedForgetState.MarkDeviceConnected("missing-device", "Missing CYD", "192.168.1.60");
failedForgetState.MarkDeviceDisconnected("missing-device");
await using (var failedForgetRuntime = new WindowsAgentRuntime(
    failedForgetState,
    new PairingApprovalService(Path.Combine(Path.GetTempPath(), $"streamdeck-runtime-failed-forget-{Guid.NewGuid():N}.json")),
    new AgentOptions(MockMode: true)))
{
    AssertTrue(!await failedForgetRuntime.ForgetConnectedDeviceAsync(), "forget missing pairing fails");
    var failedForgetSnapshot = failedForgetState.Snapshot();
    AssertEqual("missing-device", failedForgetSnapshot.DeviceId, "failed forget preserves device id");
    AssertEqual("Missing CYD", failedForgetSnapshot.DeviceName, "failed forget preserves device name");
}

await using (var runtime = new WindowsAgentRuntime(
    runtimeState,
    runtimePairing,
    new AgentOptions(MockMode: true),
    initialAudioProvider: () => new AudioSnapshot(73, false),
    listenPortOverride: 0))
{
    await runtime.StartAsync();
    AssertEqual("listening", runtimeState.Snapshot().HostStatus, "mock runtime status");
    using var socket = new ClientWebSocket();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{runtime.Port}/ws"), timeout.Token);
    await SendStringAsync(socket, ProtocolSerializer.Serialize(
        ProtocolConstants.MessageTypes.Hello,
        requestId: null,
        new HelloMessage("first-media-device", "First Media CYD", "esp32", [])), timeout.Token);
    var pendingReply = await ReceiveStringAsync(socket, timeout.Token);
    AssertContains("reconnecting", pendingReply, "unapproved device waits for pairing");
    AssertTrue(await runtimePairing.ApproveAsync("first-media-device", timeout.Token), "runtime device pairing approved while connected");

    var receivedInitialMediaUpdate = false;
    var receivedInitialAudioUpdate = false;
    var receivedInitialTypes = new List<string>();
    using (var mediaTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
    {
        try
        {
            while (!receivedInitialMediaUpdate || !receivedInitialAudioUpdate)
            {
                var message = await ReceiveStringAsync(socket, mediaTimeout.Token);
                using var received = JsonDocument.Parse(message);
                receivedInitialTypes.Add(received.RootElement.GetProperty("type").GetString() ?? "unknown");
                receivedInitialMediaUpdate |= message.Contains("\"type\":\"media.update\"", StringComparison.Ordinal);
                if (message.Contains("\"type\":\"audio.update\"", StringComparison.Ordinal))
                {
                    using var audio = JsonDocument.Parse(message);
                    AssertEqual(73, audio.RootElement.GetProperty("data").GetProperty("volumePercent").GetInt32(), "runtime sends initial host volume to device");
                    receivedInitialAudioUpdate = true;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
    AssertTrue(receivedInitialAudioUpdate, "runtime sends initial audio update after device connects");
    AssertTrue(receivedInitialMediaUpdate, $"runtime sends initial media update after device connects; received: {string.Join(",", receivedInitialTypes)}");
    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", timeout.Token);

    var mdnsResponse = await QueryMdnsAsync(runtime.Port, timeout.Token);
    AssertContains("_streamdeck", Encoding.UTF8.GetString(mdnsResponse), "mdns service response");
    AssertTrue(ContainsSrvPort(mdnsResponse, runtime.Port), "mdns srv port");

    runtimeState.MarkDeviceConnected("runtime-device", "Runtime CYD", "192.168.1.61");
    runtimeState.MarkDeviceDisconnected("runtime-device");
    var offlineSnapshot = runtimeState.Snapshot();
    AssertEqual("runtime-device", offlineSnapshot.DeviceId, "normal disconnect preserves runtime device id");
    AssertEqual("Runtime CYD", offlineSnapshot.DeviceName, "normal disconnect preserves runtime device name");
    AssertTrue(await runtime.ForgetConnectedDeviceAsync(), "forget approved offline pairing succeeds");
    var forgottenSnapshot = runtimeState.Snapshot();
    AssertEqual("listening", forgottenSnapshot.HostStatus, "forgotten device status");
    AssertEqual(null, forgottenSnapshot.DeviceId, "forgotten device id cleared");
    AssertEqual(null, forgottenSnapshot.DeviceName, "forgotten device name cleared");
    AssertEqual(null, forgottenSnapshot.RemoteAddress, "forgotten remote address cleared");
}

await TestArduinoOtaUploadSuccessAsync();
await TestArduinoOtaUploadRejectsWrongPasswordAsync();
await TestArduinoOtaValidationFailuresAsync();

Console.WriteLine("PASS StreamDeck.WindowsAgent smoke tests");

static async Task TestArduinoOtaUploadSuccessAsync()
{
    var firmware = CreateFirmwareImage(4096);
    var progressValues = new List<FirmwareUpdateProgress>();
    await using var peer = await FakeArduinoOtaPeer.StartAsync("setup-secret");

    var updater = new ArduinoOtaFirmwareUpdater();
    var result = await updater.UploadAsync(
        new FirmwareUpdateRequest(
            IPAddress.Loopback,
            "setup-secret",
            () => new MemoryStream(firmware, writable: false),
            peer.Port,
            TimeSpan.FromSeconds(5)),
        new Progress<FirmwareUpdateProgress>(progressValues.Add),
        CancellationToken.None);

    AssertTrue(result.Success, "ota upload succeeds");
    await peer.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    AssertEqual(firmware.Length, peer.ReceivedBytes, "ota received bytes");
    AssertEqual(Convert.ToHexString(MD5.HashData(firmware)).ToLowerInvariant(), peer.ReceivedMd5, "ota received md5");
    AssertTrue(progressValues.Count > 0, "ota reports progress");
    AssertEqual(firmware.Length, progressValues.Last().BytesSent, "ota final progress bytes");
    AssertEqual(firmware.Length, progressValues.Last().TotalBytes, "ota final progress total");
}

static async Task TestArduinoOtaUploadRejectsWrongPasswordAsync()
{
    var firmware = CreateFirmwareImage(1024);
    await using var peer = await FakeArduinoOtaPeer.StartAsync("expected-secret");

    var updater = new ArduinoOtaFirmwareUpdater();
    var result = await updater.UploadAsync(
        new FirmwareUpdateRequest(
            IPAddress.Loopback,
            "wrong-secret",
            () => new MemoryStream(firmware, writable: false),
            peer.Port,
            TimeSpan.FromSeconds(2)),
        progress: null,
        CancellationToken.None);

    AssertTrue(!result.Success, "ota wrong password fails");
    AssertContains("authentication failed", result.SafeError ?? string.Empty, "ota wrong password safe error");
    await peer.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    AssertEqual(0, peer.ReceivedBytes, "ota wrong password does not upload image");
}

static async Task TestArduinoOtaValidationFailuresAsync()
{
    var updater = new ArduinoOtaFirmwareUpdater();
    var invalidMagic = await updater.UploadAsync(
        new FirmwareUpdateRequest(
            IPAddress.Loopback,
            "setup-secret",
            () => new MemoryStream(new byte[] { 0x00, 0x01, 0x02 }, writable: false),
            devicePort: 9,
            timeout: TimeSpan.FromMilliseconds(100)),
        progress: null,
        CancellationToken.None);
    AssertTrue(!invalidMagic.Success, "ota invalid magic fails");
    AssertContains("valid ESP firmware", invalidMagic.SafeError ?? string.Empty, "ota invalid magic safe error");

    var oversized = new byte[ArduinoOtaFirmwareUpdater.MaxFirmwareBytes + 1];
    oversized[0] = 0xE9;
    var tooLarge = await updater.UploadAsync(
        new FirmwareUpdateRequest(
            IPAddress.Loopback,
            "setup-secret",
            () => new MemoryStream(oversized, writable: false),
            devicePort: 9,
            timeout: TimeSpan.FromMilliseconds(100)),
        progress: null,
        CancellationToken.None);
    AssertTrue(!tooLarge.Success, "ota oversized image fails");
    AssertContains("too large", tooLarge.SafeError ?? string.Empty, "ota oversized safe error");
}

static byte[] CreateFirmwareImage(int length)
{
    var image = new byte[length];
    image[0] = 0xE9;
    for (var index = 1; index < image.Length; index++)
    {
        image[index] = (byte)(index % 251);
    }

    return image;
}

static DeviceCommandDispatcher CreateTestDispatcher()
{
    return new DeviceCommandDispatcher(
        new TestMediaController(),
        new TestAudioController(),
        new TestShortcutExecutor(),
        new RejectingSystemCommandExecutor());
}

static async Task SendStringAsync(ClientWebSocket socket, string value, CancellationToken cancellationToken)
{
    var bytes = Encoding.UTF8.GetBytes(value);
    await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
}

static async Task<string> ReceiveStringAsync(ClientWebSocket socket, CancellationToken cancellationToken)
{
    var buffer = new byte[16 * 1024];
    var result = await socket.ReceiveAsync(buffer, cancellationToken);
    return Encoding.UTF8.GetString(buffer, 0, result.Count);
}

static async Task<byte[]> QueryMdnsAsync(int expectedPort, CancellationToken cancellationToken)
{
    using var udp = new UdpClient(AddressFamily.InterNetwork);
    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
    var query = CreateMdnsPtrQuery("_streamdeck._tcp.local");
    await udp.SendAsync(query, new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353), cancellationToken);
    while (true)
    {
        var result = await udp.ReceiveAsync(cancellationToken);
        if (ContainsSrvPort(result.Buffer, expectedPort)) return result.Buffer;
    }
}

static byte[] CreateMdnsPtrQuery(string name)
{
    using var stream = new MemoryStream();
    WriteUInt16(stream, 0);
    WriteUInt16(stream, 0);
    WriteUInt16(stream, 1);
    WriteUInt16(stream, 0);
    WriteUInt16(stream, 0);
    WriteUInt16(stream, 0);
    WriteDnsName(stream, name);
    WriteUInt16(stream, 12);
    WriteUInt16(stream, 0x8001);
    return stream.ToArray();
}

static bool ContainsSrvPort(byte[] message, int port)
{
    var expectedPortHigh = (byte)(port >> 8);
    var expectedPortLow = (byte)port;
    for (var index = 0; index < message.Length - 15; index++)
    {
        if (message[index] == 0x00
            && message[index + 1] == 0x21
            && message[index + 2] == 0x00
            && message[index + 3] == 0x01
            && message[index + 14] == expectedPortHigh
            && message[index + 15] == expectedPortLow)
        {
            return true;
        }
    }

    return false;
}

static void WriteDnsName(Stream stream, string name)
{
    foreach (var label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
    {
        var bytes = Encoding.UTF8.GetBytes(label);
        stream.WriteByte((byte)bytes.Length);
        stream.Write(bytes);
    }

    stream.WriteByte(0);
}

static void WriteUInt16(Stream stream, int value)
{
    stream.WriteByte((byte)(value >> 8));
    stream.WriteByte((byte)value);
}

static void AssertEqual<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }
}

static void AssertTrue(bool value, string label)
{
    if (!value)
    {
        throw new InvalidOperationException($"{label}: expected true");
    }
}

static void AssertContains(string expected, string actual, string label)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{label}: expected content to contain {expected}");
    }
}

static void AssertDoesNotContain(string unexpected, string actual, string label)
{
    if (actual.Contains(unexpected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{label}: expected content not to contain {unexpected}");
    }
}

static void AssertThrows<TException>(Action action, string label)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    catch (Exception exception)
    {
        throw new InvalidOperationException(
            $"{label}: expected {typeof(TException).Name}, got {exception.GetType().Name}",
            exception);
    }

    throw new InvalidOperationException($"{label}: expected {typeof(TException).Name}");
}

static void AssertShortcutEditorLayout(ShortcutEditorDialog dialog, Size size, string label)
{
    dialog.StartPosition = FormStartPosition.Manual;
    dialog.Location = new Point(-2000, -2000);
    dialog.Size = size;
    if (!dialog.Visible)
    {
        dialog.Show();
    }

    System.Windows.Forms.Application.DoEvents();
    dialog.PerformLayout();
    System.Windows.Forms.Application.DoEvents();

    var controls = FlattenControls(dialog).Where(control => control.Visible).ToArray();
    var essentials = new System.Windows.Forms.Control[]
    {
        controls.Single(control => control.AccessibleName == "Shortcut ID"),
        controls.Single(control => control.AccessibleName == "Shortcut name"),
        controls.Single(control => control.AccessibleName == "Shortcut icon"),
        controls.Single(control => control.AccessibleName == "Shortcut action"),
        controls.Single(control => control.AccessibleName == "Shortcut target"),
        controls.Single(control => control.AccessibleName == "Shortcut arguments"),
        controls.Single(control => control.AccessibleName == "Browse for shortcut target"),
        controls.Single(control => control.AccessibleName == "Shortcut action help"),
        controls.OfType<System.Windows.Forms.Button>().Single(button => button.Text == "Save"),
        controls.OfType<System.Windows.Forms.Button>().Single(button => button.Text == "Cancel")
    };

    var bounds = essentials.ToDictionary(control => control, control => BoundsInClient(dialog, control));
    foreach (var (control, rectangle) in bounds)
    {
        AssertTrue(rectangle.Width > 0 && rectangle.Height > 0, $"shortcut editor {label} {DescribeControl(control)} has positive bounds");
        AssertTrue(dialog.ClientRectangle.Contains(rectangle), $"shortcut editor {label} {DescribeControl(control)} remains inside the client area");
    }

    for (var left = 0; left < essentials.Length; left++)
    {
        for (var right = left + 1; right < essentials.Length; right++)
        {
            AssertTrue(!bounds[essentials[left]].IntersectsWith(bounds[essentials[right]]), $"shortcut editor {label} {DescribeControl(essentials[left])} does not overlap {DescribeControl(essentials[right])}");
        }
    }
}

static IEnumerable<System.Windows.Forms.Control> FlattenControls(System.Windows.Forms.Control root)
{
    foreach (System.Windows.Forms.Control child in root.Controls)
    {
        yield return child;
        foreach (var descendant in FlattenControls(child))
        {
            yield return descendant;
        }
    }
}

static Rectangle BoundsInClient(System.Windows.Forms.Form form, System.Windows.Forms.Control control)
{
    var screenBounds = control.RectangleToScreen(control.ClientRectangle);
    return new Rectangle(form.PointToClient(screenBounds.Location), screenBounds.Size);
}

static string DescribeControl(System.Windows.Forms.Control control) => string.IsNullOrWhiteSpace(control.AccessibleName)
    ? $"{control.GetType().Name} '{control.Text}'"
    : control.AccessibleName;

sealed class TestMediaController : IHostMediaController
{
    public Task<bool> ExecuteAsync(MediaCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }
}

sealed class TestAudioController : IHostAudioController
{
    public Task<bool> SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    public Task<bool> ToggleMuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }
}

sealed class TestShortcutExecutor : IHostShortcutExecutor
{
    public Task<bool> ExecuteAsync(string shortcutId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }
}

sealed class FakeArduinoOtaPeer : IAsyncDisposable
{
    private readonly UdpClient udp;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task runTask;
    private readonly string password;

    private FakeArduinoOtaPeer(UdpClient udp, string password)
    {
        this.udp = udp;
        this.password = password;
        Port = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
        runTask = Task.Run(RunAsync);
    }

    public int Port { get; }

    public int ReceivedBytes { get; private set; }

    public string? ReceivedMd5 { get; private set; }

    public Task Completion => runTask;

    public static Task<FakeArduinoOtaPeer> StartAsync(string password)
    {
        var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return Task.FromResult(new FakeArduinoOtaPeer(udp, password));
    }

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        udp.Dispose();
        try
        {
            await runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        cancellation.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            var invitation = await udp.ReceiveAsync(cancellation.Token);
            var invitationText = Encoding.ASCII.GetString(invitation.Buffer).Trim();
            var parts = invitationText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            TestAssert.Equal("0", parts[0], "ota command");
            var hostPort = int.Parse(parts[1], CultureInfo.InvariantCulture);
            var imageSize = int.Parse(parts[2], CultureInfo.InvariantCulture);
            var expectedMd5 = parts[3];

            const string nonce = "0123456789abcdef0123456789abcdef";
            await SendUdpAsync($"AUTH {nonce}", invitation.RemoteEndPoint);
            var auth = await udp.ReceiveAsync(cancellation.Token);
            var authText = Encoding.ASCII.GetString(auth.Buffer).Trim();
            var authParts = authText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            TestAssert.Equal("200", authParts[0], "ota auth command");
            var cnonce = authParts[1];
            var response = authParts[2];
            var passwordHash = ComputeHexMd5(password);
            var expectedResponse = ComputeHexMd5($"{passwordHash}:{nonce}:{cnonce}");
            if (!string.Equals(response, expectedResponse, StringComparison.Ordinal))
            {
                await SendUdpAsync("Authentication Failed", invitation.RemoteEndPoint);
                return;
            }

            await SendUdpAsync("OK", invitation.RemoteEndPoint);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, hostPort, cancellation.Token);
            await using var stream = client.GetStream();
            using var md5 = MD5.Create();
            var buffer = new byte[1460];
            while (ReceivedBytes < imageSize)
            {
                var read = await stream.ReadAsync(buffer, cancellation.Token);
                if (read == 0)
                {
                    break;
                }

                md5.TransformBlock(buffer, 0, read, null, 0);
                ReceivedBytes += read;
                var ack = Encoding.ASCII.GetBytes(read.ToString(CultureInfo.InvariantCulture));
                await stream.WriteAsync(ack, cancellation.Token);
            }

            md5.TransformFinalBlock([], 0, 0);
            ReceivedMd5 = Convert.ToHexString(md5.Hash!).ToLowerInvariant();
            if (ReceivedBytes == imageSize && string.Equals(ReceivedMd5, expectedMd5, StringComparison.Ordinal))
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes("OK"), cancellation.Token);
            }
        }
        catch (SocketException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellation.IsCancellationRequested)
        {
        }
    }

    private async Task SendUdpAsync(string message, IPEndPoint endpoint)
    {
        var bytes = Encoding.ASCII.GetBytes(message);
        await udp.SendAsync(bytes, endpoint, cancellation.Token);
    }

    private static string ComputeHexMd5(string value)
    {
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}

static class TestAssert
{
    public static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
        }
    }
}
