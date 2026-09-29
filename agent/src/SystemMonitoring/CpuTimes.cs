namespace StreamDeck.SystemMonitoring;

public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);
