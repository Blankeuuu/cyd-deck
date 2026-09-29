using System.Runtime.InteropServices;
using StreamDeck.Audio;

if (args.Contains("--windows-smoke", StringComparer.Ordinal))
{
    return RunWindowsSmoke();
}

var tests = new (string Name, Action Run)[]
{
    ("volume scalar maps to percent and clamps command input", VolumeMappingUsesPercentAndClamps),
    ("refresh publishes current endpoint state", RefreshPublishesCurrentEndpointState),
    ("native endpoint event updates current state", NativeEndpointEventUpdatesState),
    ("set volume and toggle mute mutate endpoint", CommandsMutateEndpoint),
    ("service disposes endpoint and ignores later events", DisposeUnsubscribesAndDisposesEndpoint),
    ("endpoint failures return false without crashing", EndpointFailuresReturnFalse),
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}

return 0;

static int RunWindowsSmoke()
{
    try
    {
        using var endpoint = new CoreAudioEndpoint();
        using var service = new WindowsMasterVolumeService(endpoint);
        var callbackCount = 0;
        service.AudioChanged += (_, _) => callbackCount++;

        if (!service.Refresh())
        {
            Console.WriteLine("FAIL Windows Core Audio smoke: endpoint was created but refresh did not return current state.");
            return 1;
        }

        var snapshot = service.CurrentSnapshot ?? throw new InvalidOperationException("Refresh succeeded without current snapshot.");
        Console.WriteLine($"PASS Windows Core Audio smoke: volume={snapshot.VolumePercent} muted={snapshot.IsMuted} callbackCount={callbackCount}");
        return 0;
    }
    catch (COMException exception) when (IsNoDefaultOutputDevice(exception))
    {
        Console.WriteLine($"SKIP Windows Core Audio smoke: no default render output device ({FormatHResult(exception.HResult)}).");
        return 0;
    }
    catch (Exception exception)
    {
        Console.WriteLine($"FAIL Windows Core Audio smoke: {exception.GetType().Name}: {exception.Message}");
        return 1;
    }
}

static bool IsNoDefaultOutputDevice(COMException exception)
{
    return unchecked((uint)exception.HResult) == 0x80070490;
}

static string FormatHResult(int hresult)
{
    return $"0x{unchecked((uint)hresult):X8}";
}

static void VolumeMappingUsesPercentAndClamps()
{
    AssertEqual(0, AudioScalarMapper.ToPercent(0f), "zero scalar");
    AssertEqual(50, AudioScalarMapper.ToPercent(0.5f), "half scalar");
    AssertEqual(100, AudioScalarMapper.ToPercent(1f), "full scalar");
    AssertEqual(0f, AudioScalarMapper.ToScalar(-20), "low clamp");
    AssertEqual(1f, AudioScalarMapper.ToScalar(125), "high clamp");
}

static void RefreshPublishesCurrentEndpointState()
{
    var endpoint = new FakeAudioEndpoint(volumeScalar: 0.42f, isMuted: true);
    using var service = new WindowsMasterVolumeService(endpoint);
    var events = new List<AudioSnapshot>();
    service.AudioChanged += (_, snapshot) => events.Add(snapshot);

    var result = service.Refresh();

    AssertTrue(result, "refresh result");
    AssertEqual(new AudioSnapshot(42, true), service.CurrentSnapshot, "current snapshot");
    AssertEqual(1, events.Count, "event count");
    AssertEqual(new AudioSnapshot(42, true), events[0], "event snapshot");
}

static void NativeEndpointEventUpdatesState()
{
    var endpoint = new FakeAudioEndpoint(volumeScalar: 0.10f, isMuted: false);
    using var service = new WindowsMasterVolumeService(endpoint);
    var events = new List<AudioSnapshot>();
    service.AudioChanged += (_, snapshot) => events.Add(snapshot);

    endpoint.RaiseChanged(0.73f, true);

    AssertEqual(new AudioSnapshot(73, true), service.CurrentSnapshot, "event snapshot");
    AssertEqual(1, events.Count, "event count");
}

static void CommandsMutateEndpoint()
{
    var endpoint = new FakeAudioEndpoint(volumeScalar: 0.25f, isMuted: false);
    using var service = new WindowsMasterVolumeService(endpoint);

    AssertTrue(service.SetVolume(88), "set volume");
    AssertEqual(0.88f, endpoint.VolumeScalar, "endpoint volume");

    AssertTrue(service.ToggleMute(), "toggle mute on");
    AssertTrue(endpoint.IsMuted, "endpoint muted");

    AssertTrue(service.ToggleMute(), "toggle mute off");
    AssertFalse(endpoint.IsMuted, "endpoint unmuted");
}

static void DisposeUnsubscribesAndDisposesEndpoint()
{
    var endpoint = new FakeAudioEndpoint(volumeScalar: 0.3f, isMuted: false);
    var service = new WindowsMasterVolumeService(endpoint);
    var eventCount = 0;
    service.AudioChanged += (_, _) => eventCount++;

    service.Dispose();
    endpoint.RaiseChanged(0.9f, true);

    AssertTrue(endpoint.IsDisposed, "endpoint disposed");
    AssertEqual(0, eventCount, "event count after dispose");
}

static void EndpointFailuresReturnFalse()
{
    var endpoint = new FakeAudioEndpoint(volumeScalar: 0.1f, isMuted: false)
    {
        ThrowOnGet = true,
        ThrowOnSet = true
    };

    using var service = new WindowsMasterVolumeService(endpoint);

    AssertFalse(service.Refresh(), "refresh failure");
    AssertFalse(service.SetVolume(55), "set volume failure");
    AssertFalse(service.SetMute(true), "set mute failure");
    AssertFalse(service.ToggleMute(), "toggle mute failure");
}

static void AssertEqual<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
    }
}

static void AssertTrue(bool value, string name)
{
    if (!value)
    {
        throw new InvalidOperationException($"{name}: expected true");
    }
}

static void AssertFalse(bool value, string name)
{
    if (value)
    {
        throw new InvalidOperationException($"{name}: expected false");
    }
}

sealed class FakeAudioEndpoint(float volumeScalar, bool isMuted) : INativeAudioEndpoint
{
    public event EventHandler<NativeAudioEndpointChangedEventArgs>? Changed;

    public float VolumeScalar { get; private set; } = volumeScalar;

    public bool IsMuted { get; private set; } = isMuted;

    public bool ThrowOnGet { get; init; }

    public bool ThrowOnSet { get; init; }

    public bool IsDisposed { get; private set; }

    public float GetVolumeScalar()
    {
        ThrowIfNeeded(ThrowOnGet);
        return VolumeScalar;
    }

    public bool GetMute()
    {
        ThrowIfNeeded(ThrowOnGet);
        return IsMuted;
    }

    public void SetVolumeScalar(float volumeScalar)
    {
        ThrowIfNeeded(ThrowOnSet);
        VolumeScalar = volumeScalar;
    }

    public void SetMute(bool isMuted)
    {
        ThrowIfNeeded(ThrowOnSet);
        IsMuted = isMuted;
    }

    public void RaiseChanged(float volumeScalar, bool isMuted)
    {
        Changed?.Invoke(this, new NativeAudioEndpointChangedEventArgs(volumeScalar, isMuted));
    }

    public void Dispose()
    {
        IsDisposed = true;
    }

    private static void ThrowIfNeeded(bool shouldThrow)
    {
        if (shouldThrow)
        {
            throw new InvalidOperationException("Endpoint failed.");
        }
    }
}
