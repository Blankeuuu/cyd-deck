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

if (args.Contains("--live-system")) {
    using var liveMetrics = new StreamDeck.SystemMonitoring.WindowsSystemMetricsProvider();
    for (var i = 0; i < 8; ++i) { var reading = liveMetrics.GetSnapshot(); Console.WriteLine($"GPU={reading.GpuUsagePercent?.ToString("F1") ?? "unavailable"}; TEMP={reading.CpuTemperatureCelsius?.ToString("F1") ?? "unavailable"}; source={reading.TemperatureSource}"); await Task.Delay(1000); }
    return;
}

if (args.Contains("--bug-repro")) { DesktopBugRegressionTests.Run(); return; }

if (args.Contains("--desktop-review")) { DesktopViewportTests.Run(); return; }

// Icon previews use the same glyph masks as the CYD; URL images round-trip to RGB565A8.
AssertEqual(21, ShortcutIconCatalog.All.Count, "all supported icons are visible in picker");
AssertEqual("hotkey", ShortcutIconCatalog.Normalize(" Keyboard "), "icon alias normalization");
using (var source = new Bitmap(48, 24))
using (var graphics = Graphics.FromImage(source))
using (var png = new MemoryStream())
{
    graphics.Clear(Color.Red); source.Save(png, System.Drawing.Imaging.ImageFormat.Png);
    var data = ShortcutIconCatalog.Encode(png.ToArray());
    var pixels = Convert.FromBase64String(data);
    AssertEqual(1728, pixels.Length, "custom icon exact byte length");
    AssertEqual((byte)0, pixels[1152], "custom icon letterbox transparency");
    AssertEqual((byte)255, pixels[1152 + 12 * 24 + 12], "custom icon center alpha");
    AssertEqual((byte)0xF8, pixels[(12 * 24 + 12) * 2 + 1], "custom icon RGB565 little endian red");
    var items = Enumerable.Range(0, 6).Select(i => new DeviceShortcutDefinition("slot" + i, "Custom", "custom", "both", IconData: data)).ToArray();
    var frame = ProtocolStateMapper.CreateShortcutsUpdate(new DeviceShortcutConfiguration(items));
    AssertTrue(Encoding.UTF8.GetByteCount(frame) < 16384, "six custom icons fit serial frame");
    AssertTrue(!frame.Contains("\\u002B"), "icon payload avoids JSON plus escapes");

    // Stress frame size with maximum permissible ID (64 chars) and Label (19 bytes UTF-8)
    var maxId = new string('s', 64);
    var maxLabel = "1234567890123456789"; // 19 UTF-8 bytes
    var maxItems = Enumerable.Range(0, 6).Select(i => new DeviceShortcutDefinition($"slot{i}-{maxId[..50]}", maxLabel, "custom", "both", IconData: data)).ToArray();
    var maxFrame = ProtocolStateMapper.CreateShortcutsUpdate(new DeviceShortcutConfiguration(maxItems));
    AssertTrue(Encoding.UTF8.GetByteCount(maxFrame) < 16384, $"six custom icons with max labels and IDs fit serial frame ({Encoding.UTF8.GetByteCount(maxFrame)} < 16384)");

    using var doc = JsonDocument.Parse(frame);
    var transmitted = doc.RootElement.GetProperty("data").GetProperty("shortcuts")[0].GetProperty("iconData").GetString()!;
    AssertTrue(pixels.SequenceEqual(Convert.FromBase64String(transmitted.Replace('-', '+').Replace('_', '/'))), "custom icon protocol round trip");
    using var output = new Bitmap(24, 24);
    using var outputGraphics = Graphics.FromImage(output);
    ShortcutIconCatalog.Draw(outputGraphics, "custom", data, new Rectangle(0, 0, 24, 24), Color.White);
    AssertTrue(output.GetPixel(12, 12).R > 240 && output.GetPixel(12, 12).G == 0, "Windows renders actual imported pixels");
}
AssertThrows<InvalidDataException>(() => ShortcutIconCatalog.Encode([1, 2, 3]), "invalid image rejected");
await TestShortcutIconDownloadLoopbackAsync();

if (args.Contains("--live-audio"))
{
    using var endpoint = new CoreAudioEndpoint();
    using var liveAudio = new WindowsMasterVolumeService(endpoint);
    AssertTrue(liveAudio.Refresh(), "default render/multimedia CoreAudio endpoint is readable");
    Console.WriteLine($"CoreAudio master volume={liveAudio.CurrentSnapshot!.VolumePercent}%, muted={liveAudio.CurrentSnapshot.IsMuted}");
    return;
}

if (args.Contains("--live-media"))
{
    await using var liveSource = new StreamDeck.Media.WindowsGlobalMediaSessionSource();
    await liveSource.InitializeAsync();
    if (args.Contains("--stress"))
    {
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var iteration = 0; iteration < 100; iteration++)
                try { liveSource.GetSessions(); liveSource.GetCurrentSession(); }
                catch (Exception exception) { errors.Enqueue(exception); }
        })));
        Console.WriteLine($"Concurrent native reads: failures={errors.Count}");
        foreach (var error in errors.Take(2)) Console.WriteLine(error);
        if (!errors.IsEmpty) Environment.ExitCode = 1;
    }
    for (var attempt = 0; attempt < 3; attempt++)
    {
        try
        {
            var sessions = liveSource.GetSessions();
            var current = liveSource.GetCurrentSession();
            Console.WriteLine($"Sessions={sessions.Count}, current={current is not null}");
            foreach (var session in sessions)
            {
                var mapped = await new StreamDeck.Media.WindowsMediaSessionMapper().MapAsync(session);
                Console.WriteLine($"App={mapped.SourceAppUserModelId}, state={mapped.PlaybackState}, titlePresent={!string.IsNullOrWhiteSpace(mapped.Title)}, thumbnail={mapped.Thumbnail is not null}");
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception.ToString());
            Environment.ExitCode = 1;
        }
    }
    return;
}

if (args.Contains("--usb-covers")) { await TestUsbCoverSenderAsync(); Console.WriteLine("USB artwork regression PASS"); return; }

var coverDiagnosticFrame = ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.DeviceInfo, null,
    new DeviceInfoMessage("1.0.0", 10, new DeviceWifiInfo(true, "lab", -50, "192.168.1.2"), "usb",
        new DeviceStorageInfo(true, true, 1024, 512), new DeviceHeapInfo(1000, 900, 800), new DeviceCoverInfo(2, true)));
var coverDiagnosticParse = ProtocolSerializer.Parse(coverDiagnosticFrame);
AssertTrue(coverDiagnosticParse.IsSuccess, "device.info accepts optional artwork diagnostic extension");
AssertTrue(ProtocolSerializer.DeserializeData<DeviceInfoMessage>(coverDiagnosticParse.Envelope!).Cover?.Rendered == true, "device.info preserves firmware-confirmed artwork rendering");
AgentDiagnosticLog.Write("Synthetic hello {\"bearerToken\":\"test-secret-for-redaction\"}");
AssertTrue(!AgentDiagnosticLog.ReadRecentLines().Last().Contains("test-secret-for-redaction", StringComparison.Ordinal), "raw hello logs never expose bearerToken");

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
AssertEqual("Connected now", AgentStatusFormatter.FormatLastSeen(snapshot), "connected last seen before telemetry");

state.UpdateDeviceInfo(new DeviceInfoMessage(
    "test-firmware",
    12,
    new DeviceWifiInfo(true, "lab", -55, "192.168.1.51"),
    "192.168.1.10",
    new DeviceStorageInfo(true, true, 1024, 512),
    new DeviceHeapInfo(1000, 900, 800)));
snapshot = state.Snapshot();
AssertTrue(snapshot.DeviceInfoUpdatedAtUtc.HasValue, "device info timestamp populated");
AssertEqual("Connected now", AgentStatusFormatter.FormatLastSeen(snapshot), "connected last seen recency");

state.MarkDeviceDisconnected("device-1");
snapshot = state.Snapshot();
AssertEqual("listening", snapshot.HostStatus, "host status after disconnect");
AssertEqual("device-1", snapshot.DeviceId, "device id preserved after disconnect");
AssertEqual("CYD", snapshot.DeviceName, "device name preserved after disconnect");
AssertEqual(null, snapshot.RemoteAddress, "remote address after disconnect");
AssertEqual("test-firmware", snapshot.DeviceInfo?.FirmwareVersion, "device info preserved after disconnect");
AssertTrue(snapshot.DeviceInfoUpdatedAtUtc.HasValue, "device info timestamp preserved after disconnect");
AssertContains("seconds ago", AgentStatusFormatter.FormatLastSeen(snapshot), "last seen preserved after disconnect");
AssertEqual("Listening", AgentStatusFormatter.ToUserStatus(snapshot.HostStatus), "listener readiness must not imply an active connection attempt");
AssertEqual("Disconnected", AgentStatusFormatter.ToDeviceStatus(snapshot), "listening device status");

var offlineValues = SettingsWindow.CreateLiveInfoValues("Device", snapshot);
AssertEqual("Unavailable", offlineValues["Firmware"], "offline telemetry is not presented as current");
var changedDevice = SettingsWindow.CreateLiveInfoValues("Overview", new AgentStatusSnapshot(
    "connected", "new-device", "New CYD", "USB", 8765, 5000, "live", []));
AssertEqual("New CYD", changedDevice["Device name"], "overview updates device name when a different device connects");
AssertEqual("new-device", changedDevice["Device ID"], "overview updates device identity after reconnect");
if (args.Contains("--status-regression"))
{
    Console.WriteLine("Connection status regression PASS");
    return;
}
var updatesInfo = SettingsWindow.CreateLiveInfoValues("Updates", new AgentStatusSnapshot(
    "connected", "device-1", "CYD", "USB", 8765, 5000, "live", [], snapshot.DeviceInfo));
AssertEqual("test-firmware", updatesInfo["Firmware version"], "Updates reads the shared telemetry firmware");
AssertEqual("192.168.1.51", updatesInfo["Device IP"], "Updates reads Wi-Fi IP even with USB control");
var startupRaceState = new AgentState();
startupRaceState.MarkDeviceConnected("fast-device", "Fast CYD", "192.168.1.70");
startupRaceState.SetHostListening(9876);
var startupRaceSnapshot = startupRaceState.Snapshot();
AssertEqual("connected", startupRaceSnapshot.HostStatus, "listener setup preserves fast device connection");
AssertEqual(9876, startupRaceSnapshot.HostPort, "listener setup still records host port after fast device connection");
AssertEqual("fast-device", startupRaceSnapshot.DeviceId, "listener setup preserves fast device id");

var options = AgentOptions.FromArgs(["--mock"]);
AssertTrue(options.MockMode, "mock option");
var usbOptions = AgentOptions.FromArgs(["--mock", "--usb-port", "COM7"]);
AssertEqual("COM7", usbOptions.UsbPort, "usb port option");
AssertTrue(UsbSerialPortDiscovery.IsCh340HardwareId("VID_1A86&PID_7523&REV_0264"), "USB discovery recognizes the CH340 parent key hardware ID");
AssertTrue(UsbSerialPortDiscovery.IsCh340HardwareId(@"USB\VID_1A86&PID_7523&REV_0264"), "USB discovery recognizes the CH340 hardware ID in a device-instance value");
AssertTrue(!UsbSerialPortDiscovery.IsCh340HardwareId(@"USB\VID_10C4&PID_EA60"), "USB discovery rejects unrelated instance hardware IDs");
AssertEqual(
    "COM8",
    UsbSerialPortDiscovery.SelectAvailableCh340Ports([(@"USB\VID_1A86&PID_7523&REV_0264", "COM8")], ["COM8"]).Single(),
    "USB discovery selects a CH340 port when the ID is stored as an instance value");
