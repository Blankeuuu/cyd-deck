using StreamDeck.Audio;
using StreamDeck.Configuration;
using StreamDeck.Media;
using StreamDeck.Protocol;
using StreamDeck.Shortcuts;
using StreamDeck.SystemMonitoring;

namespace StreamDeck.Host;

public static class ProtocolStateMapper
{
    public static string CreateAgentHello(string deviceId = "windows-agent", string name = "CYD-DECK Windows Agent")
    {
        return ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.Hello,
            requestId: null,
            new HelloMessage(deviceId, name, "agent", ["media", "audio", "shortcuts", "system"]));
    }

    public static string CreateMediaUpdate(MediaSnapshot? snapshot, string? coverId = null, string? accent = null)
    {
        var message = snapshot is null
            ? new MediaUpdateMessage(null, null, null, false, 0, null, null, null)
            : new MediaUpdateMessage(
                snapshot.Title,
                snapshot.Artist,
                snapshot.AlbumTitle,
                snapshot.PlaybackState == MediaPlaybackState.Playing,
                ToMilliseconds(snapshot.Timeline.Position),
                ToNullableMilliseconds(snapshot.Timeline.EndTime),
                coverId,
                accent);

        return ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.MediaUpdate, requestId: null, message);
    }

    public static string CreateAudioUpdate(AudioSnapshot snapshot)
    {
        return ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.AudioUpdate,
            requestId: null,
            new AudioUpdateMessage(snapshot.VolumePercent, snapshot.IsMuted));
    }

    public static string CreateSystemStats(SystemMetrics metrics)
    {
        var cpu = Math.Clamp(metrics.CpuUsagePercent, 0, 100);
        var gpu = metrics.GpuUsagePercent.HasValue
            ? (int)Math.Round(Math.Clamp(metrics.GpuUsagePercent.Value, 0, 100))
            : (int)Math.Round(Math.Clamp(cpu * 0.65 + 8, 4, 98));
        var temp = metrics.CpuTemperatureCelsius.HasValue
            ? (int)Math.Round(Math.Clamp(metrics.CpuTemperatureCelsius.Value, 20, 110))
            : (int)Math.Round(Math.Clamp(38 + cpu * 0.42, 35, 92));

        var nowLocal = DateTime.Now;
        var hostLocalMinutes = nowLocal.Hour * 60 + nowLocal.Minute;

        return ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.SystemStats,
            requestId: null,
            new SystemStatsMessage(
                cpu,
                (int)Math.Round(metrics.RamUsagePercent),
                ToSignedBytes(metrics.RamUsedBytes),
                ToSignedBytes(metrics.RamTotalBytes),
                gpu,
                temp,
                hostLocalMinutes,
                nowLocal.ToString("dddd, d MMMM", System.Globalization.CultureInfo.InvariantCulture)));
    }

    public static string CreateShortcutsUpdate(DeviceShortcutConfiguration configuration)
    {
        var shortcuts = configuration.Shortcuts
            .Select(shortcut => new global::StreamDeck.Protocol.ShortcutDefinition(shortcut.Id, shortcut.Name, shortcut.Icon, shortcut.DisplayMode, Color: shortcut.Color, IconData: shortcut.IconData?.Replace('+', '-').Replace('/', '_')))
            .ToArray();

        return ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.ShortcutsUpdate,
            requestId: null,
            new ShortcutsUpdateMessage(shortcuts));
    }

    public static string CreateConnectionState(string state, string? detail)
    {
        return ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.ConnectionState,
            requestId: null,
            new ConnectionStateMessage(state, detail));
    }

    public static string CreatePairingToken(string token)
    {
        return ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.PairingToken,
            requestId: null,
            new PairingTokenMessage(token));
    }

    public static string CreateDeviceRestart(string requestId)
    {
        return ProtocolSerializer.Serialize<DeviceRestartMessage>(
            ProtocolConstants.MessageTypes.DeviceRestart,
            requestId,
            payload: null);
    }

    public static string CreateDeviceFactoryReset(string requestId)
    {
        return ProtocolSerializer.Serialize<DeviceFactoryResetMessage>(
            ProtocolConstants.MessageTypes.DeviceFactoryReset,
            requestId,
            payload: null);
    }

    public static string CreateDeviceSettings(string requestId, DeviceSettingsMessage settings)
    {
        return ProtocolSerializer.Serialize(
            ProtocolConstants.MessageTypes.DeviceSettings,
            requestId,
            settings);
    }

    public static string CreateDeviceSettings(string requestId, AppearanceConfiguration appearance, bool applyBrightness = false)
    {
        return CreateDeviceSettings(
            requestId,
            new DeviceSettingsMessage(
                1,
                appearance.Theme,
                appearance.Accent,
                appearance.Background,
                appearance.ButtonStyle,
                appearance.PageOrder,
                appearance.PageVisibility,
                appearance.Brightness,
                appearance.DimAfterSec,
                appearance.DimBrightness,
                appearance.ScreenOffAfterSec,
                appearance.WakeOnTouch,
                appearance.TileColorScheme,
                appearance.StandbyStyle,
                appearance.ShortcutsLayout,
                applyBrightness,
                appearance.BrightnessRevision));
    }

    private static long ToMilliseconds(TimeSpan value)
    {
        return Math.Max(0, (long)value.TotalMilliseconds);
    }

    private static long? ToNullableMilliseconds(TimeSpan value)
    {
        return value <= TimeSpan.Zero ? null : ToMilliseconds(value);
    }

    private static long ToSignedBytes(ulong value)
    {
        return value > long.MaxValue ? long.MaxValue : (long)value;
    }
}
