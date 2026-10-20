using System.Text.RegularExpressions;

namespace StreamDeck.WindowsAgent.Updates;

public static class UpdateSecurityValidator
{
    public const string GitHubOwner = "Blankeuuu";
    public const string GitHubRepo = "cyd-deck";
    public const string ExpectedApiBase = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/";
    public const string ExpectedDownloadBase = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/download/";

    private static readonly Regex AgentInstallerPattern = new(
        @"^CYD-DECK-Setup-\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?-win-x64\.exe$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FirmwareAssetPattern = new(
        @"^firmware-ST7789-\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?\.bin$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ChecksumsAssetPattern = new(
        @"^(?:SHA256SUMS\.txt|checksums\.txt|manifest\.json)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public const byte EspFirmwareMagic = 0xE9;
    public static readonly byte[] WindowsPeMagic = [0x4D, 0x5A]; // 'M', 'Z'

    public static string NormalizeVersionString(string versionText)
    {
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return string.Empty;
        }

        var trimmed = versionText.Trim();
        return trimmed.StartsWith('v') || trimmed.StartsWith('V') ? trimmed[1..] : trimmed;
    }

    public static string GetExpectedAgentInstallerName(string versionText)
    {
        return $"CYD-DECK-Setup-{NormalizeVersionString(versionText)}-win-x64.exe";
    }

    public static string GetExpectedFirmwareName(string versionText)
    {
        return $"firmware-ST7789-{NormalizeVersionString(versionText)}.bin";
    }

    public static bool IsValidGitHubApiUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (!string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedPath = $"/repos/{GitHubOwner}/{GitHubRepo}/";
        return uri.AbsolutePath.StartsWith(expectedPath, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsValidGitHubDownloadUrl(string? url, string? expectedTagName = null, string? expectedAssetName = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return IsSafeDownloadRedirectUrl(uri, expectedTagName, expectedAssetName);
    }

    public static bool IsSafeDownloadRedirectUrl(Uri uri, string? expectedTagName = null, string? expectedAssetName = null)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            var expectedPrefix = $"/{GitHubOwner}/{GitHubRepo}/releases/download/";
            if (!uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(expectedTagName))
            {
                var tagSegment = $"/{GitHubOwner}/{GitHubRepo}/releases/download/{expectedTagName}/";
                if (!uri.AbsolutePath.StartsWith(tagSegment, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(expectedAssetName))
            {
                var expectedEnd = $"/{expectedAssetName}";
                if (!uri.AbsolutePath.EndsWith(expectedEnd, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        if (string.Equals(uri.Host, "objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static bool IsSafeSanitizedFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var baseName = Path.GetFileName(fileName);
        return string.Equals(fileName, baseName, StringComparison.Ordinal);
    }

    public static bool IsAgentInstallerAsset(string? fileName)
    {
        if (!IsSafeSanitizedFileName(fileName))
        {
            return false;
        }

        return AgentInstallerPattern.IsMatch(fileName!);
    }

    public static bool IsAgentInstallerAssetForVersion(string? fileName, string versionText)
    {
        if (!IsSafeSanitizedFileName(fileName))
        {
            return false;
        }

        var expected = GetExpectedAgentInstallerName(versionText);
        return string.Equals(fileName, expected, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsFirmwareAsset(string? fileName)
    {
        if (!IsSafeSanitizedFileName(fileName))
        {
            return false;
        }

        // Full images are intended for clean esptool/web-installer flashing at offset 0x0,
        // and must never be staged or uploaded via OTA.
        if (fileName!.Contains("-full", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains(".full", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("full.bin", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Wrong board protection: only ST7789 display controller is supported for OTA.
        if (fileName.Contains("ili9341", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return FirmwareAssetPattern.IsMatch(fileName);
    }

    public static bool IsFirmwareAssetForVersion(string? fileName, string versionText)
    {
        if (!IsFirmwareAsset(fileName))
        {
            return false;
        }

        var expected = GetExpectedFirmwareName(versionText);
        return string.Equals(fileName, expected, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsChecksumsAsset(string? fileName)
    {
        if (!IsSafeSanitizedFileName(fileName))
        {
            return false;
        }

        return ChecksumsAssetPattern.IsMatch(fileName!);
    }

    public static bool ValidateFirmwareMagicAndSize(byte[] imageBytes, out string? safeError)
    {
        safeError = null;
        if (imageBytes.Length == 0)
        {
            safeError = "Obraz oprogramowania układowego jest pusty.";
            return false;
        }

        if (imageBytes.Length > ArduinoOtaFirmwareUpdater.MaxFirmwareBytes)
        {
            safeError = "Obraz oprogramowania układowego przekracza maksymalny rozmiar partycji OTA.";
            return false;
        }

        if (imageBytes[0] != EspFirmwareMagic)
        {
            safeError = "Plik nie jest poprawnym binarnym obrazem oprogramowania układowego ESP.";
            return false;
        }

        return true;
    }

    public static bool ValidateInstallerMagicAndSize(byte[] headerBytes, long totalLength, out string? safeError)
    {
        safeError = null;
        if (totalLength < 1024)
        {
            safeError = "Plik instalatora jest niekompletny lub uszkodzony.";
            return false;
        }

        // Limit sanity: installer shouldn't exceed 300MB
        if (totalLength > 300 * 1024 * 1024)
        {
            safeError = "Plik instalatora przekracza dopuszczalny limit rozmiaru.";
            return false;
        }

        if (headerBytes.Length < 2 || headerBytes[0] != WindowsPeMagic[0] || headerBytes[1] != WindowsPeMagic[1])
        {
            safeError = "Pobrany plik nie jest poprawnym plikiem wykonywalnym systemu Windows (brak sygnatury MZ).";
            return false;
        }

        return true;
    }
}