AssertEqual(
    "COM5",
    UsbSerialPortDiscovery.SelectAvailableCh340Ports(
        [("VID_10C4&PID_EA60", "COM7"), ("VID_1A86&PID_7523", "COM5")],
        ["COM5", "COM7"]).Single(),
    "USB discovery selects the connected CH340 port");
AssertEqual(
    "COM5,COM8",
    string.Join(',', UsbSerialPortDiscovery.SelectAvailableCh340Ports(
        [("VID_1A86&PID_7523", "COM8"), ("VID_1A86&PID_7523", "COM5"), ("VID_1A86&PID_7523", "COM5")],
        ["COM5", "COM8"])),
    "USB discovery returns sorted unique CH340 candidates");
AssertEqual(
    0,
    UsbSerialPortDiscovery.SelectAvailableCh340Ports(
        [("VID_1A86&PID_7523", "COM5")],
        ["COM7"]).Count,
    "USB discovery ignores stale CH340 port registrations");
AssertEqual(
    0,
    UsbSerialPortDiscovery.SelectAvailableCh340Ports(
        [("VID_10C4&PID_EA60", "COM7")],
        ["COM7"]).Count,
    "USB discovery does not claim unrelated serial hardware");

AssertTrue(UsbSerialPortDiscovery.IsCp210xHardwareId("VID_10C4&PID_EA60"), "USB discovery recognizes CP210x parent key hardware ID");
AssertTrue(UsbSerialPortDiscovery.IsCp210xHardwareId(@"USB\VID_10C4&PID_EA60&REV_0100"), "USB discovery recognizes CP210x hardware ID in device-instance value");
AssertTrue(!UsbSerialPortDiscovery.IsCp210xHardwareId(@"USB\VID_0403&PID_6001"), "USB discovery rejects unrelated non-CP210x hardware IDs");
AssertTrue(UsbSerialPortDiscovery.IsSupportedHardwareId(@"USB\VID_1A86&PID_7523"), "Supported discovery recognizes CH340");
AssertTrue(UsbSerialPortDiscovery.IsSupportedHardwareId(@"USB\VID_10C4&PID_EA60"), "Supported discovery recognizes CP210x");
AssertTrue(!UsbSerialPortDiscovery.IsSupportedHardwareId(@"USB\VID_0403&PID_6001"), "Supported discovery rejects unrelated FTDI adapters");
AssertTrue(!UsbSerialPortDiscovery.IsSupportedHardwareId(@"USB\VID_067B&PID_2303"), "Supported discovery rejects unrelated Prolific adapters");

AssertEqual(
    "COM7",
    UsbSerialPortDiscovery.SelectAvailableSupportedPorts([(@"USB\VID_10C4&PID_EA60", "COM7")], ["COM7"]).Single(),
    "USB discovery selects connected CP210x port");
AssertEqual(
    "COM5,COM7",
    string.Join(',', UsbSerialPortDiscovery.SelectAvailableSupportedPorts(
        [("VID_10C4&PID_EA60", "COM7"), ("VID_1A86&PID_7523", "COM5")],
        ["COM5", "COM7"])),
    "USB discovery returns both CH340 and CP210x sorted candidates");
AssertEqual(
    0,
    UsbSerialPortDiscovery.SelectAvailableSupportedPorts(
        [("VID_0403&PID_6001", "COM9")],
        ["COM9"]).Count,
    "USB discovery rejects unrelated serial hardware");

AssertTrue(WindowsAgentApplicationContext.ShouldTriggerPairingNotification("listening", "pairing pending"), "Pairing pending transition triggers tray notification");
AssertTrue(!WindowsAgentApplicationContext.ShouldTriggerPairingNotification("pairing pending", "pairing pending"), "Repeated pairing pending does not trigger notification spam");
AssertTrue(WindowsAgentApplicationContext.ShouldTriggerPairingNotification("connected", "pairing pending"), "Reconnect to pairing pending triggers notification");
AssertTrue(!WindowsAgentApplicationContext.ShouldTriggerPairingNotification("pairing pending", "connected"), "Connected transition does not trigger pairing notification");

AssertEqual("COM7", WindowsAgentRuntime.ResolveUsbPortCandidates(usbOptions).Single(), "explicit USB override is a hard single candidate");
await TestUsbLineProtocolTransportAsync();
await TestUsbCoverSenderAsync();
await TestUsbRuntimeAdvancesFromSilentCandidateToValidEsp32HelloAsync();
await TestUsbRuntimeAdvancesFromPortOpenFailureToValidEsp32HelloAsync();
await TestUsbRuntimeRejectsWrongRoleHelloAsync();
await TestUsbRuntimeHardOverrideRetriesSinglePortAsync();
await TestUsbRuntimeDisposesTimedOutProbeAsync();
await TestUsbProbeDoesNotConsumeFrameAfterHelloAsync();
await TestUsbProbeRecoversFromLeadingEmptyLineAndFragmentAsync();
await TestUsbDeviceRuntimeHandshakeAsync();
await TestReconnectWithSelectedComPortAsync();
await TestRuntimePortSwitchingAsync();
await TestDraftNullComPortResolvesToAutomaticAsync();
await TestSameIdLabelActionDraftPreservesDirtyAndDoesNotPersistShortcutsAsync();
await TestSaveFailureDoesNotTriggerReconnectAndPreservesDraftAsync();
await TestTrayProductionNotificationAndBalloonClickAsync();
var usbFirstCalls = new List<string>();
AssertTrue(await WindowsAgentRuntime.SendWithUsbPriorityAsync(
    usbReady: true,
    sendUsb: () => { usbFirstCalls.Add("USB"); return Task.FromResult(true); },
    sendWebSocket: () => { usbFirstCalls.Add("WebSocket"); return Task.FromResult(true); }),
    "runtime sends over ready USB before WebSocket");
AssertEqual("USB", string.Join(',', usbFirstCalls), "USB primary route avoids duplicate WebSocket command");

var wifiFallbackCalls = new List<string>();
AssertTrue(await WindowsAgentRuntime.SendWithUsbPriorityAsync(
    usbReady: false,
    sendUsb: () => { wifiFallbackCalls.Add("USB"); return Task.FromResult(true); },
    sendWebSocket: () => { wifiFallbackCalls.Add("WebSocket"); return Task.FromResult(true); }),
    "runtime uses Wi-Fi when USB is not ready");
AssertEqual("WebSocket", string.Join(',', wifiFallbackCalls), "ready Wi-Fi is the first fallback when USB is unavailable");

var usbRecoveryCalls = new List<string>();
AssertTrue(await WindowsAgentRuntime.SendWithUsbPriorityAsync(
    usbReady: false,
    sendUsb: () => { usbRecoveryCalls.Add("USB"); return Task.FromResult(true); },
    sendWebSocket: () => { usbRecoveryCalls.Add("WebSocket"); return Task.FromResult(false); }),
    "runtime tries USB after WebSocket is unavailable");
AssertEqual("WebSocket,USB", string.Join(',', usbRecoveryCalls), "USB recovery is attempted after failed Wi-Fi route");

var disconnectedUsbCalls = new List<string>();
AssertTrue(await WindowsAgentRuntime.SendWithUsbPriorityAsync(
    usbReady: true,
    sendUsb: () => { disconnectedUsbCalls.Add("USB"); throw new IOException("simulated detach"); },
    sendWebSocket: () => { disconnectedUsbCalls.Add("WebSocket"); return Task.FromResult(true); }),
    "runtime falls back to Wi-Fi after USB detach");
