namespace StreamDeck.SystemMonitoring;

public interface ISystemMetricsProvider
{
    SystemMetrics GetSnapshot();
}
