namespace StreamDeck.Audio;

public sealed class NativeAudioEndpointChangedEventArgs : EventArgs
{
    public NativeAudioEndpointChangedEventArgs(float volumeScalar, bool isMuted)
    {
        AudioScalarMapper.ValidateScalar(volumeScalar);
        VolumeScalar = volumeScalar;
        IsMuted = isMuted;
    }

    public float VolumeScalar { get; }

    public bool IsMuted { get; }
}
