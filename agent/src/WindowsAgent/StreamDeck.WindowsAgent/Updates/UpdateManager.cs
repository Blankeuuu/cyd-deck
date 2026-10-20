using System.Security.Cryptography;

namespace StreamDeck.WindowsAgent.Updates;

public sealed class UpdateManager : IDisposable
{
    private readonly UpdateReleaseClient releaseClient;
    private readonly UpdateStagingService stagingService;
    private readonly UpdateInstallerLauncher installerLauncher;
    private readonly Version currentAgentVersion;
    private readonly object gate = new();
    private long generation;
    private bool disposed;
    private UpdateCheckResult? stagedAgentRelease;
    private UpdateCheckResult? stagedFirmwareRelease;

    public UpdateCheckResult? LastCheckResult { get; private set; }
    public string? StagedAgentInstallerPath { get; private set; }
    public string? StagedFirmwarePath { get; private set; }
    public DateTimeOffset? LastCheckTimeUtc { get; private set; }

    public event EventHandler<UpdateCheckResult>? CheckCompleted;

    public UpdateManager(
        Version? currentAgentVersion = null,
        UpdateReleaseClient? releaseClient = null,
        UpdateStagingService? stagingService = null,
        UpdateInstallerLauncher? installerLauncher = null)
    {
        this.currentAgentVersion = currentAgentVersion ?? GetCurrentAssemblyVersion();
        this.releaseClient = releaseClient ?? new UpdateReleaseClient();
        this.stagingService = stagingService ?? new UpdateStagingService();
        this.installerLauncher = installerLauncher ?? new UpdateInstallerLauncher();
    }

    public static Version GetCurrentAssemblyVersion()
    {
        var version = typeof(UpdateManager).Assembly.GetName().Version;
        return version is not null ? new Version(version.Major, version.Minor, Math.Max(0, version.Build)) : new Version(1, 4, 0);
    }

