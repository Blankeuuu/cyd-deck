using System.Runtime.InteropServices;

namespace StreamDeck.Audio;

public sealed class CoreAudioEndpoint : INativeAudioEndpoint
{
    private static readonly Guid AudioEndpointVolumeId = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly Guid EventContext = new("E13EA98A-AF60-4DAF-BE83-22CF2937C360");

    private readonly IAudioEndpointVolume _endpointVolume;
    private readonly AudioEndpointVolumeCallback _callback;
    private bool _disposed;

    public CoreAudioEndpoint()
        : this(CreateDefaultRenderEndpoint())
    {
    }

    public static CoreAudioEndpoint CreateDefaultCaptureEndpoint()
    {
        return new CoreAudioEndpoint(CreateDefaultEndpoint(EDataFlow.Capture));
    }

    internal CoreAudioEndpoint(IAudioEndpointVolume endpointVolume)
    {
        _endpointVolume = endpointVolume;
        _callback = new AudioEndpointVolumeCallback(this);
        CoreAudioMarshal.ThrowIfFailed(_endpointVolume.RegisterControlChangeNotify(_callback));
    }

    public event EventHandler<NativeAudioEndpointChangedEventArgs>? Changed;

    public float GetVolumeScalar()
    {
        ThrowIfDisposed();
        CoreAudioMarshal.ThrowIfFailed(_endpointVolume.GetMasterVolumeLevelScalar(out var volumeScalar));
        AudioScalarMapper.ValidateScalar(volumeScalar);
        return volumeScalar;
    }

    public bool GetMute()
    {
        ThrowIfDisposed();
        CoreAudioMarshal.ThrowIfFailed(_endpointVolume.GetMute(out var isMuted));
        return isMuted;
    }

    public void SetVolumeScalar(float volumeScalar)
    {
        ThrowIfDisposed();
        AudioScalarMapper.ValidateScalar(volumeScalar);
        var eventContext = EventContext;
        CoreAudioMarshal.ThrowIfFailed(_endpointVolume.SetMasterVolumeLevelScalar(volumeScalar, ref eventContext));
    }

    public void SetMute(bool isMuted)
    {
        ThrowIfDisposed();
        var eventContext = EventContext;
        CoreAudioMarshal.ThrowIfFailed(_endpointVolume.SetMute(isMuted, ref eventContext));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _endpointVolume.UnregisterControlChangeNotify(_callback);

        if (Marshal.IsComObject(_endpointVolume))
        {
            Marshal.FinalReleaseComObject(_endpointVolume);
        }

        _disposed = true;
    }

    private static IAudioEndpointVolume CreateDefaultRenderEndpoint()
    {
        return CreateDefaultEndpoint(EDataFlow.Render);
    }

    private static IAudioEndpointVolume CreateDefaultEndpoint(EDataFlow dataFlow)
    {
        var enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumerator();

        try
        {
            CoreAudioMarshal.ThrowIfFailed(enumerator.GetDefaultAudioEndpoint(dataFlow, ERole.Multimedia, out var device));

            try
            {
                var endpointVolumeId = AudioEndpointVolumeId;
                CoreAudioMarshal.ThrowIfFailed(device.Activate(ref endpointVolumeId, ClsCtx.InprocServer, IntPtr.Zero, out var endpoint));
                return (IAudioEndpointVolume)endpoint;
            }
            finally
            {
                if (Marshal.IsComObject(device))
                {
                    Marshal.FinalReleaseComObject(device);
                }
            }
        }
        finally
        {
            if (Marshal.IsComObject(enumerator))
            {
                Marshal.FinalReleaseComObject(enumerator);
            }
        }
    }

    private void RaiseChanged(float volumeScalar, bool isMuted)
    {
        if (_disposed)
        {
            return;
        }

        Changed?.Invoke(this, new NativeAudioEndpointChangedEventArgs(volumeScalar, isMuted));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class AudioEndpointVolumeCallback(CoreAudioEndpoint owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr notifyData)
        {
            try
            {
                var notification = Marshal.PtrToStructure<AudioVolumeNotificationData>(notifyData);
                owner.RaiseChanged(notification.MasterVolume, notification.Muted);
            }
            catch
            {
                return 0;
            }

            return 0;
        }
    }
}
