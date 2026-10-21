using StreamDeck.SystemMonitoring;

namespace StreamDeck.WindowsAgent.Runtime;

// A failed sensor, subscriber or transport must not terminate subsequent samples.
internal static class SystemMetricsPublisher
{
    public static async Task RunAsync(
        ISystemMetricsProvider provider,
        TimeSpan interval,
        Action<SystemMetrics> sampled,
        Func<SystemMetrics, CancellationToken, Task> publish,
        Action<Exception> failed,
        CancellationToken cancellationToken,
        Action? onTick = null)
    {
        var effectiveInterval = interval <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : interval;
        using var timer = new PeriodicTimer(effectiveInterval);
        do
        {
            try
            {
                var metrics = provider.GetSnapshot();
                if (metrics is not null)
                {
                    sampled(metrics);
                    await publish(metrics, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                try
                {
                    failed(error);
                }
                catch (Exception handlerError) when (handlerError is not OperationCanceledException)
                {
                    // Error handler failure must not crash the publisher loop.
                }
            }

            try
            {
                onTick?.Invoke();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                try
                {
                    failed(error);
                }
                catch (Exception handlerError) when (handlerError is not OperationCanceledException)
                {
                }
            }
        }
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }
}
