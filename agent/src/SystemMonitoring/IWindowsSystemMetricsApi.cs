namespace StreamDeck.SystemMonitoring;

public interface IWindowsSystemMetricsApi
{
    DateTimeOffset UtcNow { get; }

    bool TryGetSystemTimes(out CpuTimes times);

    bool TryGetMemoryStatus(out MemoryStatus status);

    ulong GetTickCount64();
}