AssertEqual("USB,WebSocket", string.Join(',', disconnectedUsbCalls), "USB transport failure advances to Wi-Fi fallback");

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
AssertEqual(3, ShortcutEditorDialog.GetDisplayModeChoices().Count, "shortcut editor exposes label/icon/both display modes");
AssertEqual(ShortcutDisplayModes.Icon, ShortcutEditorDialog.NormalizeDisplayModeChoice(ShortcutEditorDialog.DisplayIconChoice), "shortcut editor maps icon display mode");
AssertEqual(ShortcutDisplayModes.Both, ShortcutEditorDialog.NormalizeDisplayModeChoice(ShortcutEditorDialog.DisplayBothChoice), "shortcut editor maps combined label/icon display mode");
var generatedShortcutId1 = ShortcutEditorDialog.CreateNewShortcutId();
var generatedShortcutId2 = ShortcutEditorDialog.CreateNewShortcutId();
AssertTrue(generatedShortcutId1.StartsWith("shortcut-", StringComparison.Ordinal), "new shortcut IDs use a generated prefix");
AssertTrue(!string.Equals(generatedShortcutId1, generatedShortcutId2, StringComparison.Ordinal), "new shortcut dialogs receive distinct IDs");
AssertEqual(1, shortcutActionChoices.Count(choice => ShortcutEditorDialog.NormalizeActionChoice(choice) == ShortcutActionTypes.LaunchApplication), "shortcut editor exposes one launch action choice");
AssertEqual(1, shortcutActionChoices.Count(choice => ShortcutEditorDialog.NormalizeActionChoice(choice) == ShortcutActionTypes.Hotkey), "shortcut editor exposes one generic hotkey action choice");
AssertEqual(0, shortcutActionChoices.Count(choice => ShortcutEditorDialog.NormalizeActionChoice(choice) == ShortcutActionTypes.PowerShell), "shortcut editor hides redundant PowerShell action choice");
AssertTrue(!shortcutActionChoices.Any(choice => choice.Contains("PowerShell", StringComparison.OrdinalIgnoreCase)), "shortcut editor does not show a separate PowerShell script action");
AssertTrue(!shortcutActionChoices.Contains(ShortcutActionTypes.LaunchApplication), "shortcut editor hides raw launch action type from display choices");
AssertEqual(ShortcutActionTypes.LaunchApplication, ShortcutEditorDialog.NormalizeActionChoice(ShortcutEditorDialog.LaunchApplicationChoice), "shortcut editor launch choice maps to supported launch action");
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
AssertTrue(shortcutActionChoices.All(choice => !choice.Contains("OBS", StringComparison.OrdinalIgnoreCase)), "shortcut editor does not expose redundant app-specific hotkey presets");
AssertContains("Win+L", ShortcutEditorDialog.GetActionFieldHints(ShortcutEditorDialog.SendHotkeyChoice).HelpText, "hotkey help documents reserved Windows shortcuts");
AssertEqual("Script", ShortcutEditorDialog.GetActionFieldHints(ShortcutActionTypes.PowerShell).TargetLabel, "legacy PowerShell shortcut types are edited through generic Script UI");
var quotedLaunchAction = ShortcutEditorDialog.CreateActionForInputs(ShortcutEditorDialog.LaunchApplicationChoice, "notepad.exe", "--name \"Two Words\" --flag");
AssertEqual(3, quotedLaunchAction.Arguments.Count, "quote-aware argument parser preserves argument count");
AssertEqual("--name", quotedLaunchAction.Arguments[0], "quote-aware argument parser keeps leading option");
AssertEqual("Two Words", quotedLaunchAction.Arguments[1], "quote-aware argument parser keeps grouped text");
var legacyPowerShellAction = ShortcutEditorDialog.CreateActionForInputs(ShortcutActionTypes.PowerShell, "C:\\Tools\\legacy.ps1", "-Verbose");
AssertEqual(ShortcutActionTypes.PowerShell, legacyPowerShellAction.Type, "legacy PowerShell action type remains round-trippable");
AssertEqual("C:\\Tools\\legacy.ps1", legacyPowerShellAction.ScriptPath, "legacy PowerShell action preserves script path");
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
AssertContains("Select a shortcut", SettingsWindow.ShortcutsSourceOfTruthText, "shortcuts page explains the next action");
AssertContains("first six", SettingsWindow.ShortcutsSourceOfTruthText, "shortcuts page explains device visibility limit");
AssertContains("first shortcut", SettingsWindow.CreateShortcutsEmptyStateText(emptyShortcutLoad), "empty shortcut catalog has explicit empty state");
AssertContains("Add", SettingsWindow.CreateShortcutsEmptyStateText(emptyShortcutLoad), "empty shortcut catalog names next action");
AssertContains("CYD", SettingsWindow.CreateShortcutsEmptyStateText(emptyShortcutLoad), "empty state describes the shortcut destination");
AssertContains("failed to load", SettingsWindow.CreateShortcutsEmptyStateText(shortcutLoadFailure), "load failure empty state is distinct from genuinely empty catalog");
AssertContains("Import full config", SettingsWindow.CreateShortcutsEmptyStateText(shortcutLoadFailure), "load failure empty state names recovery action");
AssertContains("inactive", SettingsWindow.PendingShortcutImportsText, "pending device shortcut imports are labeled inactive");
AssertContains("not executable or synced", SettingsWindow.PendingShortcutImportsText, "pending device shortcut imports are not executable or synced");
var pendingShortcutStore = new PendingShortcutImportStore();
pendingShortcutStore.UpdateFromDeviceReport(
    new ShortcutsReportMessage([
        new StreamDeck.Protocol.ShortcutDefinition("saved", "Saved", ActionType: ShortcutActionTypes.LaunchApplication),
        new StreamDeck.Protocol.ShortcutDefinition("device-only", "Device only", ActionType: ShortcutActionTypes.OpenUrl),
        new StreamDeck.Protocol.ShortcutDefinition("device-only", "Duplicate", ActionType: ShortcutActionTypes.Hotkey)
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
var diagnosticLines = new[]
{
    "2026-01-01 INFO Agent listening on port 8765",
    "2026-01-01 WARN USB discovery waiting for adapter",
    "2026-01-01 ERROR USB transport disconnected: IOException",
    "2026-01-01 USB pairing failed: timeout",
    "2026-01-01 pairing approval pending"
};
var allDiagnosticLines = SettingsWindow.FilterDiagnosticLines(diagnosticLines, string.Empty, errorsOnly: false);
AssertEqual(diagnosticLines.Length, allDiagnosticLines.Count, "empty diagnostic filter returns every recent line");
var searchedDiagnosticLines = SettingsWindow.FilterDiagnosticLines(diagnosticLines, "usb", errorsOnly: false);
AssertEqual(3, searchedDiagnosticLines.Count, "diagnostic search is case-insensitive");
AssertContains("WARN", searchedDiagnosticLines[0], "diagnostic search preserves source order");
var errorDiagnosticLines = SettingsWindow.FilterDiagnosticLines(diagnosticLines, string.Empty, errorsOnly: true);
AssertEqual(2, errorDiagnosticLines.Count, "errors-only filter includes error, exception, and failure entries");
AssertContains("failed", errorDiagnosticLines[1], "errors-only filter recognizes failed operations");
var combinedDiagnosticLines = SettingsWindow.FilterDiagnosticLines(diagnosticLines, "USB", errorsOnly: true);
AssertEqual(2, combinedDiagnosticLines.Count, "diagnostic search and errors-only filters compose");
AssertContains("IOException", combinedDiagnosticLines[0], "error filter retains transport exception details");
var warningAndErrorLines = SettingsWindow.FilterDiagnosticLines(diagnosticLines, string.Empty, "warnings & errors");
AssertEqual(3, warningAndErrorLines.Count, "warnings & errors filter includes both warning and error entries");
var infoLines = SettingsWindow.FilterDiagnosticLines(diagnosticLines, string.Empty, "info");
AssertEqual(2, infoLines.Count, "info filter excludes warnings and errors");
AssertContains("listening", infoLines[0], "info filter retains informational entries");
AssertContains("pending", infoLines[1], "info filter retains second informational entry");
AssertEqual("just now", AgentStatusFormatter.FormatRelativeTime(DateTimeOffset.UtcNow), "relative time just now");
AssertEqual("5m ago", AgentStatusFormatter.FormatRelativeTime(DateTimeOffset.UtcNow.AddMinutes(-5)), "relative time minutes ago");
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
AssertEqual((ushort)0x5B, WindowsHotkeySender.ResolveVirtualKey("WIN"), "hotkey Windows key mapping");
AssertEqual((ushort)0xB3, WindowsHotkeySender.ResolveVirtualKey("MEDIA_PLAY_PAUSE"), "hotkey media play/pause mapping");
AssertTrue(WindowsHotkeySender.IsLockWorkstationHotkey([WindowsHotkeySender.ResolveVirtualKey("WIN"), WindowsHotkeySender.ResolveVirtualKey("L")]), "Win+L is routed through LockWorkStation instead of SendInput");
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
AssertTrue(!SettingsWindow.ShouldRebuildPageOnStateChanged("Pairing"), "telemetry must not rebuild Pairing and drop selection");
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

await RunOnStaAsync(async () =>
{
AssertEqual(Color.FromArgb(246, 247, 249).ToArgb(), SettingsWindow.MapClientThemeColor(Color.FromArgb(13, 15, 20), "light").ToArgb(), "light theme maps the client background");
AssertEqual(Color.FromArgb(13, 15, 20).ToArgb(), SettingsWindow.MapClientThemeColor(Color.FromArgb(246, 247, 249), "dark").ToArgb(), "dark theme restores the client background");
AssertEqual(Color.FromArgb(246, 247, 249).ToArgb(), SettingsWindow.GetClientThemeBackground("light").ToArgb(), "light theme initializes the content host before attach");
AssertEqual(Color.FromArgb(0x12, 0x34, 0x56).ToArgb(), SettingsWindow.ResolveClientAccentForTests("#123456").ToArgb(), "client app follows configured accent color");
AssertEqual(Color.FromArgb(0x65, 0x43, 0x21).ToArgb(), SettingsWindow.ResolveClientBackgroundForTests("dark", "#654321").ToArgb(), "client app follows configured background color");
AssertEqual(SettingsWindow.GetClientThemeBackground("light").ToArgb(), SettingsWindow.ResolveClientBackgroundForTests("light", string.Empty).ToArgb(), "light client theme keeps light default background when device color is unset");
AssertTrue(SettingsWindow.GetNavigationWidth() >= 180, "settings navigation uses a wider hierarchy rail");
AssertTrue(SettingsWindow.GetNavigationButtonSize().Height >= 42, "settings navigation buttons have comfortable target height");
AssertTrue(SettingsWindow.GetClientContentPadding().Left >= 20, "settings content uses spacious page margins");
AssertEqual(new Size(900, 620), SettingsWindow.GetMinimumWindowSizeForTests(), "settings window keeps the validated 900x620 minimum size");
AssertTrue(SettingsWindow.ShouldPreserveClientThemeBackColor(SettingsWindow.DeviceColorPreviewTagForTests), "device color preview is not remapped by client theme");
var minimumClientWidth = 900 - SettingsWindow.GetNavigationWidth() - SettingsWindow.GetClientContentPadding().Horizontal;
var shortcutActionButtonWidths = new[] { "Add", "Edit", "Delete", "Move up", "Move down", "Move to…", "Import full config", "Export full config" }
    .Select(SettingsWindow.MeasureSettingsActionButtonWidth)
    .ToArray();
AssertTrue(SettingsWindow.CalculateActionBarHeight(minimumClientWidth, shortcutActionButtonWidths) > SettingsWindow.GetActionButtonHeight(), "shortcuts action bar grows when controls wrap at minimum width");
AssertTrue(SettingsWindow.CalculateActionBarHeight(minimumClientWidth, shortcutActionButtonWidths) <= 156, "wrapped shortcuts action bar remains accessible at minimum height");
AssertTrue(SettingsWindow.PageLayoutHasScrollableBodyAndAccessibleFooterForTests(900 - SettingsWindow.GetNavigationWidth(), 620 - SettingsWindow.GetClientContentPadding().Vertical, 520, [SettingsWindow.MeasureSettingsActionButtonWidth("Reconnect device")]), "overview page keeps its body scrollable instead of clipping its footer frame at minimum size");
var orderedShortcuts = new List<string> { "one", "two", "three", "four" };
SettingsWindow.MoveShortcutInMemory(orderedShortcuts, 0, SettingsWindow.NormalizeShortcutTargetIndex(0, 3, orderedShortcuts.Count));
AssertEqual("two,three,one,four", string.Join(',', orderedShortcuts), "shortcut Move to position reorders by user-visible one-based position");
AssertTrue(SettingsWindow.GetAppearanceSliderTrackWidth(minimumClientWidth) <= 300, "appearance sliders shrink below old fixed width at minimum window size");
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
    using var pickerBitmap = new Bitmap(colorPicker.Width, colorPicker.Height);
    colorPicker.DrawToBitmap(pickerBitmap, new Rectangle(Point.Empty, colorPicker.Size));
    AssertTrue(BitmapContainsColor(pickerBitmap, Color.FromArgb(0x12, 0xAB, 0xEF)), "appearance color picker visually renders the configured swatch color");
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
Directory.CreateDirectory("artifacts/ui-review");
var shortcutFixtureStore = new AgentConfigurationStore(Path.GetFullPath("artifacts/ui-review/shortcut-regression.json"));
shortcutFixtureStore.Save(AgentConfiguration.CreateDefault() with { Shortcuts = new[]
{
    new ShortcutDefinition { Id = "test-first", Name = "First shortcut", Icon = "browser", Action = new ShortcutActionDefinition { Type = ShortcutActionTypes.OpenUrl, Url = "https://example.com/first" } },
    new ShortcutDefinition { Id = "test-second", Name = "Second shortcut", Icon = "browser", Action = new ShortcutActionDefinition { Type = ShortcutActionTypes.OpenUrl, Url = "https://example.com/second" } }
}});
var catalogFixture = ShortcutCatalog.Create(new ShortcutConfiguration { Shortcuts = shortcutFixtureStore.LoadOrDefault().Shortcuts! });
catalogFixture.Replace(new ShortcutConfiguration { Shortcuts = shortcutFixtureStore.LoadOrDefault().Shortcuts!.Reverse().ToArray() });
AssertEqual("test-second", catalogFixture.DeviceConfiguration.Shortcuts[0].Id, "live catalog replacement updates the device order atomically");
AssertTrue(catalogFixture.TryGetAction("test-second", out var liveAction) && liveAction.Url == "https://example.com/second", "live catalog replacement keeps executable actions available");
var brightnessRevisionConfig = new AppearanceConfiguration { BrightnessRevision = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" };
var hydrationSettings = ProtocolStateMapper.CreateDeviceSettings("hydrate", brightnessRevisionConfig);
AssertTrue(ProtocolSerializer.Parse(hydrationSettings).IsSuccess, "new settings parse successfully");
AssertContains("\"applyBrightness\":false", hydrationSettings, "hydration preserves local brightness unless revision changes");
AssertContains("\"brightnessRevision\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"", hydrationSettings, "hydration carries saved brightness revision");
var forceSettings = ProtocolStateMapper.CreateDeviceSettings("save", brightnessRevisionConfig, applyBrightness: true);
AssertContains("\"applyBrightness\":true", forceSettings, "explicit sync applies brightness");
AssertTrue(!ProtocolSerializer.Parse("""{"protocol":1,"type":"device.settings","data":{"version":1,"brightnessRevision":"bad"}}""").IsSuccess, "invalid brightness revision is rejected");
DesktopBugRegressionTests.Run();
DesktopViewportTests.Run();
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
using (var hotkeyEditor = new ShortcutEditorDialog(new ShortcutDefinition { Action = new ShortcutActionDefinition { Type = ShortcutActionTypes.Hotkey, Keys = ["CTRL", "SHIFT", "R"] } }))
{
    var hotkeyControls = FlattenControls(hotkeyEditor).ToArray();
    var hotkeyTarget = hotkeyControls.OfType<System.Windows.Forms.TextBox>().First(t => t.AccessibleName == "Shortcut target");
    AssertEqual("CTRL+SHIFT+R", hotkeyTarget.Text, "hotkey target is populated from keys");
    var clearBtn = hotkeyControls.OfType<System.Windows.Forms.Button>().First(b => b.AccessibleName == "Clear hotkey combination");
    hotkeyEditor.ClearHotkey();
    AssertEqual("", hotkeyTarget.Text, "Clear button clears hotkey combination");
}
SynchronizationContext.SetSynchronizationContext(previousShortcutEditorContext);

using (var picker = new ShortcutIconPicker("lock", null, null))
{
    picker.StartPosition = FormStartPosition.Manual;
    picker.Location = new Point(-2000, -2000);
    picker.Show();
    Application.DoEvents();
    picker.PerformLayout();
    Application.DoEvents();

    AssertEqual("lock", picker.IconKey, "picker initial key is lock");
    AssertEqual(null, picker.IconData, "picker initial data is null");

    // Test switching built-in icon
    var terminalButton = FlattenControls(picker).OfType<Button>().Single(b => b.Text == "terminal");
    terminalButton.PerformClick();
    Application.DoEvents();
    AssertEqual("terminal", picker.IconKey, "picker switches to terminal built-in icon");

    // Test failed URL download preserves previous icon state
    var urlBox = FlattenControls(picker).OfType<TextBox>().Single(t => t.AccessibleName == "Image URL");
    var loadButton = FlattenControls(picker).OfType<Button>().Single(b => b.Text == "Load preview");
    urlBox.Text = "https://invalid-non-existent-domain-test.local/icon.png";
    loadButton.PerformClick();
    while (!loadButton.Enabled) Application.DoEvents();
    AssertEqual("terminal", picker.IconKey, "picker preserves previous icon key on failed download");
    AssertEqual(null, picker.IconData, "picker preserves null icon data on failed download");

    // Capture visual artifact for UI review
    using var render = new Bitmap(picker.Width, picker.Height);
    picker.DrawToBitmap(render, new Rectangle(Point.Empty, picker.Size));
    Directory.CreateDirectory("artifacts/ui-review");
    render.Save("artifacts/ui-review/icon-picker.png");
    picker.Close();
}
});
var liveOverview = SettingsWindow.CreateLiveInfoValues("Overview", new AgentStatusSnapshot(
    "connected", "device-1", "CYD", "192.168.1.8:1234", 8765, 5000, "live", ["192.168.1.10"]));
AssertEqual("Connected", liveOverview["CYD"], "overview live device state");
AssertEqual("Connected", liveOverview["Agent service"], "overview live websocket state");
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
var settingsConstructor = typeof(SettingsWindow).GetConstructor([
    typeof(AgentState),
    typeof(WindowsAgentRuntime),
    typeof(WindowsStartupService),
    typeof(IPairingApprovalService),
    typeof(Icon),
    typeof(AgentConfigurationStore)]);
AssertTrue(settingsConstructor is not null, "settings window is wired with native pairing service");

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

await TestWebSocketLoopbackBidirectionalPairingAsync();

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

static async Task TestWebSocketLoopbackBidirectionalPairingAsync()
{
    var state = new AgentState();
    var pairing = new PairingApprovalService(Path.Combine(Path.GetTempPath(), $"streamdeck-ws-e2e-pairing-{Guid.NewGuid():N}.json"));
    await using var listener = new WindowsAgentWebSocketServer(
        new TransportOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            WebSocketPath = "/ws",
            AllowRemoteClients = false
        },
        CreateTestDispatcher(),
        pairing,
        state,
        new AppearanceConfiguration());

    await listener.StartAsync();
    AssertTrue(listener.Port > 0, "loopback websocket listener uses an ephemeral port");
    AssertTrue(listener.Port != 8765, "loopback websocket acceptance test does not bind the default agent port");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    const string deviceId = "ws-e2e-device";

    using var firstSocket = new ClientWebSocket();
    await firstSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{listener.Port}/ws"), timeout.Token);
    await SendStringAsync(firstSocket, ProtocolSerializer.Serialize(
        ProtocolConstants.MessageTypes.Hello,
        requestId: null,
        new HelloMessage(deviceId, "Loopback CYD", "esp32", [])), timeout.Token);

    var pendingReply = await ReceiveStringAsync(firstSocket, timeout.Token);
    AssertContains("reconnecting", pendingReply, "loopback websocket pending hello response");
    var pending = await pairing.ListPendingAsync(timeout.Token);
    AssertEqual(1, pending.Count, "loopback websocket pending pairing count");
    AssertEqual(deviceId, pending[0].DeviceId, "loopback websocket pending device id");

    AssertTrue(await pairing.ApproveAsync(deviceId, timeout.Token), "loopback websocket pairing approval succeeds");
    var approvedMessages = await ReceiveUntilTypesAsync(firstSocket, [
        ProtocolConstants.MessageTypes.PairingToken,
        ProtocolConstants.MessageTypes.Hello,
        ProtocolConstants.MessageTypes.ConnectionState,
        ProtocolConstants.MessageTypes.DeviceSettings
    ], timeout.Token);
    var pairingTokenFrame = approvedMessages.Single(message => MessageType(message) == ProtocolConstants.MessageTypes.PairingToken);
    using var pairingTokenDocument = JsonDocument.Parse(pairingTokenFrame);
    var bearerToken = pairingTokenDocument.RootElement.GetProperty("data").GetProperty("token").GetString();
    AssertTrue(!string.IsNullOrWhiteSpace(bearerToken), "loopback websocket approval returns bearer token in memory");
    AssertEqual(ProtocolConstants.MessageTypes.PairingToken, MessageType(approvedMessages[0]), "pairing token precedes agent hello after approval");
    AssertEqual(ProtocolConstants.MessageTypes.Hello, MessageType(approvedMessages[1]), "agent hello follows pairing token after approval");
    AssertTrue(approvedMessages.Any(message => message.Contains("\"state\":\"connected\"", StringComparison.Ordinal)), "approved loopback websocket reports connected");

    await firstSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect", timeout.Token);

    using var reconnectSocket = new ClientWebSocket();
    await reconnectSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{listener.Port}/ws?token={Uri.EscapeDataString(bearerToken!)}"), timeout.Token);
    await SendStringAsync(reconnectSocket, ProtocolSerializer.Serialize(
        ProtocolConstants.MessageTypes.Hello,
        requestId: null,
        new HelloMessage(deviceId, "Loopback CYD", "esp32", [])), timeout.Token);
    var reconnectMessages = await ReceiveUntilTypesAsync(reconnectSocket, [
        ProtocolConstants.MessageTypes.Hello,
        ProtocolConstants.MessageTypes.ConnectionState,
        ProtocolConstants.MessageTypes.DeviceSettings
    ], timeout.Token);
    AssertTrue(!reconnectMessages.Any(message => MessageType(message) == ProtocolConstants.MessageTypes.PairingToken), "websocket bearer reconnect does not echo pairing token");
    AssertTrue(reconnectMessages.Any(message => message.Contains("\"state\":\"connected\"", StringComparison.Ordinal)), "websocket bearer reconnect reports connected");

    var hostToDeviceMessage = ProtocolStateMapper.CreateDeviceSettings("ws-settings-e2e", new AppearanceConfiguration());
    AssertTrue(await listener.SendToActiveDeviceAsync(hostToDeviceMessage, timeout.Token), "loopback websocket sends host frame to active device");
    var deviceReceived = await ReceiveUntilContainsAsync(reconnectSocket, "ws-settings-e2e", timeout.Token);
    AssertContains("ws-settings-e2e", deviceReceived, "device receives host-to-device websocket frame");

    await SendStringAsync(reconnectSocket, ProtocolSerializer.Serialize(
        ProtocolConstants.MessageTypes.Ping,
        requestId: "ws-ping-e2e",
        new PingMessage(1234)), timeout.Token);
    var pong = await ReceiveStringAsync(reconnectSocket, timeout.Token);
    AssertContains(ProtocolConstants.MessageTypes.Pong, pong, "device-to-agent websocket ping receives pong");

    await reconnectSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", timeout.Token);
}

static async Task TestUsbLineProtocolTransportAsync()
{
    var written = new List<string>();
    var stream = new FakeUsbDuplexStream(written);
    UsbLineProtocolTransport? transport = null;
    var sink = new UsbProtocolSink((message, token) => transport!.SendFrameAsync(message, token));
    var session = new DeviceProtocolSession(CreateTestDispatcher(), new RejectingDeviceAuthorizer(), sink);
    transport = new UsbLineProtocolTransport(stream, session, sink, maxFrameBytes: 256);

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var run = transport.RunAsync(cts.Token);
    await stream.InjectAsync(Encoding.UTF8.GetBytes("{\"protocol\":1,\"type\":\"media.command\",\"requestId\":\"blocked\",\"data\":{\"command\":\"next\"}}\r\n"));
    await WaitUntilAsync(() => written.Any(message => message.Contains("unauthorized", StringComparison.Ordinal)), cts.Token);
    AssertTrue(written.Any(message => message.Contains("ack", StringComparison.Ordinal)), "usb pre-auth commands fail closed with ack");

    await stream.InjectAsync(Encoding.UTF8.GetBytes(new string('x', 300) + "\n"));
    await WaitUntilAsync(() => written.Any(message => message.Contains("too large", StringComparison.Ordinal)), cts.Token);
    await stream.InjectAsync([0xC3, 0x28, (byte)'\n']);
    await WaitUntilAsync(() => written.Any(message => message.Contains("valid UTF-8", StringComparison.Ordinal)), cts.Token);
    await transport.SendFrameAsync("{}", cts.Token);
    AssertTrue(written.Any(message => message == "{}\r\n"), "usb outgoing frames are CRLF delimited");
    await Task.WhenAll(Enumerable.Range(0, 16).Select(index => transport.SendFrameAsync($"{{\"index\":{index}}}", cts.Token).AsTask()));
    AssertEqual(16, written.Count(message => message.StartsWith("{\"index\":", StringComparison.Ordinal) && message.EndsWith("}\r\n", StringComparison.Ordinal)), "usb outgoing writes are serialized as complete frames");
    await transport.DisposeAsync();
    try { await run; } catch (ObjectDisposedException) { } catch (OperationCanceledException) { }
}

static async Task TestUsbCoverSenderAsync()
{
    var coverCacheDirectory = Path.Combine(Path.GetTempPath(), $"streamdeck-usb-covers-{Guid.NewGuid():N}");
    var coverId = new string('c', 64);
    Directory.CreateDirectory(Path.Combine(coverCacheDirectory, "covers"));
    var coverBytes = Enumerable.Range(0, UsbCoverSender.MaxRawChunkBytes + 17).Select(index => (byte)(index % 251)).ToArray();
    await File.WriteAllBytesAsync(Path.Combine(coverCacheDirectory, "covers", $"{coverId}.jpg"), coverBytes);
    var sink = new MemoryProtocolSink();
    var sender = new UsbCoverSender(coverCacheDirectory, TimeSpan.Zero);

    AssertTrue(await sender.SendCoverAsync(coverId, sink), "usb cover sender returns true for cache hit");
    AssertEqual(3, sink.Messages.Count, "usb cover sender sends bounded chunks and complete");
    AssertTrue(!sink.Messages.Any(message => message.Contains("\\u002B", StringComparison.OrdinalIgnoreCase)), "USB base64 uses literal plus characters understood by firmware");
    using var first = JsonDocument.Parse(sink.Messages[0]);
    AssertEqual("cover.chunk", first.RootElement.GetProperty("type").GetString(), "usb cover chunk type");
    AssertEqual(coverId, first.RootElement.GetProperty("data").GetProperty("coverId").GetString(), "usb cover chunk cover id");
    AssertEqual(0, first.RootElement.GetProperty("data").GetProperty("offset").GetInt32(), "usb cover chunk offset");
    AssertEqual(coverBytes.Length, first.RootElement.GetProperty("data").GetProperty("total").GetInt32(), "usb cover chunk total");
    AssertTrue(Convert.FromBase64String(first.RootElement.GetProperty("data").GetProperty("data").GetString()!).Length <= UsbCoverSender.MaxRawChunkBytes, "usb cover chunk raw size bound");
    using var complete = JsonDocument.Parse(sink.Messages[^1]);
    AssertEqual("cover.complete", complete.RootElement.GetProperty("type").GetString(), "usb cover complete type");
    var repeatedFrames = new List<string>();
    var activeSink = new UsbDeviceRuntime.ActiveUsbSink(new AgentState(), coverCacheDirectory,
        message => { repeatedFrames.Add(message); return ValueTask.CompletedTask; }, _ => { });
    var update = ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.MediaUpdate, null,
        new MediaUpdateMessage("Title", "Artist", "Album", true, 0, 1000, coverId, null));
    await activeSink.TrySendCoverForMediaUpdateAsync(update);
    await activeSink.TrySendCoverForMediaUpdateAsync(update);
    AssertEqual(1, repeatedFrames.Count(message => message.Contains("cover.complete", StringComparison.Ordinal)), "unchanged artwork transfers only once per USB session");
    var cacheFrames = new List<string>();
    var cachedSink = new UsbDeviceRuntime.ActiveUsbSink(new AgentState(), coverCacheDirectory,
        message => { cacheFrames.Add(message); return ValueTask.CompletedTask; }, _ => { });
    cachedSink.TrackIncoming(ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.Hello, null,
        new HelloMessage("cached-device", "CYD", "esp32", ["cover-cache-request"])));
    await cachedSink.SendUpdateAsync(update);
    await cachedSink.SendUpdateAsync(update);
    AssertEqual(0, cacheFrames.Count(message => message.Contains("cover.chunk", StringComparison.Ordinal)), "SD cache hit sends metadata without artwork bytes");
    await cachedSink.SendRequestedCoverAsync(new CoverRequestMessage(new string('d', 64)));
    AssertEqual(0, cacheFrames.Count(message => message.Contains("cover.complete", StringComparison.Ordinal)), "stale cache request cannot transfer unrelated artwork");
    await cachedSink.SendRequestedCoverAsync(new CoverRequestMessage(coverId));
    await cachedSink.SendRequestedCoverAsync(new CoverRequestMessage(coverId));
    AssertEqual(1, cacheFrames.Count(message => message.Contains("cover.complete", StringComparison.Ordinal)), "cache miss requests one transfer and duplicate requests are deduplicated");
    await cachedSink.SendUpdateAsync(update);
    AssertEqual(1, cacheFrames.Count(message => message.Contains("cover.complete", StringComparison.Ordinal)), "metadata after cached artwork does not retransmit it");
    Directory.Delete(coverCacheDirectory, recursive: true);
}

static async Task TestUsbDeviceRuntimeHandshakeAsync()
{
    var written = new List<string>();
    var stream = new FakeUsbDuplexStream(written);
    var reconnectWritten = new List<string>();
    var reconnectStream = new FakeUsbDuplexStream(reconnectWritten);
    var streams = new Queue<IUsbDuplexStream>([stream, reconnectStream]);
    var state = new AgentState();
    state.SetHostListening(8765);
    var pairing = new PairingApprovalService(Path.Combine(Path.GetTempPath(), $"streamdeck-usb-runtime-pairing-{Guid.NewGuid():N}.json"));
    await using var runtime = new UsbDeviceRuntime(
        () => streams.Dequeue(),
        CreateTestDispatcher(),
        pairing,
        state,
        new AppearanceConfiguration { Brightness = 42 },
        new DeviceShortcutConfiguration([new DeviceShortcutDefinition("docs", "Docs", "browser", ShortcutDisplayModes.Both)]),
        Path.Combine(Path.GetTempPath(), $"streamdeck-usb-runtime-covers-{Guid.NewGuid():N}"),
        initialAudioProvider: () => new AudioSnapshot(31, false));

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await runtime.StartAsync(timeout.Token);
    await stream.InjectAsync(Encoding.UTF8.GetBytes(ProtocolSerializer.Serialize(
        ProtocolConstants.MessageTypes.Hello,
        requestId: null,
        new HelloMessage("usb-device", "USB CYD", "esp32", [])) + "\r\n"));

    await WaitUntilAsync(() => written.Any(message => message.Contains("reconnecting", StringComparison.Ordinal)), timeout.Token);
    var pending = await pairing.ListPendingAsync(timeout.Token);
    AssertEqual(1, pending.Count, "usb runtime creates a pending pairing after hello");
    AssertEqual("usb-device", pending[0].DeviceId, "usb pending device id");
    AssertTrue(await pairing.ApproveAsync("usb-device", timeout.Token), "usb pending device approved");
    await WaitUntilAsync(() => written.Any(message => message.Contains("\"type\":\"connection.state\"", StringComparison.Ordinal) && message.Contains("connected", StringComparison.Ordinal)), timeout.Token);
    await WaitUntilAsync(() => state.Snapshot().HostStatus == "connected", timeout.Token);
    var snapshot = state.Snapshot();
    AssertEqual("usb-device", snapshot.DeviceId, "usb runtime connected device id");
    AssertEqual("USB", snapshot.RemoteAddress, "usb runtime connected route");
    AssertTrue(written.Any(message => message.Contains("\"type\":\"pairing.token\"", StringComparison.Ordinal)), "usb approval sends bearer token before ready state");
    var pairingTokenFrame = written.Single(message => message.Contains("\"type\":\"pairing.token\"", StringComparison.Ordinal));
    using var pairingTokenDocument = JsonDocument.Parse(pairingTokenFrame);
    var bearerToken = pairingTokenDocument.RootElement.GetProperty("data").GetProperty("token").GetString();
    AssertTrue(written.Any(message => message.Contains("\"type\":\"device.settings\"", StringComparison.Ordinal)), "usb approval sends initial device settings");
    AssertTrue(written.Any(message => message.Contains("\"type\":\"shortcuts.update\"", StringComparison.Ordinal)), "usb approval sends initial shortcuts");
    AssertTrue(written.Any(message => message.Contains("\"type\":\"audio.update\"", StringComparison.Ordinal)), "usb approval sends initial audio");

    AssertTrue(await runtime.SendToActiveDeviceAsync(ProtocolStateMapper.CreateDeviceSettings("settings-test", new AppearanceConfiguration()), timeout.Token), "usb active device accepts host command after approval");
    AssertTrue(written.Any(message => message.Contains("settings-test", StringComparison.Ordinal)), "usb active sink sends host command after approval");

    state.MarkDeviceConnected("usb-device", "USB CYD", "192.168.1.80:1234");
    await stream.FailAsync();
    await WaitUntilAsync(() => stream.DisposeCount > 0, timeout.Token);
    await WaitUntilAsync(() => !runtime.IsReady, timeout.Token);
    var wifiSnapshot = state.Snapshot();
    AssertEqual("connected", wifiSnapshot.HostStatus, "usb disconnect preserves simultaneous WiFi connection state");
    AssertEqual("192.168.1.80:1234", wifiSnapshot.RemoteAddress, "usb disconnect preserves simultaneous WiFi remote address");

    await reconnectStream.InjectAsync(Encoding.UTF8.GetBytes(ProtocolSerializer.Serialize(
        ProtocolConstants.MessageTypes.Hello,
        requestId: null,
        new HelloMessage("usb-device", "USB CYD", "esp32", [], bearerToken)) + "\r\n"));
    await WaitUntilAsync(() => reconnectWritten.Any(message => message.Contains("\"type\":\"connection.state\"", StringComparison.Ordinal) && message.Contains("connected", StringComparison.Ordinal)), timeout.Token);
    var echoedTokenFrame = reconnectWritten.Single(message => message.Contains("\"type\":\"pairing.token\"", StringComparison.Ordinal));
    using var echoedTokenDocument = JsonDocument.Parse(echoedTokenFrame);
    AssertEqual(bearerToken, echoedTokenDocument.RootElement.GetProperty("data").GetProperty("token").GetString(), "USB reconnect echoes only the bearer token verified from hello");
}

static async Task TestUsbRuntimeAdvancesFromSilentCandidateToValidEsp32HelloAsync()
{
    var firstWritten = new List<string>();
    var secondWritten = new List<string>();
    var first = new FakeUsbDuplexStream(firstWritten);
    var second = new FakeUsbDuplexStream(secondWritten);
    var opened = new List<string>();
    var pairing = new PairingApprovalService(Path.Combine(Path.GetTempPath(), $"streamdeck-usb-probe-advance-{Guid.NewGuid():N}.json"));
    await using var runtime = new UsbDeviceRuntime(
        () => ["COM5", "COM8"],
        port =>
        {
            opened.Add(port);
            return port == "COM5" ? first : second;
        },
        CreateTestDispatcher(),
        pairing,
        new AgentState(),
        new AppearanceConfiguration(),
        new DeviceShortcutConfiguration([]),
        Path.Combine(Path.GetTempPath(), $"streamdeck-usb-probe-advance-covers-{Guid.NewGuid():N}"),
        helloProbeTimeout: TimeSpan.FromMilliseconds(40));

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    await runtime.StartAsync(timeout.Token);
    await WaitUntilAsync(() => first.DisposeCount > 0 && opened.Contains("COM8", StringComparer.Ordinal), timeout.Token);
    await second.InjectAsync(Encoding.UTF8.GetBytes(CreateUsbHello("usb-probe-device", "esp32") + "\r\n"));
    await WaitUntilAsync(() => secondWritten.Any(message => message.Contains("reconnecting", StringComparison.Ordinal)), timeout.Token);

    AssertEqual("COM5,COM8", string.Join(',', opened.Take(2)), "USB probe advances from silent candidate to next CH340 port");
    var pending = await pairing.ListPendingAsync(timeout.Token);
    AssertEqual("usb-probe-device", pending.Single().DeviceId, "second valid ESP32 hello stays alive for user pairing");
}

static async Task TestUsbRuntimeAdvancesFromPortOpenFailureToValidEsp32HelloAsync()
{
    var secondWritten = new List<string>();
    var second = new FakeUsbDuplexStream(secondWritten);
    var opened = new List<string>();
    var pairing = new PairingApprovalService(Path.Combine(Path.GetTempPath(), $"streamdeck-usb-probe-fault-{Guid.NewGuid():N}.json"));
    await using var runtime = new UsbDeviceRuntime(
        () => ["COM3", "COM5"],
        port =>
        {
            opened.Add(port);
            if (port == "COM3") throw new UnauthorizedAccessException("Access to the port 'COM3' is denied.");
            return second;
        },
        CreateTestDispatcher(),
        pairing,
        new AgentState(),
        new AppearanceConfiguration(),
        new DeviceShortcutConfiguration([]),
        Path.Combine(Path.GetTempPath(), $"streamdeck-usb-probe-fault-covers-{Guid.NewGuid():N}"),
        helloProbeTimeout: TimeSpan.FromMilliseconds(40));

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    await runtime.StartAsync(timeout.Token);
    await WaitUntilAsync(() => opened.Contains("COM5", StringComparer.Ordinal), timeout.Token);
    await second.InjectAsync(Encoding.UTF8.GetBytes(CreateUsbHello("usb-probe-device", "esp32") + "\r\n"));
    await WaitUntilAsync(() => secondWritten.Any(message => message.Contains("reconnecting", StringComparison.Ordinal)), timeout.Token);

    AssertEqual("COM3,COM5", string.Join(',', opened.Take(2)), "USB probe advances when an earlier candidate port throws on open");
    var pending = await pairing.ListPendingAsync(timeout.Token);
    AssertEqual("usb-probe-device", pending.Single().DeviceId, "valid ESP32 hello connects after earlier open failure");
}

static async Task TestUsbRuntimeRejectsWrongRoleHelloAsync()
{
    var wrongWritten = new List<string>();
    var validWritten = new List<string>();
    var wrong = new FakeUsbDuplexStream(wrongWritten);
    var valid = new FakeUsbDuplexStream(validWritten);
    var opened = new List<string>();
    await using var runtime = new UsbDeviceRuntime(
        () => ["COM5", "COM8"],
        port =>
        {
            opened.Add(port);
            return port == "COM5" ? wrong : valid;
        },
        CreateTestDispatcher(),
        new PairingApprovalService(Path.Combine(Path.GetTempPath(), $"streamdeck-usb-probe-role-{Guid.NewGuid():N}.json")),
        new AgentState(),
        new AppearanceConfiguration(),
        new DeviceShortcutConfiguration([]),
        Path.Combine(Path.GetTempPath(), $"streamdeck-usb-probe-role-covers-{Guid.NewGuid():N}"),
        helloProbeTimeout: TimeSpan.FromMilliseconds(200));

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    await runtime.StartAsync(timeout.Token);
    await wrong.InjectAsync(Encoding.UTF8.GetBytes(CreateUsbHello("wrong-role", "desktop") + "\r\n"));
    await WaitUntilAsync(() => wrong.DisposeCount > 0 && opened.Contains("COM8", StringComparer.Ordinal), timeout.Token);
    await valid.InjectAsync(Encoding.UTF8.GetBytes(CreateUsbHello("valid-role", "esp32") + "\r\n"));
    await WaitUntilAsync(() => validWritten.Any(message => message.Contains("reconnecting", StringComparison.Ordinal)), timeout.Token);

    AssertTrue(!wrongWritten.Any(message => message.Contains("connected", StringComparison.Ordinal)), "wrong-role hello does not select a USB candidate");
}

static async Task TestUsbRuntimeHardOverrideRetriesSinglePortAsync()
{
    var first = new FakeUsbDuplexStream([]);
    var second = new FakeUsbDuplexStream([]);
    var opened = new List<string>();
    await using var runtime = new UsbDeviceRuntime(
        () => ["COM7"],
        _ =>
        {
            opened.Add("COM7");
            return opened.Count == 1 ? first : second;
        },
        CreateTestDispatcher(),
        new PairingApprovalService(Path.Combine(Path.GetTempPath(), $"streamdeck-usb-probe-override-{Guid.NewGuid():N}.json")),
        new AgentState(),
        new AppearanceConfiguration(),
        new DeviceShortcutConfiguration([]),
        Path.Combine(Path.GetTempPath(), $"streamdeck-usb-probe-override-covers-{Guid.NewGuid():N}"),
        helloProbeTimeout: TimeSpan.FromMilliseconds(40));

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    await runtime.StartAsync(timeout.Token);
    await WaitUntilAsync(() => opened.Count >= 2, timeout.Token);

    AssertTrue(opened.All(port => port == "COM7"), "hard USB override retries only the configured port");
    AssertEqual(1, first.DisposeCount, "timed-out hard override probe is disposed before retry");
}

static async Task TestUsbRuntimeDisposesTimedOutProbeAsync()
{
    var stream = new FakeUsbDuplexStream([]);
    await using var runtime = new UsbDeviceRuntime(
        () => ["COM5"],
        _ => stream,
        CreateTestDispatcher(),
        new PairingApprovalService(Path.Combine(Path.GetTempPath(), $"streamdeck-usb-probe-timeout-{Guid.NewGuid():N}.json")),
        new AgentState(),
        new AppearanceConfiguration(),
        new DeviceShortcutConfiguration([]),
        Path.Combine(Path.GetTempPath(), $"streamdeck-usb-probe-timeout-covers-{Guid.NewGuid():N}"),
        helloProbeTimeout: TimeSpan.FromMilliseconds(40));

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    await runtime.StartAsync(timeout.Token);
    await WaitUntilAsync(() => stream.DisposeCount > 0, timeout.Token);

    AssertTrue(!runtime.IsReady, "timed-out USB probe is not marked ready");
}

static async Task TestUsbProbeDoesNotConsumeFrameAfterHelloAsync()
{
    var written = new List<string>();
    var stream = new FakeUsbDuplexStream(written);
    await using var runtime = new UsbDeviceRuntime(
        () => ["COM5"],
        _ => stream,
        CreateTestDispatcher(),
        new PairingApprovalService(Path.Combine(Path.GetTempPath(), $"streamdeck-usb-probe-buffer-{Guid.NewGuid():N}.json")),
        new AgentState(),
        new AppearanceConfiguration(),
        new DeviceShortcutConfiguration([]),
        Path.Combine(Path.GetTempPath(), $"streamdeck-usb-probe-buffer-covers-{Guid.NewGuid():N}"),
        helloProbeTimeout: TimeSpan.FromMilliseconds(200));

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    await runtime.StartAsync(timeout.Token);
    var hello = CreateUsbHello("usb-buffer-device", "esp32");
    var followingFrame = ProtocolSerializer.Serialize(
        ProtocolConstants.MessageTypes.MediaCommand,
        requestId: "after-hello",
        new MediaCommandMessage(MediaCommand.Next));
    await stream.InjectAsync(Encoding.UTF8.GetBytes(hello + "\r\n" + followingFrame + "\r\n"));

    await WaitUntilAsync(() => written.Any(message => message.Contains("after-hello", StringComparison.Ordinal)), timeout.Token);
    AssertTrue(written.Any(message => message.Contains("unauthorized", StringComparison.Ordinal)), "frame immediately after probed hello reaches normal USB session");
}

static async Task TestUsbProbeRecoversFromLeadingEmptyLineAndFragmentAsync()
{
    var written = new List<string>();
    var stream = new FakeUsbDuplexStream(written);
    var pairing = new PairingApprovalService(Path.Combine(Path.GetTempPath(), $"streamdeck-usb-probe-recover-{Guid.NewGuid():N}.json"));
    await using var runtime = new UsbDeviceRuntime(
        () => ["COM5"],
        _ => stream,
        CreateTestDispatcher(),
        pairing,
        new AgentState(),
        new AppearanceConfiguration(),
        new DeviceShortcutConfiguration([]),
        Path.Combine(Path.GetTempPath(), $"streamdeck-usb-probe-recover-covers-{Guid.NewGuid():N}"),
        helloProbeTimeout: TimeSpan.FromMilliseconds(500));

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    await runtime.StartAsync(timeout.Token);
    var hello = CreateUsbHello("usb-recovered-device", "esp32");
    await stream.InjectAsync(Encoding.UTF8.GetBytes("\r\n\r\n{\"partial-fragment-mid-transmission\r\n" + hello + "\r\n"));

    await WaitUntilAsync(() => written.Any(message => message.Contains("reconnecting", StringComparison.Ordinal)), timeout.Token);
    var pending = await pairing.ListPendingAsync(timeout.Token);
    AssertEqual("usb-recovered-device", pending.Single().DeviceId, "USB probe recovers from leading empty lines and mid-stream fragments");
}

static string CreateUsbHello(string deviceId, string role)
{
    return ProtocolSerializer.Serialize(
        ProtocolConstants.MessageTypes.Hello,
        requestId: null,
        new HelloMessage(deviceId, "USB CYD", role, []));
}

static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
{
    while (!condition())
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(10, cancellationToken);
    }
}

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
        new SynchronousProgress<FirmwareUpdateProgress>(progressValues.Add),
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

static async Task<List<string>> ReceiveUntilTypesAsync(ClientWebSocket socket, IEnumerable<string> expectedTypes, CancellationToken cancellationToken)
{
    var remaining = new HashSet<string>(expectedTypes, StringComparer.Ordinal);
    var messages = new List<string>();
    while (remaining.Count > 0)
    {
        var message = await ReceiveStringAsync(socket, cancellationToken);
        messages.Add(message);
        remaining.Remove(MessageType(message));
    }

    return messages;
}

static async Task<string> ReceiveUntilContainsAsync(ClientWebSocket socket, string expectedContent, CancellationToken cancellationToken)
{
    while (true)
    {
        var message = await ReceiveStringAsync(socket, cancellationToken);
        if (message.Contains(expectedContent, StringComparison.Ordinal))
        {
            return message;
        }
    }
}

static string MessageType(string message)
{
    using var document = JsonDocument.Parse(message);
    return document.RootElement.GetProperty("type").GetString() ?? string.Empty;
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

static bool BitmapContainsColor(Bitmap bitmap, Color expected)
{
    for (var y = 0; y < bitmap.Height; y++)
    {
        for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (Math.Abs(pixel.R - expected.R) <= 2
                && Math.Abs(pixel.G - expected.G) <= 2
                && Math.Abs(pixel.B - expected.B) <= 2)
            {
                return true;
            }
        }
    }

    return false;
}

static string DescribeControl(System.Windows.Forms.Control control) => string.IsNullOrWhiteSpace(control.AccessibleName)
    ? $"{control.GetType().Name} '{control.Text}'"
    : control.AccessibleName;

static Task RunOnStaAsync(Func<Task> test)
{
    var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var thread = new Thread(() =>
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        using var context = new ApplicationContext();
        using var dispatcher = new Control();
        dispatcher.CreateControl();
        dispatcher.BeginInvoke(async () =>
        {
            try { await test(); completion.TrySetResult(); }
            catch (Exception exception) { completion.TrySetException(exception); }
            finally { context.ExitThread(); }
        });
        Application.Run(context);
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.IsBackground = true;
    thread.Start();
    return completion.Task;
}

static int GetFreeTcpPort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

static async Task TestShortcutIconDownloadLoopbackAsync()
{
    var port = GetFreeTcpPort();
    using var listener = new HttpListener();
    listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    listener.Start();

    var serverCts = new CancellationTokenSource();
    var serverTask = Task.Run(async () =>
    {
        while (!serverCts.Token.IsCancellationRequested && listener.IsListening)
        {
            try
            {
                var context = await listener.GetContextAsync();
                if (context.Request.Url?.AbsolutePath == "/test.png")
                {
                    using var bmp = new Bitmap(16, 16);
                    using var g = Graphics.FromImage(bmp);
                    g.Clear(Color.Blue);
                    using var ms = new MemoryStream();
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    var bytes = ms.ToArray();
                    context.Response.ContentType = "image/png";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes, serverCts.Token);
                    context.Response.Close();
                }
                else
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                }
            }
            catch (Exception) when (serverCts.IsCancellationRequested || !listener.IsListening)
            {
                break;
            }
        }
    });

    try
    {
        var validData = await ShortcutIconCatalog.DownloadAsync($"http://127.0.0.1:{port}/test.png", CancellationToken.None);
        var bytes = Convert.FromBase64String(validData);
        AssertEqual(1728, bytes.Length, "downloaded test.png converts to 1728 bytes RGB565A8");

        var threw404 = false;
        try
        {
            await ShortcutIconCatalog.DownloadAsync($"http://127.0.0.1:{port}/missing.png", CancellationToken.None);
        }
        catch (HttpRequestException)
        {
            threw404 = true;
        }
        AssertTrue(threw404, "missing URL throws HttpRequestException");

        AssertThrows<InvalidDataException>(() =>
            ShortcutIconCatalog.DownloadAsync("ftp://example.com/icon.png", CancellationToken.None).GetAwaiter().GetResult(),
            "non-http URL throws InvalidDataException");
    }
    finally
    {
        serverCts.Cancel();
        listener.Stop();
        try { await serverTask; } catch { }
    }
}

