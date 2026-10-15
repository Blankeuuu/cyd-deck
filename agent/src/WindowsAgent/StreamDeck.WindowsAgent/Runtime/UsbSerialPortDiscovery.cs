using System.IO;
using System.IO.Ports;
using System.Security;
using Microsoft.Win32;

namespace StreamDeck.WindowsAgent.Runtime;

internal static class UsbSerialPortDiscovery
{
    private static readonly string[] UsbEnumRegistryPaths =
    [
        @"SYSTEM\CurrentControlSet\Enum\USB",
        @"SYSTEM\CurrentControlSet\Enum\USBSER"
    ];
    private const string Ch340HardwareIdPrefix = "VID_1A86&PID_7523";

    internal static IReadOnlyList<string> FindConnectedCh340Ports()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            return SelectAvailableCh340Ports(ReadCh340Ports(), SerialPort.GetPortNames());
        }
        catch (Exception exception) when (exception is IOException or SecurityException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    internal static IReadOnlyList<string> SelectAvailableCh340Ports(
        IEnumerable<(string HardwareId, string PortName)> devicePorts,
        IEnumerable<string> availablePorts)
    {
        var available = new HashSet<string>(availablePorts, StringComparer.OrdinalIgnoreCase);
        return devicePorts
            .Where(device => IsCh340HardwareId(device.HardwareId))
            .Select(device => device.PortName)
            .Where(portName => !string.IsNullOrWhiteSpace(portName) && available.Contains(portName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetPortNumber)
            .ThenBy(portName => portName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static bool IsCh340HardwareId(string hardwareId)
    {
        return !string.IsNullOrWhiteSpace(hardwareId)
            && hardwareId.Split('\\', StringSplitOptions.RemoveEmptyEntries)
                .Any(component => component.StartsWith(Ch340HardwareIdPrefix, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<(string HardwareId, string PortName)> ReadCh340Ports()
    {
        var discovered = new List<(string HardwareId, string PortName)>();
        foreach (var registryPath in UsbEnumRegistryPaths)
        {
            using var usbDevices = Registry.LocalMachine.OpenSubKey(registryPath);
            if (usbDevices is null) continue;

            foreach (var deviceName in usbDevices.GetSubKeyNames())
            {
                using var device = usbDevices.OpenSubKey(deviceName);
                if (device is null) continue;

                foreach (var instanceName in device.GetSubKeyNames())
                {
                    using var instance = device.OpenSubKey(instanceName);
                    if (instance is null) continue;

                    var hardwareId = IsCh340HardwareId(deviceName)
                        ? deviceName
                        : IsCh340HardwareId(instanceName)
                            ? instanceName
                            : GetCh340HardwareId(instance);
                    if (hardwareId is null) continue;

                    using var parameters = device.OpenSubKey($@"{instanceName}\Device Parameters");
                    var portName = parameters?.GetValue("PortName") as string
                        ?? instance.GetValue("PortName") as string;
                    if (!string.IsNullOrWhiteSpace(portName))
                    {
                        discovered.Add((hardwareId, portName));
                    }
                }
            }
        }

        return discovered;
    }

    private static string? GetCh340HardwareId(RegistryKey instance)
    {
        foreach (var valueName in new[] { "HardwareID", "CompatibleIDs" })
        {
            switch (instance.GetValue(valueName))
            {
                case string value when IsCh340HardwareId(value):
                    return value;
                case string[] values:
                    var match = values.FirstOrDefault(IsCh340HardwareId);
                    if (match is not null) return match;
                    break;
            }
        }

        return null;
    }

    private static int GetPortNumber(string portName)
    {
        if (!portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
        {
            return int.MaxValue;
        }

        return int.TryParse(portName.AsSpan(3), out var number) ? number : int.MaxValue;
    }
}

internal sealed class UsbPortUnavailableException : IOException
{
}
