using System.Text.Json;
using StreamDeck.SystemMonitoring;
using Xunit;

namespace StreamDeck.Host.Tests;

public sealed class MetricsReliabilityTests
{
    [Fact]
    public void MissingHardwareSensorsNeverBecomeSyntheticReadings()
    {
        using var json = JsonDocument.Parse(ProtocolStateMapper.CreateSystemStats(new SystemMetrics
        {
            TimestampUtc = DateTimeOffset.UtcNow, CpuUsagePercent = 50,
            RamUsedBytes = 100, RamTotalBytes = 200, Uptime = TimeSpan.Zero
        }));
        var data = json.RootElement.GetProperty("data");
        Assert.True(!data.TryGetProperty("gpuPercent", out var gpu) || gpu.ValueKind == JsonValueKind.Null);
        Assert.True(!data.TryGetProperty("tempCelsius", out var temp) || temp.ValueKind == JsonValueKind.Null);
    }
}