static async Task TestReconnectWithSelectedComPortAsync()
{
    await RunOnStaAsync(async () =>
    {
        var testConfigPath = Path.GetFullPath("artifacts/ui-review/reconnect-test-config.json");
        Directory.CreateDirectory("artifacts/ui-review");
        var testStore = new AgentConfigurationStore(testConfigPath);
        var shortcutA = new ShortcutDefinition { Id = "shortcut-a", Name = "Shortcut A", Icon = "app", Action = new() { Type = ShortcutActionTypes.OpenUrl, Url = "https://example.com" } };
        var shortcutDraftB = new ShortcutDefinition { Id = "shortcut-b", Name = "Shortcut B (Unsaved Draft)", Icon = "browser", Action = new() { Type = ShortcutActionTypes.OpenUrl, Url = "https://draft.com" } };
        testStore.Save(AgentConfiguration.CreateDefault() with
        {
            Server = new ServerConfiguration { UsbPort = "COM3" },
            Shortcuts = [shortcutA]
        });

        var testState = new AgentState();
        var testPairing = new PairingApprovalService(Path.GetFullPath("artifacts/ui-review/reconnect-pairing.json"));
        var testRuntime = new WindowsAgentRuntime(testState, testPairing, new AgentOptions(true), listenPortOverride: 0, configurationStore: testStore);
        await testRuntime.StartAsync();

        using var icon = AppIconFactory.CreateIcon();
        using var window = new SettingsWindow(testState, testRuntime, new WindowsStartupService(new AgentOptions(true)), testPairing, icon, testStore);

        var draftWithUnsavedShortcut = testStore.LoadOrDefault() with
        {
            Shortcuts = [shortcutA, shortcutDraftB],
            Server = new ServerConfiguration { UsbPort = "COM9" }
        };
        window.SetConfigurationDraftForTests(draftWithUnsavedShortcut);
        AssertTrue(window.IsConfigurationDirtyForTests, "Window draft is marked dirty");

        await window.ReconnectWithSelectedPortAsync("COM9");

        var diskConfig = testStore.LoadOrDefault();
        AssertEqual("COM9", diskConfig.Server?.UsbPort, "Reconnect persists selected COM port to disk");
        AssertEqual(1, diskConfig.Shortcuts?.Count ?? 0, "Reconnect does NOT persist unconfirmed shortcut drafts");
        AssertEqual("shortcut-a", diskConfig.Shortcuts![0].Id, "Disk config still has only saved shortcut A");
        AssertEqual(2, window.GetConfigurationDraftForTests()?.Shortcuts?.Count ?? 0, "Window draft in memory preserves uncommitted shortcut B");
        AssertTrue(window.IsConfigurationDirtyForTests, "Window draft remains dirty because shortcut B is still uncommitted");

        await testRuntime.DisposeAsync();

        // Clean test where only port is changed and no other drafts exist:
        var cleanStore = new AgentConfigurationStore(Path.GetFullPath("artifacts/ui-review/reconnect-clean-config.json"));
        cleanStore.Save(AgentConfiguration.CreateDefault() with { Server = new ServerConfiguration { UsbPort = "COM3" } });
        var cleanRuntime = new WindowsAgentRuntime(testState, testPairing, new AgentOptions(true), listenPortOverride: 0, configurationStore: cleanStore);
        await cleanRuntime.StartAsync();
        using var cleanWindow = new SettingsWindow(testState, cleanRuntime, new WindowsStartupService(new AgentOptions(true)), testPairing, icon, cleanStore);
        cleanWindow.SetConfigurationDraftForTests(cleanStore.LoadOrDefault() with { Server = new ServerConfiguration { UsbPort = "COM8" } });
        await cleanWindow.ReconnectWithSelectedPortAsync("COM8");
        AssertEqual("COM8", cleanStore.LoadOrDefault().Server?.UsbPort, "Reconnect persists port when only port is changed");
        AssertTrue(!cleanWindow.IsConfigurationDirtyForTests, "Window draft is no longer dirty when only port was changed");
        await cleanRuntime.DisposeAsync();
    });
}

