using System.Net;
using StreamDeck.Protocol;

namespace StreamDeck.WindowsAgent;

public sealed class AgentState
{
    private readonly object syncRoot = new();
    private string hostStatus = "starting";
    private string? deviceId;
    private string? deviceName;
    private string? remoteAddress;
    private int hostPort = 8765;
    private int dashboardPort;
    private string mode = "live";
    private DeviceInfoMessage? deviceInfo;
    private DateTimeOffset? deviceInfoUpdatedAtUtc;

    public event EventHandler? Changed;

    public AgentStatusSnapshot Snapshot()
    {
        lock (syncRoot)
        {
            return new AgentStatusSnapshot(
                hostStatus,
                deviceId,
                deviceName,
                remoteAddress,
                hostPort,
                dashboardPort,
                mode,
                GetPrivateLanAddresses(),
                deviceInfo,
                deviceInfoUpdatedAtUtc);
        }
    }

    public void SetMode(string value)
    {
        lock (syncRoot)
        {
            mode = value;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetHostListening(int port)
    {
        lock (syncRoot)
        {
            hostPort = port;
            if (hostStatus != "connected")
            {
                hostStatus = "listening";
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetDashboardListening(int port)
    {
        lock (syncRoot)
        {
            dashboardPort = port;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetFaulted(string status)
    {
        lock (syncRoot)
        {
            hostStatus = status;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkDeviceConnected(string id, string name, string? address)
    {
        lock (syncRoot)
        {
            if (!string.Equals(deviceId, id, StringComparison.Ordinal))
            {
                deviceInfo = null;
                deviceInfoUpdatedAtUtc = null;
            }
            deviceId = id;
            deviceName = name;
            remoteAddress = address;
            hostStatus = "connected";
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkDeviceDisconnected(string id)
    {
        lock (syncRoot)
        {
            if (!string.Equals(deviceId, id, StringComparison.Ordinal) || hostStatus != "connected")
            {
                return;
            }

            remoteAddress = null;
            hostStatus = "listening";
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkDeviceForgotten(string id)
    {
        lock (syncRoot)
        {
            if (!string.Equals(deviceId, id, StringComparison.Ordinal))
            {
                return;
            }

            deviceId = null;
            deviceName = null;
            remoteAddress = null;
            hostStatus = "listening";
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkPairingPending(string id, string name, string? address)
    {
        lock (syncRoot)
        {
            deviceId = id;
            deviceName = name;
            remoteAddress = address;
            hostStatus = "pairing pending";
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateDeviceInfo(DeviceInfoMessage info)
    {
        lock (syncRoot)
        {
            deviceInfo = info;
            deviceInfoUpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<string> GetPrivateLanAddresses()
    {
        try
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                .Select(address => address.ToString())
                .Order(StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }
}

public sealed record AgentStatusSnapshot(
    string HostStatus,
    string? DeviceId,
    string? DeviceName,
    string? RemoteAddress,
    int HostPort,
    int DashboardPort,
    string Mode,
    IReadOnlyList<string> LanAddresses,
    DeviceInfoMessage? DeviceInfo = null,
    DateTimeOffset? DeviceInfoUpdatedAtUtc = null);
