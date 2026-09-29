namespace StreamDeck.Covers;

public sealed record CoverProcessResult(
    string CoverId,
    string FilePath,
    string? AccentHex,
    long LengthBytes,
    bool WasCacheHit);
