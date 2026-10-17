namespace StreamDeck.SystemMonitoring;

public sealed class WindowsSystemMetricsProvider : ISystemMetricsProvider, IDisposable
{
    private readonly IWindowsSystemMetricsApi _api;
    private readonly object _sync = new();
    private readonly IHardwareMetricsReader? _hardware;
    private CpuTimes? _previousCpuTimes;
    private double _lastCpuUsagePercent;

    public WindowsSystemMetricsProvider()
        : this(new WindowsSystemMetricsApi(), OperatingSystem.IsWindows() ? new WindowsHardwareMetricsReader() : null)
    {
    }

    public WindowsSystemMetricsProvider(IWindowsSystemMetricsApi api) : this(api, null) { }

    public WindowsSystemMetricsProvider(IWindowsSystemMetricsApi api, IHardwareMetricsReader? hardware)
    {
        _hardware = hardware;
        _api = api ?? throw new ArgumentNullException(nameof(api));

        if (_api.TryGetSystemTimes(out var initialTimes))
        {
            _previousCpuTimes = initialTimes;
        }
    }

    public SystemMetrics GetSnapshot()
    {
        var cpuUsagePercent = ReadCpuUsagePercent();
        var memory = ReadMemoryStatus();
        var hardware = _hardware?.Read();

        return new SystemMetrics
        {
            TimestampUtc = _api.UtcNow,
            CpuUsagePercent = cpuUsagePercent,
            RamTotalBytes = memory.TotalBytes,
            RamUsedBytes = memory.UsedBytes,
            Uptime = TimeSpan.FromMilliseconds(_api.GetTickCount64()),
            GpuUsagePercent = hardware?.GpuPercent,
            VramTotalBytes = null,
            VramUsedBytes = null,
            CpuTemperatureCelsius = hardware?.CpuTemperature,
            GpuTemperatureCelsius = hardware?.GpuTemperature,
            GpuSource = hardware?.GpuSource,
            TemperatureSource = hardware?.TemperatureSource,
        };
    }

    private double ReadCpuUsagePercent()
    {
        if (!_api.TryGetSystemTimes(out var current))
        {
            return _lastCpuUsagePercent;
        }

        lock (_sync)
        {
            if (_previousCpuTimes is not { } previous)
            {
                _previousCpuTimes = current;
                _lastCpuUsagePercent = 0;
                return _lastCpuUsagePercent;
            }

            var idleDelta = SaturatingDelta(current.Idle, previous.Idle);
            var kernelDelta = SaturatingDelta(current.Kernel, previous.Kernel);
            var userDelta = SaturatingDelta(current.User, previous.User);
            var totalDelta = kernelDelta + userDelta;

            _previousCpuTimes = current;

            if (totalDelta == 0)
            {
                return _lastCpuUsagePercent;
            }

            var busyDelta = totalDelta > idleDelta ? totalDelta - idleDelta : 0;
            _lastCpuUsagePercent = Math.Clamp((double)busyDelta / totalDelta * 100, 0, 100);
            return _lastCpuUsagePercent;
        }
    }

    private MemoryStatus ReadMemoryStatus()
    {
        if (_api.TryGetMemoryStatus(out var status))
        {
            return status;
        }

        return new MemoryStatus(0, 0);
    }

    public void Dispose() => _hardware?.Dispose();

    private static ulong SaturatingDelta(ulong current, ulong previous) =>
        current >= previous ? current - previous : 0;
}
