using System.Globalization;
using System.Text.RegularExpressions;

namespace StreamDeck.WindowsAgent.Updates;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    ChecksumMissing,
    Error
}

public enum UpdateAssetType
{
    WindowsAgentInstaller,
    CYDFirmware
}

public sealed record ReleaseAssetInfo(
    string Name,
    long Size,
    string DownloadUrl,
    string ContentType);

public sealed record ReleaseInfo(
    string TagName,
    Version Version,
    string Name,
    string Body,
    DateTimeOffset PublishedAt,
    bool IsPrerelease,
    bool IsDraft,
    string HtmlUrl,
    IReadOnlyList<ReleaseAssetInfo> Assets);

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    Version CurrentAgentVersion,
    string? CurrentFirmwareVersion,
    ReleaseInfo? LatestRelease,
    ReleaseAssetInfo? AgentAsset,
    ReleaseAssetInfo? FirmwareAsset,
    ReleaseAssetInfo? ChecksumsAsset,
    string? ExpectedAgentSha256,
    string? ExpectedFirmwareSha256,
    string? ErrorMessage)
{
    public bool HasAgentUpdate => Status == UpdateCheckStatus.UpdateAvailable && AgentAsset is not null;
    public bool HasFirmwareUpdate => Status == UpdateCheckStatus.UpdateAvailable && FirmwareAsset is not null;
}

public sealed record UpdateStagingProgress(
    long BytesDownloaded,
    long TotalBytes,
    int PercentComplete,
    string StatusMessage);

public sealed record UpdateStagingResult(
    bool Success,
    string? StagedFilePath,
    string? Sha256Hash,
    string? SafeError);

public static class UpdateVersionParser
{
    private static readonly Regex VersionPattern = new(
        @"^v?(?<major>\d+)\.(?<minor>\d+)(?:\.(?<build>\d+))?(?:\.(?<revision>\d+))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool TryParse(string? versionText, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return false;
        }

        var match = VersionPattern.Match(versionText.Trim());
        if (!match.Success)
        {
            return false;
        }

        var major = int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture);
        var minor = int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture);

        if (match.Groups["revision"].Success)
        {
            var build = match.Groups["build"].Success ? int.Parse(match.Groups["build"].Value, CultureInfo.InvariantCulture) : 0;
            var revision = int.Parse(match.Groups["revision"].Value, CultureInfo.InvariantCulture);
            version = new Version(major, minor, build, revision);
        }
        else if (match.Groups["build"].Success)
        {
            var build = int.Parse(match.Groups["build"].Value, CultureInfo.InvariantCulture);
            version = new Version(major, minor, build);
        }
        else
        {
            version = new Version(major, minor);
        }

        return true;
    }

    public static Version Parse(string? versionText)
    {
        if (TryParse(versionText, out var version))
        {
            return version;
        }

        throw new FormatException($"Invalid version format: '{versionText}'");
    }

    public static bool IsNewer(Version candidate, Version current)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(current);
        return candidate.CompareTo(current) > 0;
    }
}
