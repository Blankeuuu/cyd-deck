using StreamDeck.Protocol;
using StreamDeck.Host;
using StreamDeck.Shortcuts;
using StreamDeck.WindowsAgent;
using StreamDeck.WindowsAgent.Runtime;
using StreamDeck.Configuration;

var count = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    count++;
    Console.WriteLine($"PASS {name}");
}

var state = new AgentState();
state.MarkConfigurationSaved("COM9", "COM7");
Check(state.Snapshot().Connection is { IsConnected: false, SavedUsbPort: "COM9", CliUsbPortOverride: "COM7", ActiveUsbPort: null }, "saved and CLI ports are not active");
state.MarkDeviceConnected("A", "CYD", "10.0.0.2:123", DeviceTransport.WebSocket, sessionId: "ws1");
state.MarkDeviceConnected("A", "CYD", "USB", DeviceTransport.Usb, "COM7", "usb1");
Check(state.Snapshot().Connection is { Transport: DeviceTransport.Usb, ActiveUsbPort: "COM7" }, "USB selected over WebSocket");
state.MarkDeviceDisconnected("A", "ws1");
Check(state.Snapshot().Connection!.IsConnected, "old WebSocket disconnect preserves USB");
state.MarkDeviceConnected("A", "CYD", "USB", DeviceTransport.Usb, "COM7", "usb2");
state.MarkDeviceDisconnected("A", "usb1");
Check(state.Snapshot().Connection!.IsConnected, "old USB disconnect preserves replacement");
state.MarkDeviceConnected("B", "other", "10.0.0.3:456", DeviceTransport.WebSocket, sessionId: "ws2");
Check(state.Snapshot().DeviceId == "A", "background WebSocket cannot overwrite USB identity");
state.MarkDeviceDisconnected("A", "usb2");
Check(state.Snapshot().Connection is { DeviceId: "B", Transport: DeviceTransport.WebSocket, ActiveUsbPort: null }, "fallback picks live WebSocket");
state.MarkDeviceDisconnected("B", "ws2");
Check(state.Snapshot().Connection is { IsConnected: false, ActiveUsbPort: null, Transport: DeviceTransport.None }, "disconnect clears actual transport");

var synchronizer = new ConfigurationSynchronizer(state);
Check(await synchronizer.SendAsync(_ => Task.FromResult(true)), "fake transport accepted configuration");
Check(state.Snapshot().Synchronization!.Status == ConfigurationSyncStatus.SentUnconfirmed, "send is never complete configuration ACK");
await synchronizer.SendAsync(_ => Task.FromResult(false));
Check(state.Snapshot().Synchronization!.Status == ConfigurationSyncStatus.Disconnected, "no target remains retryable");
await synchronizer.SendAsync(_ => throw new IOException("fake failure"));
Check(state.Snapshot().Synchronization is { Status: ConfigurationSyncStatus.Failed, CanRetry: true }, "failure permits retry");
await synchronizer.SendAsync(async token => { await Task.Delay(Timeout.Infinite, token); return true; }, timeout: TimeSpan.FromMilliseconds(30));
Check(state.Snapshot().Synchronization!.Status == ConfigurationSyncStatus.TimedOut, "send deadline");
var oldSend = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var pending = synchronizer.SendAsync(_ => oldSend.Task);
var oldGeneration = state.Snapshot().Synchronization!.Generation;
state.MarkConfigurationSaved("COM8");
var newGeneration = state.Snapshot().Synchronization!.Generation;
oldSend.SetResult(true);
await pending;
Check(newGeneration > oldGeneration && state.Snapshot().Synchronization is { Status: ConfigurationSyncStatus.SavedOffline }, "late send cannot overwrite newer save");
await synchronizer.SendAsync(_ => Task.FromResult(true));
Check(state.Snapshot().Synchronization!.Status == ConfigurationSyncStatus.SentUnconfirmed, "retry succeeds without false confirmation");
var generation = state.BeginConfigurationSync();
state.MarkDeviceConnected("C", "CYD", "USB", DeviceTransport.Usb, "COM8", "c");
state.MarkDeviceDisconnected("C", "c");
state.CompleteConfigurationSync(generation, ConfigurationSyncStatus.SentUnconfirmed);
Check(state.Snapshot().Synchronization!.Status == ConfigurationSyncStatus.Disconnected, "late completion cannot undo disconnect");
using (var cancel = new CancellationTokenSource())
{
    cancel.Cancel();
    try { await synchronizer.SendAsync(token => Task.FromCanceled<bool>(token), cancel.Token); }
    catch (OperationCanceledException) { }
    Check(state.Snapshot().Synchronization!.Status == ConfigurationSyncStatus.Superseded, "cancel supersedes send");
}

var wire = new List<string>();
var usbState = new AgentState();
var usbSink = new UsbDeviceRuntime.ActiveUsbSink(usbState, "unused-cover-cache",
    message => { wire.Add(message); return ValueTask.CompletedTask; }, _ => { }, "COM17");
