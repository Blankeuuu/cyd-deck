namespace StreamDeck.Audio;

public sealed record AudioSnapshot(int VolumePercent, bool IsMuted, string? DeviceName = null);
