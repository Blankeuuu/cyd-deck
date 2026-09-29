namespace StreamDeck.Audio;

public interface INativeAudioEndpoint : IDisposable
{
    event EventHandler<NativeAudioEndpointChangedEventArgs>? Changed;

    float GetVolumeScalar();

    bool GetMute();

    void SetVolumeScalar(float volumeScalar);

    void SetMute(bool isMuted);
}
