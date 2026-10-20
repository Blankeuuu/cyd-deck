using System.Net;
using StreamDeck.Audio;
using StreamDeck.Media;
using StreamDeck.Protocol;
using StreamDeck.SystemMonitoring;

namespace StreamDeck.WindowsAgent;

public sealed record HostSystemTelemetrySnapshot(
    double CpuPercent,
    double RamPercent,
    ulong RamUsedBytes,
    ulong RamTotalBytes,
    double GpuPercent,
    double TempCelsius,
    IReadOnlyList<double> CpuHistory,
    IReadOnlyList<double> RamHistory,
    IReadOnlyList<double> GpuHistory,
    IReadOnlyList<double> TempHistory, bool GpuAvailable = true, bool TempAvailable = true, string? GpuSource = null, string? TemperatureSource = null);

public sealed partial class AgentState
{
    private const int MaxHistorySamples = 30;
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
    private MediaSnapshot? currentMedia;
    private AudioSnapshot? currentAudio;
    private HostSystemTelemetrySnapshot? systemTelemetry;
    private readonly Queue<double> cpuHistory = new();
    private readonly Queue<double> ramHistory = new();
    private readonly Queue<double> gpuHistory = new();
    private readonly Queue<double> tempHistory = new();

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
                deviceInfoUpdatedAtUtc,
                currentMedia,
                currentAudio,
                systemTelemetry,
                BuildConnectionSnapshot(),
                synchronization);
        }
    }

    public void UpdateHostMedia(MediaSnapshot? snapshot)
    {
        lock (syncRoot)
        {
            currentMedia = snapshot;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateHostAudio(AudioSnapshot? snapshot)
    {
        lock (syncRoot)
        {
            currentAudio = snapshot;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateSystemMetrics(SystemMetrics metrics)
    {
        lock (syncRoot)
        {
            var cpu = Math.Clamp(metrics.CpuUsagePercent, 0, 100);
            var ram = Math.Clamp(metrics.RamUsagePercent, 0, 100);
            var gpu = metrics.GpuUsagePercent.HasValue
                ? Math.Clamp(metrics.GpuUsagePercent.Value, 0, 100)
                : 0;
            var temp = metrics.CpuTemperatureCelsius.HasValue
                ? Math.Clamp(metrics.CpuTemperatureCelsius.Value, 20, 110)
                : 0;

            PushSample(cpuHistory, cpu);
            PushSample(ramHistory, ram);
            if (metrics.GpuUsagePercent.HasValue) PushSample(gpuHistory, gpu); else gpuHistory.Clear();
            if (metrics.CpuTemperatureCelsius.HasValue) PushSample(tempHistory, temp); else tempHistory.Clear();

            systemTelemetry = new HostSystemTelemetrySnapshot(
                cpu,
                ram,
                metrics.RamUsedBytes,
                metrics.RamTotalBytes,
                gpu,
                temp,
                cpuHistory.ToArray(),
                ramHistory.ToArray(),
                gpuHistory.ToArray(),
                tempHistory.ToArray(), metrics.GpuUsagePercent.HasValue, metrics.CpuTemperatureCelsius.HasValue, metrics.GpuSource, metrics.TemperatureSource);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void PushSample(Queue<double> queue, double value)
    {
        if (queue.Count >= MaxHistorySamples)
        {
            queue.Dequeue();
        }

        queue.Enqueue(value);
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

    public void MarkDeviceConnected(string id, string name, string? address,
        DeviceTransport transport = DeviceTransport.None, string? activeUsbPort = null, string? sessionId = null)
    {
        lock (syncRoot)
        {
            transport = transport == DeviceTransport.None ? (address == "USB" ? DeviceTransport.Usb : DeviceTransport.WebSocket) : transport;
            connections[sessionId ?? id] = new(true, id, name, transport, activeUsbPort, savedUsbPort, cliUsbPortOverride, address);
            SelectActiveConnection();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkDeviceDisconnected(string id, string? sessionId = null)
    {
        lock (syncRoot)
        {
            var key = sessionId ?? id;
            if (!connections.TryGetValue(key, out var connection) || connection.DeviceId != id) return;
            connections.Remove(key);
            SelectActiveConnection();
            if (connections.Count == 0 && synchronization is not null)
                synchronization = synchronization with { Status = ConfigurationSyncStatus.Disconnected, Error = null };
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

            foreach (var key in connections.Where(pair => pair.Value.DeviceId == id).Select(pair => pair.Key).ToArray()) connections.Remove(key);
            activeConnection = null;
            deviceInfo = null;
            deviceInfoUpdatedAtUtc = null;
            deviceId = null;
            deviceName = null;
            remoteAddress = null;
            SelectActiveConnection();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkPairingPending(string id, string name, string? address)
    {
        lock (syncRoot)
        {
            if (activeConnection is not null) return;
            deviceInfo = null;
            deviceInfoUpdatedAtUtc = null;
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
    DateTimeOffset? DeviceInfoUpdatedAtUtc = null,
    MediaSnapshot? CurrentMedia = null,
    AudioSnapshot? CurrentAudio = null,
    HostSystemTelemetrySnapshot? SystemTelemetry = null,
    ConnectionSnapshot? Connection = null,
    ConfigurationSyncSnapshot? Synchronization = null);
