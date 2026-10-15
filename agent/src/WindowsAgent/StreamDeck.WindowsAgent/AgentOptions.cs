namespace StreamDeck.WindowsAgent;

public sealed record AgentOptions(bool MockMode, string? UsbPort = null, bool StartMinimized = false)
{
    public static AgentOptions FromArgs(IReadOnlyList<string> args)
    {
        string? usbPort = null;
        var startMinimized = false;
        for (var index = 0; index < args.Count; index++)
        {
            if (string.Equals(args[index], "--usb-port", StringComparison.OrdinalIgnoreCase)
                && index + 1 < args.Count
                && !string.IsNullOrWhiteSpace(args[index + 1]))
            {
                usbPort = args[index + 1];
                index++;
            }
            else if (string.Equals(args[index], "--minimized", StringComparison.OrdinalIgnoreCase)
                || string.Equals(args[index], "--tray", StringComparison.OrdinalIgnoreCase)
                || string.Equals(args[index], "--startup", StringComparison.OrdinalIgnoreCase))
            {
                startMinimized = true;
            }
        }

        return new AgentOptions(
            args.Any(arg => string.Equals(arg, "--mock", StringComparison.OrdinalIgnoreCase)),
            usbPort,
            startMinimized);
    }
}
