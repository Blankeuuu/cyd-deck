namespace StreamDeck.Audio;

public static class AudioScalarMapper
{
    public static int ToPercent(float volumeScalar)
    {
        ValidateScalar(volumeScalar);
        return (int)MathF.Round(volumeScalar * 100f, MidpointRounding.AwayFromZero);
    }

    public static float ToScalar(int volumePercent)
    {
        return Math.Clamp(volumePercent, 0, 100) / 100f;
    }

    internal static void ValidateScalar(float volumeScalar)
    {
        if (float.IsNaN(volumeScalar) || volumeScalar < 0f || volumeScalar > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(volumeScalar), volumeScalar, "Volume scalar must be between 0 and 1.");
        }
    }
}
