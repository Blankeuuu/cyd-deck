namespace StreamDeck.SystemMonitoring;

public readonly record struct MemoryStatus(ulong TotalBytes, ulong AvailableBytes)
{
    public ulong UsedBytes => TotalBytes >= AvailableBytes ? TotalBytes - AvailableBytes : 0;
}