    public async Task CheckAutomaticallyAsync(Func<string?> firmwareVersion,
        TimeSpan startupDelay, CancellationToken cancellationToken = default)
    {
        await Task.Delay(startupDelay, cancellationToken).ConfigureAwait(false);
        if (ShouldCheckAutomatically(TimeSpan.FromHours(4)))
            await CheckForUpdatesAsync(firmwareVersion(), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public bool ShouldCheckAutomatically(TimeSpan minimumInterval)
    {
        if (LastCheckTimeUtc is null)
        {
            return true;
        }

        return DateTimeOffset.UtcNow - LastCheckTimeUtc.Value >= minimumInterval;
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        string? currentFirmwareVersion = null,
        string? apiUrl = null,
        CancellationToken cancellationToken = default)
    {
        long checkGeneration;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            checkGeneration = ++generation;
            LastCheckResult = null;
            StagedAgentInstallerPath = null;
            StagedFirmwarePath = null;
            stagedAgentRelease = null;
            stagedFirmwareRelease = null;
        }

        var result = await releaseClient.CheckForUpdatesAsync(
            currentAgentVersion, currentFirmwareVersion, apiUrl, cancellationToken).ConfigureAwait(false);

        lock (gate)
        {
            if (disposed || generation != checkGeneration) return result;
            LastCheckResult = result;
            LastCheckTimeUtc = DateTimeOffset.UtcNow;
        }
        CheckCompleted?.Invoke(this, result);
        return result;
    }

    public Task<UpdateStagingResult> StageAgentUpdateAsync(
        IProgress<UpdateStagingProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        StageAsync(UpdateAssetType.WindowsAgentInstaller, progress, cancellationToken);

    public Task<UpdateStagingResult> StageFirmwareUpdateAsync(
        IProgress<UpdateStagingProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        StageAsync(UpdateAssetType.CYDFirmware, progress, cancellationToken);

    private async Task<UpdateStagingResult> StageAsync(UpdateAssetType type,
        IProgress<UpdateStagingProgress>? progress, CancellationToken cancellationToken)
    {
        UpdateCheckResult? release;
        long stageGeneration;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            release = LastCheckResult;
            stageGeneration = generation;
        }
        var agent = type == UpdateAssetType.WindowsAgentInstaller;
        var asset = agent ? release?.AgentAsset : release?.FirmwareAsset;
        var hash = agent ? release?.ExpectedAgentSha256 : release?.ExpectedFirmwareSha256;
        if (release?.Status != UpdateCheckStatus.UpdateAvailable || asset is null || !IsCompleteHash(hash))
            return new(false, null, null, "Brak dostępnej aktualizacji lub pełnej sumy SHA-256.");

        var result = await stagingService.StageAssetAsync(asset, hash!, type, progress, cancellationToken).ConfigureAwait(false);
        lock (gate)
        {
            if (disposed || generation != stageGeneration || !ReferenceEquals(release, LastCheckResult))
                return new(false, null, null, "Wydanie zostało unieważnione przez nowsze sprawdzenie aktualizacji.");
            if (result.Success)
            {
                if (agent)
                {
                    StagedAgentInstallerPath = result.StagedFilePath;
                    stagedAgentRelease = release;
                }
                else
                {
                    StagedFirmwarePath = result.StagedFilePath;
                    stagedFirmwareRelease = release;
                }
            }
        }
        return result;
    }

    private static bool IsCompleteHash(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    private bool VerifyStagedFile(bool agent, out string? errorMessage)
    {
        var release = agent ? stagedAgentRelease : stagedFirmwareRelease;
        var path = agent ? StagedAgentInstallerPath : StagedFirmwarePath;
        var hash = agent ? release?.ExpectedAgentSha256 : release?.ExpectedFirmwareSha256;
        errorMessage = null;
        if (disposed || release?.Status != UpdateCheckStatus.UpdateAvailable ||
            !ReferenceEquals(release, LastCheckResult) || !IsCompleteHash(hash) ||
            string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            errorMessage = "Brak zweryfikowanego pakietu bieżącego wydania.";
            return false;
        }
        try
        {
            using var stream = File.OpenRead(path);
            if (string.Equals(Convert.ToHexString(SHA256.HashData(stream)), hash, StringComparison.OrdinalIgnoreCase))
                return true;
            errorMessage = agent
                ? "Suma kontrolna instalatora na dysku uległa zmianie. Pobierz pakiet ponownie."
                : "Suma kontrolna firmware na dysku uległa zmianie. Pobierz pakiet ponownie.";
        }
        catch (Exception ex)
        {
            errorMessage = $"Błąd weryfikacji integralności pakietu: {ex.Message}";
        }
        if (agent) { StagedAgentInstallerPath = null; stagedAgentRelease = null; }
        else { StagedFirmwarePath = null; stagedFirmwareRelease = null; }
        return false;
    }

    public bool TryGetVerifiedFirmware(out string? path, out string? version, out string? errorMessage)
        => TryGetVerifiedFirmware(out path, out version, out errorMessage, out _);

    public bool TryGetVerifiedFirmware(out string? path, out string? version, out string? errorMessage, out string? sha256)
    {
        lock (gate)
        {
            path = null;
            version = null;
            sha256 = null;
            if (!VerifyStagedFile(false, out errorMessage)) return false;
            path = StagedFirmwarePath;
            version = stagedFirmwareRelease?.LatestRelease?.TagName;
            sha256 = stagedFirmwareRelease?.ExpectedFirmwareSha256;
            return true;
        }
    }

    public bool TryInstallStagedAgent(bool silent, out string? errorMessage)
    {
        lock (gate)
        {
            if (!VerifyStagedFile(true, out errorMessage)) return false;
            return installerLauncher.TryLaunchInstaller(StagedAgentInstallerPath!, out errorMessage,
                silent, exitCurrentProcess: true);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            generation++;
            StagedAgentInstallerPath = null;
            StagedFirmwarePath = null;
        }
        releaseClient.Dispose();
        stagingService.Dispose();
    }
}
