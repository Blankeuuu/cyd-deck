namespace StreamDeck.Protocol;

public static class ProtocolConstants
{
    public const int CurrentVersion = 1;

    public static class MessageTypes
    {
        public const string Hello = "hello";
        public const string MediaCommand = "media.command";
        public const string AudioSetVolume = "audio.setVolume";
        public const string AudioToggleMute = "audio.toggleMute";
        public const string ShortcutExecute = "shortcut.execute";
        public const string SystemCommand = "system.command";
        public const string DeviceRestart = "device.restart";
        public const string DeviceFactoryReset = "device.factoryReset";
        public const string DeviceSettings = "device.settings";
        public const string DeviceInfo = "device.info";
        public const string Ping = "ping";
        public const string MediaUpdate = "media.update";
        public const string MediaTimeline = "media.timeline";
        public const string MediaPlayback = "media.playback";
        public const string AudioUpdate = "audio.update";
        public const string SystemStats = "system.stats";
        public const string ShortcutsUpdate = "shortcuts.update";
        public const string ShortcutsReport = "shortcuts.report";
        public const string ConnectionState = "connection.state";
        public const string PairingToken = "pairing.token";
        public const string Ack = "ack";
        public const string Pong = "pong";
    }
}
