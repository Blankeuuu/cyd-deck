namespace StreamDeck.Protocol;

public sealed record HelloMessage(string DeviceId, string Name, string Role, string[] Capabilities, string? BearerToken = null, DeviceInfoMessage? DeviceInfo = null);

public sealed record MediaCommandMessage(MediaCommand Command);

public enum MediaCommand
{
    PlayPause,
    Play,
    Pause,
    Previous,
    Next,
    Stop
}

public sealed record AudioSetVolumeMessage(int VolumePercent);

public sealed record ShortcutExecuteMessage(string ShortcutId);

public sealed record SystemCommandMessage(SystemCommand Command);

public enum SystemCommand
{
    Sleep,
    Lock,
    RestartAgent
}

public sealed record DeviceRestartMessage();

public sealed record DeviceFactoryResetMessage();

public sealed record DeviceSettingsMessage(
    int Version,
    string? Theme,
    string? Accent,
    string? Background,
    string? ButtonStyle,
    IReadOnlyList<string>? PageOrder,
    IReadOnlyList<string>? PageVisibility,
    int? Brightness,
    int? DimAfterSec,
    int? DimBrightness,
    int? ScreenOffAfterSec,
    bool? WakeOnTouch,
    string? TileColorScheme = null,
    string? StandbyStyle = null,
    string? ShortcutsLayout = null);

public sealed record DeviceInfoMessage(
    string FirmwareVersion,
    long UptimeSeconds,
    DeviceWifiInfo Wifi,
    string? AgentHost,
    DeviceStorageInfo Sd,
    DeviceHeapInfo Heap,
    DeviceCoverInfo? Cover = null,
    DeviceAudioInfo? Audio = null,
    DevicePerformanceInfo? Performance = null);

public sealed record DevicePerformanceInfo(long LoopMaxUs, long RenderMaxUs, long NetworkMaxUs, long SlowLoops, long WifiDiscoveryCount);

public sealed record DeviceAudioInfo(int VolumePercent, bool Muted);

public sealed record DeviceCoverInfo(int Status, bool Rendered);

public sealed record DeviceWifiInfo(bool Connected, string? Ssid, int? Rssi, string? Ip);

public sealed record DeviceStorageInfo(bool Mounted, bool Ready, long? CapacityBytes, long? FreeBytes);

public sealed record DeviceHeapInfo(long FreeBytes, long MinFreeBytes, long LargestBlockBytes);

public sealed record PingMessage(long SentAtUnixMs);

public sealed record CoverRequestMessage(string CoverId);

public sealed record MediaUpdateMessage(
    string? Title,
    string? Artist,
    string? Album,
    bool Playing,
    long PositionMs,
    long? DurationMs,
    string? CoverId,
    string? Accent);

public sealed record MediaTimelineMessage(long PositionMs, long? DurationMs, long UpdatedAtUnixMs);

public sealed record MediaPlaybackMessage(bool Playing);

public sealed record AudioUpdateMessage(int VolumePercent, bool Muted);

public sealed record SystemStatsMessage(
    double CpuPercent,
    int MemoryPercent,
    long MemoryUsedBytes,
    long MemoryTotalBytes,
    int? GpuPercent = null,
    int? TempCelsius = null,
    int? HostLocalMinutes = null);

public sealed record ShortcutDefinition(string Id, string Label, string? Icon = null, string? DisplayMode = null, string? ActionType = null);

public sealed record ShortcutsUpdateMessage(IReadOnlyList<ShortcutDefinition> Shortcuts);

public sealed record ShortcutsReportMessage(IReadOnlyList<ShortcutDefinition> Shortcuts);

public sealed record ConnectionStateMessage(string State, string? Detail);

public sealed record PairingTokenMessage(string Token);

public sealed record AckMessage(bool Success, ProtocolError? Error);

public sealed record PongMessage(long SentAtUnixMs, long ReceivedAtUnixMs);