usbSink.TrackIncoming(ProtocolSerializer.Serialize("hello", null, new HelloMessage("test-device", "CYD test", "esp32", [])));
await usbSink.SendTextAsync(ProtocolStateMapper.CreateShortcutsUpdate(new DeviceShortcutConfiguration([])));
Check(usbState.Snapshot().Synchronization!.Status == ConfigurationSyncStatus.Sending, "USB producer exposes sending after first configuration frame");
await usbSink.SendTextAsync(ProtocolStateMapper.CreateDeviceSettings("settings-test", new AppearanceConfiguration()));
await usbSink.SendTextAsync(ProtocolStateMapper.CreateConnectionState("connected", null));
Check(usbState.Snapshot().Connection is { ActiveUsbPort: "COM17", DeviceId: "test-device", Transport: DeviceTransport.Usb }, "USB producer publishes actual COM and identity");
Check(usbState.Snapshot().Synchronization!.Status == ConfigurationSyncStatus.SentUnconfirmed && wire.Count == 3, "USB hydration aggregates both frames");
var info = new DeviceInfoMessage("old-fw", 1, new DeviceWifiInfo(true, null, null, "10.0.0.5"), null,
    new DeviceStorageInfo(false, false, null, null), new DeviceHeapInfo(1, 1, 1));
usbState.UpdateDeviceInfoForSession(usbSink.SessionId, info);
Check(usbState.Snapshot().DeviceInfo?.Wifi.Ip == "10.0.0.5" && usbState.Snapshot().Connection!.Transport == DeviceTransport.Usb, "WiFi IP does not imply WebSocket transport");
usbState.MarkDeviceConnected("test-device", "CYD test", "USB", DeviceTransport.Usb, "COM17", "replacement");
usbState.UpdateDeviceInfoForSession(usbSink.SessionId, info);
Check(usbState.Snapshot().DeviceInfo is null, "old session telemetry cannot populate replacement");
usbState.MarkPairingPending("other", "other", null);
Check(usbState.Snapshot().DeviceId == "test-device" && usbState.Snapshot().HostStatus == "connected", "pending pairing cannot overwrite authorized USB");
usbState.MarkDeviceForgotten("test-device");
Check(usbState.Snapshot().Connection!.IsConnected == false && usbState.Snapshot().DeviceInfo is null, "forget removes all sessions and telemetry");
using (var canceledSend = new CancellationTokenSource())
{
    canceledSend.Cancel();
    var cancellationSink = new UsbDeviceRuntime.ActiveUsbSink(new AgentState(), "unused-cover-cache",
        _ => throw new InvalidOperationException("legacy send must not run"), _ => { },
        sendWithCancellation: (_, token) => ValueTask.FromCanceled(token));
    var propagated = false;
    try { await cancellationSink.SendTextAsync("{}", canceledSend.Token); }
    catch (OperationCanceledException) { propagated = true; }
    Check(propagated, "USB sink propagates per-attempt cancellation");
}
var beforeAck = usbState.Snapshot().Synchronization;
usbSink.TrackIncoming(ProtocolSerializer.SerializeAck("settings-test", true));
Check(usbState.Snapshot().Synchronization == beforeAck, "old settings ACK cannot confirm complete configuration");
var legacy = new AgentStatusSnapshot("connected", "old", "old", "USB", 1, 2, "mock", []);
Check(legacy.Connection is null && legacy.Synchronization is null, "legacy snapshot constructor remains compatible");
state.MarkConfigurationSaved("COM6");
Check(state.Snapshot().Connection!.CliUsbPortOverride == "COM7", "save without CLI argument retains active launch override");
var older = state.BeginConfigurationSync();
var latest = state.BeginConfigurationSync();
state.CompleteConfigurationSync(older, ConfigurationSyncStatus.Failed);
Check(state.Snapshot().Synchronization is { Status: ConfigurationSyncStatus.Sending } && state.Snapshot().Synchronization!.Generation == latest, "older failure cannot overwrite newer send");
state.CompleteConfigurationSync(latest, ConfigurationSyncStatus.SentUnconfirmed);
// A private in-memory store is mandatory: no default user store, registry, physical ports or live runtime.
var files = new MemoryFiles();
var store = new AgentConfigurationStore("fixture/config.json", fileSystem: files);
store.Save(AgentConfiguration.CreateDefault());
Check(store.Load().Server is not null && files.Writes > 0, "configuration store is explicitly isolated");
await using (var offlineRuntime = new WindowsAgentRuntime(new AgentState(),
    new PairingApprovalService(Path.Combine(Path.GetTempPath(), $"cyd-state-test-{Guid.NewGuid():N}.json")),
    new AgentOptions(true), configurationStore: store))
{
    Check(!await offlineRuntime.SyncSavedConfigurationAsync(), "offline saved sync uses supplied store without starting live services");
}
Console.WriteLine($"StateTests: {count}/{count} PASS");

sealed class MemoryFiles : IConfigurationFileSystem
{
    private readonly Dictionary<string, string> data = new();
    public int Writes { get; private set; }
    public bool FileExists(string path) => data.ContainsKey(path);
    public string ReadAllText(string path) => data[path];
    public void WriteAllText(string path, string contents) { data[path] = contents; Writes++; }
    public void CreateDirectory(string path) { }
    public void ReplaceFile(string sourceFileName, string destinationFileName, string? destinationBackupFileName) => MoveFile(sourceFileName, destinationFileName, true);
    public void DeleteFile(string path) => data.Remove(path);
    public void MoveFile(string source, string destination, bool overwrite) { data[destination] = data[source]; data.Remove(source); }
}
