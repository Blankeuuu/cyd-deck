using StreamDeck.Protocol;

namespace StreamDeck.WindowsAgent;

public enum DeviceTransport { None, Usb, WebSocket }
public enum ConfigurationSyncStatus { SavedOffline, Sending, SentUnconfirmed, Confirmed, Failed, Disconnected, TimedOut, Superseded }

public sealed record ConnectionSnapshot(bool IsConnected, string? DeviceId, string? DeviceName,
    DeviceTransport Transport, string? ActiveUsbPort, string? SavedUsbPort,
    string? CliUsbPortOverride, string? RemoteAddress);

public sealed record ConfigurationSyncSnapshot(long Generation, ConfigurationSyncStatus Status, string? Error = null)
{
    public bool CanRetry => Status != ConfigurationSyncStatus.Sending;
}

public sealed partial class AgentState
{
    private readonly Dictionary<string, ConnectionSnapshot> connections = new();
    private ConnectionSnapshot? activeConnection;
    private string? activeSessionId;
    private string? savedUsbPort;
    private string? cliUsbPortOverride;
    private long syncGeneration;
    private ConfigurationSyncSnapshot? synchronization;

    private ConnectionSnapshot BuildConnectionSnapshot() => activeConnection is { } active
        ? active with { SavedUsbPort = savedUsbPort, CliUsbPortOverride = cliUsbPortOverride }
        : new(false, null, null, DeviceTransport.None, null, savedUsbPort, cliUsbPortOverride, null);

    private void SelectActiveConnection()
    {
        var next = connections.Values.LastOrDefault(value => value.Transport == DeviceTransport.Usb)
            ?? connections.Values.LastOrDefault();
        var nextSession = connections.LastOrDefault(pair => ReferenceEquals(pair.Value, next)).Key;
        if (nextSession != activeSessionId)
        {
            deviceInfo = null;
            deviceInfoUpdatedAtUtc = null;
        }
        if (activeSessionId is not null && activeSessionId != nextSession && synchronization is not null)
            synchronization = synchronization with { Status = ConfigurationSyncStatus.Disconnected, Error = null };
        activeSessionId = nextSession;
        activeConnection = next;
        remoteAddress = next?.RemoteAddress;
        if (next is not null) { deviceId = next.DeviceId; deviceName = next.DeviceName; }
        hostStatus = next is null ? "listening" : "connected";
    }

    // Called only after a successful local save, or after reading the persisted configuration at startup.
    public void MarkConfigurationSaved(string? savedPort, string? cliOverride = null)
    {
        lock (syncRoot)
        {
            savedUsbPort = savedPort;
            cliUsbPortOverride = cliOverride ?? cliUsbPortOverride;
            synchronization = new(++syncGeneration, ConfigurationSyncStatus.SavedOffline);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public long BeginConfigurationSync()
    {
        long generation;
        lock (syncRoot)
        {
            generation = ++syncGeneration;
            synchronization = new(generation, ConfigurationSyncStatus.Sending);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return generation;
    }

    public void CompleteConfigurationSync(long generation, ConfigurationSyncStatus status, string? error = null)
    {
        if (status is ConfigurationSyncStatus.Confirmed or ConfigurationSyncStatus.Sending)
            throw new ArgumentException("Protocol 1 does not confirm the complete configuration.", nameof(status));
        lock (syncRoot)
        {
            if (synchronization?.Generation != generation || synchronization.Status != ConfigurationSyncStatus.Sending) return;
            synchronization = new(generation, status, error);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool IsActiveDeviceSession(string sessionId)
    {
        lock (syncRoot) return activeSessionId == sessionId;
    }

    public void UpdateDeviceInfoForSession(string sessionId, DeviceInfoMessage info)
    {
        lock (syncRoot)
        {
            if (sessionId != activeSessionId || !connections.ContainsKey(sessionId)) return;
            deviceInfo = info;
            deviceInfoUpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