static async Task TestRuntimePortSwitchingAsync()
{
    await RunOnStaAsync(async () =>
    {
        var testConfigPath = Path.GetFullPath("artifacts/ui-review/runtime-port-switch-config.json");
        Directory.CreateDirectory("artifacts/ui-review");
        var testStore = new AgentConfigurationStore(testConfigPath);
        testStore.Save(AgentConfiguration.CreateDefault() with
        {
            Server = new ServerConfiguration { UsbPort = "COM7" }
        });

        var testState = new AgentState();
        var testPairing = new PairingApprovalService(Path.GetFullPath("artifacts/ui-review/runtime-switch-pairing.json"));
        var testRuntime = new WindowsAgentRuntime(testState, testPairing, new AgentOptions(true), listenPortOverride: 0, configurationStore: testStore);
        await testRuntime.StartAsync();

        AssertEqual("COM7", testRuntime.EffectiveUsbPort, "Initial runtime effective port matches saved COM7");

        using var icon = AppIconFactory.CreateIcon();
        using var window = new SettingsWindow(testState, testRuntime, new WindowsStartupService(new AgentOptions(true)), testPairing, icon, testStore);

        // 1. COM7 -> Automatic:
        await window.ReconnectWithSelectedPortAsync("Automatic");
        var savedAfterAutomatic = testStore.LoadOrDefault();
        AssertEqual(null, savedAfterAutomatic.Server?.UsbPort, "Store has null port after selecting Automatic");
        AssertEqual(null, testRuntime.EffectiveUsbPort, "Runtime reloaded with null effective port, not stuck on COM7");

        // 2. COM7 -> Save COM8:
        await window.ReconnectWithSelectedPortAsync("COM8");
        var savedAfterCom8 = testStore.LoadOrDefault();
        AssertEqual("COM8", savedAfterCom8.Server?.UsbPort, "Store has COM8 after selecting COM8");
        AssertEqual("COM8", testRuntime.EffectiveUsbPort, "Runtime reloaded with COM8, showing no stale activeUsbPortOverride");

        await testRuntime.DisposeAsync();

        // 3. CLI override precedence:
        var cliRuntime = new WindowsAgentRuntime(testState, testPairing, new AgentOptions(true) { UsbPort = "COM1" }, listenPortOverride: 0, configurationStore: testStore);
        await cliRuntime.StartAsync();
        AssertEqual("COM1", cliRuntime.EffectiveUsbPort, "CLI override options.UsbPort takes precedence over stored port");
        await cliRuntime.DisposeAsync();
    });
}

