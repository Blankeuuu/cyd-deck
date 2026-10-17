using StreamDeck.SystemMonitoring;

var tests = new (string Name, Action Run)[]
{
    ("snapshot contains cpu ram and uptime with optional gpu and temperature fields", SnapshotContainsExpectedValues),
    ("cpu usage is calculated from GetSystemTimes deltas", CpuUsageUsesSystemTimeDeltas),
    ("first cpu sample is controlled when no baseline exists", FirstCpuSampleIsControlled),
    ("GPU aggregation sums shared engines without adding unrelated engines", GpuAggregation),
    ("hardware readings and source labels pass through provider", HardwareReadings),
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}

static void GpuAggregation()
{
    if (!OperatingSystem.IsWindows()) return;
    var value = WindowsHardwareMetricsReader.AggregateGpuEngines(new[] { ("pid_1_luid_a_b_phys_0_eng_0_engtype_3D", 20d), ("pid_2_luid_a_b_phys_0_eng_0_engtype_3D", 30d), ("pid_3_luid_a_b_phys_0_eng_1_engtype_Copy", 40d) });
    AssertEqual(50d, value!.Value, "busiest engine");
    if (WindowsHardwareMetricsReader.AggregateGpuEngines([]).HasValue) throw new Exception("Missing GPU is not a zero reading");
}
static void HardwareReadings()
{
    var api = new FakeWindowsSystemMetricsApi([new(100, 500, 500)], new(8000, 2000), 1);
    using var provider = new WindowsSystemMetricsProvider(api, new FakeHardware()); var snapshot = provider.GetSnapshot();
    AssertEqual(31d, snapshot.GpuUsagePercent!.Value, "real GPU value"); AssertEqual(65d, snapshot.CpuTemperatureCelsius!.Value, "real temperature value"); AssertEqual("CPU sensor", snapshot.TemperatureSource!, "sensor provenance");
}

static void SnapshotContainsExpectedValues()
{
    var api = new FakeWindowsSystemMetricsApi(
        new[]
        {
            new CpuTimes(100, 500, 500),
            new CpuTimes(200, 700, 700),
        },
        new MemoryStatus(16_000, 4_000),
        uptimeMilliseconds: 12_345);

    var provider = new WindowsSystemMetricsProvider(api);
    var snapshot = provider.GetSnapshot();

    AssertEqual(75d, snapshot.CpuUsagePercent, nameof(snapshot.CpuUsagePercent));
    AssertEqual(16_000UL, snapshot.RamTotalBytes, nameof(snapshot.RamTotalBytes));
    AssertEqual(12_000UL, snapshot.RamUsedBytes, nameof(snapshot.RamUsedBytes));
    AssertEqual(75d, snapshot.RamUsagePercent, nameof(snapshot.RamUsagePercent));
    AssertEqual(TimeSpan.FromMilliseconds(12_345), snapshot.Uptime, nameof(snapshot.Uptime));
    AssertNull(snapshot.GpuUsagePercent, nameof(snapshot.GpuUsagePercent));
    AssertNull(snapshot.VramTotalBytes, nameof(snapshot.VramTotalBytes));
    AssertNull(snapshot.VramUsedBytes, nameof(snapshot.VramUsedBytes));
    AssertNull(snapshot.CpuTemperatureCelsius, nameof(snapshot.CpuTemperatureCelsius));
    AssertNull(snapshot.GpuTemperatureCelsius, nameof(snapshot.GpuTemperatureCelsius));
}

static void CpuUsageUsesSystemTimeDeltas()
{
    var api = new FakeWindowsSystemMetricsApi(
        new[]
        {
            new CpuTimes(1_000, 5_000, 5_000),
            new CpuTimes(1_100, 5_400, 5_600),
        },
        new MemoryStatus(8_000, 2_000),
        uptimeMilliseconds: 1);

    var provider = new WindowsSystemMetricsProvider(api);
    var snapshot = provider.GetSnapshot();

    AssertEqual(90d, snapshot.CpuUsagePercent, nameof(snapshot.CpuUsagePercent));
}

static void FirstCpuSampleIsControlled()
{
    var api = new FakeWindowsSystemMetricsApi(
        new[] { new CpuTimes(1_000, 5_000, 5_000) },
        new MemoryStatus(8_000, 8_000),
        uptimeMilliseconds: 1);

    api.FailNextCpuRead = true;

    var provider = new WindowsSystemMetricsProvider(api);
    var snapshot = provider.GetSnapshot();

    AssertEqual(0d, snapshot.CpuUsagePercent, nameof(snapshot.CpuUsagePercent));
}

static void AssertEqual<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
    }
}

static void AssertNull<T>(T? value, string name)
{
    if (value is not null)
    {
        throw new InvalidOperationException($"{name}: expected null, got {value}");
    }
}

internal sealed class FakeWindowsSystemMetricsApi : IWindowsSystemMetricsApi
{
    private readonly Queue<CpuTimes> _cpuTimes;
    private readonly MemoryStatus _memoryStatus;
    private readonly ulong _uptimeMilliseconds;

    public FakeWindowsSystemMetricsApi(IEnumerable<CpuTimes> cpuTimes, MemoryStatus memoryStatus, ulong uptimeMilliseconds)
    {
        _cpuTimes = new Queue<CpuTimes>(cpuTimes);
        _memoryStatus = memoryStatus;
        _uptimeMilliseconds = uptimeMilliseconds;
    }

    public bool FailNextCpuRead { get; set; }

    public DateTimeOffset UtcNow { get; } = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    public bool TryGetSystemTimes(out CpuTimes times)
    {
        if (FailNextCpuRead)
        {
            FailNextCpuRead = false;
            times = default;
            return false;
        }

        if (_cpuTimes.TryDequeue(out times))
        {
            return true;
        }

        return false;
    }

    public bool TryGetMemoryStatus(out MemoryStatus status)
    {
        status = _memoryStatus;
        return true;
    }

    public ulong GetTickCount64() => _uptimeMilliseconds;
}

sealed class FakeHardware : IHardwareMetricsReader { public HardwareMetricsSnapshot Read() => new(31, 65, null, "GPU counter", "CPU sensor"); public void Dispose() {} }
