namespace StreamDeck.WindowsAgent.Shell;

internal static class AgentStatusFormatter
{
    public static string ToUserStatus(string hostStatus)
    {
        return hostStatus switch
        {
            "connected" => "Connected",
            "listening" => "Connecting",
            "pairing pending" => "Pairing pending",
            "starting" => "Connecting",
            var value when value.StartsWith("faulted:", StringComparison.Ordinal) => "Error",
            _ => "Disconnected"
        };
    }

    public static string ToDeviceStatus(AgentStatusSnapshot snapshot)
    {
        return snapshot.HostStatus switch
        {
            "connected" => "Connected",
            "pairing pending" => "Pairing pending",
            "listening" or "starting" => "Disconnected",
            var value when value.StartsWith("faulted:", StringComparison.Ordinal) => "Error",
            _ => "Disconnected"
        };
    }

    public static string FormatLastSeen(AgentStatusSnapshot snapshot)
    {
        if (snapshot.DeviceInfoUpdatedAtUtc is not { } updatedAtUtc)
        {
            return "not connected";
        }

        var elapsed = DateTimeOffset.UtcNow - updatedAtUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalSeconds < 60)
        {
            var seconds = Math.Max(0, (int)elapsed.TotalSeconds);
            return seconds == 1 ? "1 second ago" : $"{seconds} seconds ago";
        }

        var minutes = Math.Max(1, (int)elapsed.TotalMinutes);
        return minutes == 1 ? "1 minute ago" : $"{minutes} minutes ago";
    }
}