static async Task TestDraftNullComPortResolvesToAutomaticAsync()
{
    await RunOnStaAsync(async () =>
    {
        var testConfigPath = Path.GetFullPath("artifacts/ui-review/draft-automatic-config.json");
        Directory.CreateDirectory("artifacts/ui-review");
        var testStore = new AgentConfigurationStore(testConfigPath);
        testStore.Save(AgentConfiguration.CreateDefault() with
        {
            Server = new ServerConfiguration { UsbPort = "COM7" }
        });

        var testState = new AgentState();
        var testPairing = new PairingApprovalService(Path.GetFullPath("artifacts/ui-review/draft-automatic-pairing.json"));
        var testRuntime = new WindowsAgentRuntime(testState, testPairing, new AgentOptions(true), listenPortOverride: 0, configurationStore: testStore);
        await testRuntime.StartAsync();

        using var icon = AppIconFactory.CreateIcon();
        using var window = new SettingsWindow(testState, testRuntime, new WindowsStartupService(new AgentOptions(true)), testPairing, icon, testStore);

        var draft = testStore.LoadOrDefault() with
        {
            Server = new ServerConfiguration { UsbPort = null }
        };
        window.SetConfigurationDraftForTests(draft);

        await window.ReconnectAsyncForTests();

        var reloaded = testStore.LoadOrDefault();
        AssertEqual(null, reloaded.Server?.UsbPort, "Draft null port correctly preserved as Automatic, not overridden by disk COM7 via ??");
        AssertEqual(null, testRuntime.EffectiveUsbPort, "Runtime reconnected with null port from draft");

        await testRuntime.DisposeAsync();
    });
}

