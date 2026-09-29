namespace StreamDeck.SystemMonitoring;

public sealed record SystemMetrics
{
    public required DateTimeOffset TimestampUtc { get; init; }
    public required double CpuUsagePercent { get; init; }
    public required ulong RamTotalBytes { get; init; }
    public required ulong RamUsedBytes { get; init; }
    public required TimeSpan Uptime { get; init; }
    public double? GpuUsagePercent { get; init; }
    public ulong? VramTotalBytes { get; init; }
    public ulong? VramUsedBytes { get; init; }
    public double? CpuTemperatureCelsius { get; init; }
    public double? GpuTemperatureCelsius { get; init; }

    public double RamUsagePercent =>
        RamTotalBytes == 0 ? 0 : Math.Clamp((double)RamUsedBytes / RamTotalBytes * 100, 0, 100);
}