static async Task TestSameIdLabelActionDraftPreservesDirtyAndDoesNotPersistShortcutsAsync()
{
    await RunOnStaAsync(async () =>
    {
        var testConfigPath = Path.GetFullPath("artifacts/ui-review/same-id-diff-label-config.json");
        Directory.CreateDirectory("artifacts/ui-review");
        var testStore = new AgentConfigurationStore(testConfigPath);
        var initialShortcut = new ShortcutDefinition
        {
            Id = "shortcut-1",
            Name = "Original Label",
            Icon = "app",
            Action = new ShortcutActionDefinition { Type = ShortcutActionTypes.OpenUrl, Url = "https://original.com" }
        };
        testStore.Save(AgentConfiguration.CreateDefault() with
        {
            Server = new ServerConfiguration { UsbPort = "COM3" },
            Shortcuts = [initialShortcut]
        });

        var testState = new AgentState();
        var testPairing = new PairingApprovalService(Path.GetFullPath("artifacts/ui-review/same-id-pairing.json"));
        var testRuntime = new WindowsAgentRuntime(testState, testPairing, new AgentOptions(true), listenPortOverride: 0, configurationStore: testStore);
        await testRuntime.StartAsync();

        using var icon = AppIconFactory.CreateIcon();
        using var window = new SettingsWindow(testState, testRuntime, new WindowsStartupService(new AgentOptions(true)), testPairing, icon, testStore);

        var modifiedShortcut = initialShortcut with
        {
            Name = "Modified Label",
            Action = new ShortcutActionDefinition { Type = ShortcutActionTypes.OpenUrl, Url = "https://modified.com" }
        };
        var draft = testStore.LoadOrDefault() with
        {
            Shortcuts = [modifiedShortcut],
            Server = new ServerConfiguration { UsbPort = "COM9" }
        };
        window.SetConfigurationDraftForTests(draft);
        AssertTrue(window.IsConfigurationDirtyForTests, "Window draft is marked dirty initially");

        await window.ReconnectWithSelectedPortAsync("COM9");

        var diskConfig = testStore.LoadOrDefault();
        AssertEqual("COM9", diskConfig.Server?.UsbPort, "Selected COM port is persisted to disk");
        AssertEqual("Original Label", diskConfig.Shortcuts![0].Name, "Shortcut Label on disk was NOT overwritten by port-only save");
        AssertEqual("https://original.com", diskConfig.Shortcuts![0].Action?.Url, "Shortcut Action on disk was NOT overwritten by port-only save");

        AssertTrue(window.IsConfigurationDirtyForTests, "configurationDirty remains TRUE because modified label/action are uncommitted");
        AssertEqual("Modified Label", window.GetConfigurationDraftForTests()?.Shortcuts![0].Name, "In-memory draft preserves unsaved label");
        AssertEqual("https://modified.com", window.GetConfigurationDraftForTests()?.Shortcuts![0].Action?.Url, "In-memory draft preserves unsaved action");

        await testRuntime.DisposeAsync();
    });
}

static async Task TestSaveFailureDoesNotTriggerReconnectAndPreservesDraftAsync()
{
    await RunOnStaAsync(async () =>
    {
        var testConfigPath = Path.GetFullPath("artifacts/ui-review/save-fail-config.json");
        Directory.CreateDirectory("artifacts/ui-review");
        var realFs = new ConfigurationFileSystem();
        realFs.WriteAllText(testConfigPath, AgentConfigurationJson.Serialize(AgentConfiguration.CreateDefault() with
        {
            Server = new ServerConfiguration { UsbPort = "COM3" }
        }));

        var failingFs = new FailingConfigurationFileSystem(realFs, failWrites: true);
        var testStore = new AgentConfigurationStore(testConfigPath, fileSystem: failingFs);

        var testState = new AgentState();
        var testPairing = new PairingApprovalService(Path.GetFullPath("artifacts/ui-review/save-fail-pairing.json"));
        var testRuntime = new WindowsAgentRuntime(testState, testPairing, new AgentOptions(true), listenPortOverride: 0, configurationStore: testStore);
        await testRuntime.StartAsync();

        using var icon = AppIconFactory.CreateIcon();
        using var window = new SettingsWindow(testState, testRuntime, new WindowsStartupService(new AgentOptions(true)), testPairing, icon, testStore);

        var draft = testStore.LoadOrDefault() with
        {
            Server = new ServerConfiguration { UsbPort = "COM9" }
        };
        window.SetConfigurationDraftForTests(draft);
        AssertTrue(window.IsConfigurationDirtyForTests, "Draft is dirty before reconnect");

        string? capturedDialogTitle = null;
        string? capturedDialogMessage = null;
        SettingsWindow.DialogMessageHookForTests = (title, msg) =>
        {
            capturedDialogTitle = title;
            capturedDialogMessage = msg;
        };

        try
        {
            var reconnectCountBefore = testRuntime.ReconnectCount;
            await window.ReconnectWithSelectedPortAsync("COM9");

            AssertEqual(reconnectCountBefore, testRuntime.ReconnectCount, "ReconnectAsync was NOT called after save failure");
            AssertTrue(window.IsConfigurationDirtyForTests, "Draft remains dirty after save failure");
            AssertEqual("COM9", window.GetConfigurationDraftForTests()?.Server?.UsbPort, "Draft in memory is preserved");
            AssertEqual("Configuration save failed", capturedDialogTitle, "Error was routed to UI dialog handler");
        }
        finally
        {
            SettingsWindow.DialogMessageHookForTests = null;
        }

        await testRuntime.DisposeAsync();
    });
}

static async Task TestTrayProductionNotificationAndBalloonClickAsync()
{
    await RunOnStaAsync(async () =>
    {
        var testConfigPath = Path.GetFullPath("artifacts/ui-review/tray-test-config.json");
        Directory.CreateDirectory("artifacts/ui-review");
        var testStore = new AgentConfigurationStore(testConfigPath);
        testStore.Save(AgentConfiguration.CreateDefault());

        var testState = new AgentState();
        var testPairing = new PairingApprovalService(Path.GetFullPath("artifacts/ui-review/tray-pairing.json"));
        var testRuntime = new WindowsAgentRuntime(testState, testPairing, new AgentOptions(true), listenPortOverride: 0, configurationStore: testStore);
        await testRuntime.StartAsync();

        var startup = new WindowsStartupService(new AgentOptions(true));
        using var guard = SingleInstanceGuard.Acquire("Local\\CYD_Deck_Tray_Test_" + Guid.NewGuid().ToString("N"));
        using var context = new WindowsAgentApplicationContext(testState, testRuntime, startup, testPairing, guard, new AgentOptions(true) { StartMinimized = true });

        // 1. Przejście ze stanu listening do pairing pending:
        context.CheckConnectionNotificationForTests(new AgentStatusSnapshot("pairing pending", "cyd-1", "CYD-Dev", "USB", 8765, 5000, "live", []));
        AssertEqual("Pairing", context.GetBalloonTipTargetPageForTests(), "Tray notification targets Pairing page on pairing pending");

        // Kliknięcie w dymek:
        context.TriggerBalloonTipClickedForTests();
        AssertEqual(null, context.GetBalloonTipTargetPageForTests(), "Balloon target page reset after click");
        AssertEqual("Pairing", context.MainWindowForTests?.CurrentPageForTests, "Window navigated to Pairing page on balloon click");

        // 2. Kolejny tick w stanie pairing pending nie spamuje (cel pozostaje null):
        context.CheckConnectionNotificationForTests(new AgentStatusSnapshot("pairing pending", "cyd-1", "CYD-Dev", "USB", 8765, 5000, "live", []));
        AssertEqual(null, context.GetBalloonTipTargetPageForTests(), "Subsequent pairing pending tick produces no balloon notification spam");

        // 3. Połączenie urządzenia:
        context.CheckConnectionNotificationForTests(new AgentStatusSnapshot("connected", "cyd-1", "CYD-Dev", "USB", 8765, 5000, "live", []));
        AssertEqual(null, context.GetBalloonTipTargetPageForTests(), "Connected balloon has null target (opens default page)");

        // Kliknięcie w dymek połączenia:
        context.MainWindowForTests?.ShowPage("Shortcuts");
        context.TriggerBalloonTipClickedForTests();
        AssertEqual("Shortcuts", context.MainWindowForTests?.CurrentPageForTests, "Connected balloon click does not force navigate to Pairing");

        context.Dispose();
        await testRuntime.DisposeAsync();
    });
}

sealed class FailingConfigurationFileSystem : IConfigurationFileSystem
{
    private readonly IConfigurationFileSystem inner;
    private readonly bool failWrites;

    public FailingConfigurationFileSystem(IConfigurationFileSystem inner, bool failWrites = true)
    {
        this.inner = inner;
        this.failWrites = failWrites;
    }

    public bool FileExists(string path) => inner.FileExists(path);
    public void CreateDirectory(string path) => inner.CreateDirectory(path);
    public string ReadAllText(string path) => inner.ReadAllText(path);
    public void WriteAllText(string path, string contents)
    {
        if (failWrites) throw new IOException("Simulated disk write error");
        inner.WriteAllText(path, contents);
    }
    public void ReplaceFile(string sourceFileName, string destinationFileName, string? destinationBackupFileName)
    {
        if (failWrites) throw new IOException("Simulated file replacement error");
        inner.ReplaceFile(sourceFileName, destinationFileName, destinationBackupFileName);
    }
    public void MoveFile(string sourceFileName, string destinationFileName, bool overwrite)
    {
        if (failWrites) throw new IOException("Simulated file move error");
        inner.MoveFile(sourceFileName, destinationFileName, overwrite);
    }
    public void DeleteFile(string path) => inner.DeleteFile(path);
}

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

sealed class FakeUsbDuplexStream(List<string> written) : IUsbDuplexStream
{
    private readonly MemoryStream pending = new();
    private readonly SemaphoreSlim available = new(0);
    private bool disposed;
    private Exception? readFailure;

    public int DisposeCount { get; private set; }

    public async Task InjectAsync(byte[] bytes)
    {
        lock (pending)
        {
            var position = pending.Position;
            pending.Position = pending.Length;
            pending.Write(bytes);
            pending.Position = position;
        }
        available.Release();
        await Task.Yield();
    }

    public async Task CompleteAsync()
    {
        disposed = true;
        available.Release();
        await Task.Yield();
    }

    public async Task FailAsync()
    {
        readFailure = new IOException("simulated USB detach");
        available.Release();
        await Task.Yield();
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        await available.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (readFailure is not null) throw readFailure;
        if (disposed) return 0;
        lock (pending)
        {
            pending.Position = 0;
            var count = pending.Read(buffer.Span);
            var rest = pending.ToArray().AsSpan(count).ToArray();
            pending.SetLength(0);
            pending.Write(rest);
            pending.Position = 0;
            if (pending.Length > 0) available.Release();
            return count;
        }
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        lock (written)
        {
            written.Add(Encoding.UTF8.GetString(buffer.Span));
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        disposed = true;
        available.Release();
        pending.Dispose();
        available.Dispose();
        return ValueTask.CompletedTask;
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

sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
