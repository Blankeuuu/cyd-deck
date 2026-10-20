using System.Net;
using System.Security.Cryptography;

namespace StreamDeck.WindowsAgent.Updates;

public sealed class UpdateStagingService : IDisposable
{
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly string stagingDirectory;

    public const long MaxAgentInstallerBytes = 300 * 1024 * 1024; // 300 MB
    public const long MaxFirmwareBytes = ArduinoOtaFirmwareUpdater.MaxFirmwareBytes; // 0x1F0000 (~1.93 MB)

    public UpdateStagingService(HttpClient? httpClient = null, string? customStagingDirectory = null)
    {
        this.httpClient = httpClient ?? new HttpClient(CreateDownloadHandler()) { Timeout = TimeSpan.FromSeconds(30) };
        ownsHttpClient = httpClient is null;
        stagingDirectory = customStagingDirectory ?? GetDefaultStagingDirectory();
    }

    // The production transport never follows redirects before our validation.
    public static HttpClientHandler CreateDownloadHandler() => new() { AllowAutoRedirect = false };

    public void Dispose()
    {
        if (ownsHttpClient) httpClient.Dispose();
    }

    public static string GetDefaultStagingDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "StreamDeckAgent", "updates", "staging");
    }

    public string StagingDirectory => stagingDirectory;

    public async Task<UpdateStagingResult> StageAssetAsync(
        ReleaseAssetInfo asset,
        string expectedSha256,
        UpdateAssetType assetType,
        IProgress<UpdateStagingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);

        if (!UpdateSecurityValidator.IsValidGitHubDownloadUrl(asset.DownloadUrl))
        {
            return new UpdateStagingResult(false, null, null, "Adres URL pobierania nie należy do zaufanej domeny GitHub.");
        }

        if (assetType == UpdateAssetType.WindowsAgentInstaller && !UpdateSecurityValidator.IsAgentInstallerAsset(asset.Name))
        {
            return new UpdateStagingResult(false, null, null, "Nazwa pliku instalatora nie spełnia reguł dozwolonych zasobów (whitelist).");
        }

        if (assetType == UpdateAssetType.CYDFirmware && !UpdateSecurityValidator.IsFirmwareAsset(asset.Name))
        {
            return new UpdateStagingResult(false, null, null, "Nazwa pliku oprogramowania układowego nie spełnia reguł dozwolonych zasobów (whitelist) lub jest obrazem pełnym (full flash).");
        }

        var maxAllowedBytes = assetType == UpdateAssetType.CYDFirmware ? MaxFirmwareBytes : MaxAgentInstallerBytes;
        if (asset.Size > maxAllowedBytes)
        {
            return new UpdateStagingResult(false, null, null, "Rozmiar deklarowany zasobu przekracza dopuszczalny limit.");
        }

        Directory.CreateDirectory(stagingDirectory);
        var targetFile = Path.Combine(stagingDirectory, asset.Name);
        var tempFile = Path.Combine(stagingDirectory, $"{asset.Name}.{Guid.NewGuid():N}.tmp");

        // Timeout bounding both header and body transfer
        using var bodyTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bodyTimeoutCts.CancelAfter(TimeSpan.FromSeconds(90));
        var effectiveCt = bodyTimeoutCts.Token;

        try
        {
            progress?.Report(new UpdateStagingProgress(0, asset.Size, 0, "Nawiązywanie połączenia z serwerem wydań…"));

            using var response = await SendWithSafeRedirectsAsync(asset.DownloadUrl, effectiveCt).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateStagingResult(false, null, null, $"Błąd serwera podczas pobierania: {(int)response.StatusCode}.");
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > maxAllowedBytes)
            {
                return new UpdateStagingResult(false, null, null, "Pobierany plik przekracza dopuszczalny limit rozmiaru.");
            }

            var totalBytes = contentLength ?? asset.Size;
            await using var sourceStream = await response.Content.ReadAsStreamAsync(effectiveCt).ConfigureAwait(false);
            await using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                var bytesDownloaded = 0L;
                int read;

                while ((read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), effectiveCt).ConfigureAwait(false)) > 0)
                {
                    bytesDownloaded += read;

                    // Streaming limit check BEFORE writing entire payload
                    if (bytesDownloaded > maxAllowedBytes)
                    {
                        return new UpdateStagingResult(false, null, null, "Przekroczono maksymalny dopuszczalny rozmiar pobieranego strumienia.");
                    }

                    await fileStream.WriteAsync(buffer.AsMemory(0, read), effectiveCt).ConfigureAwait(false);
                    sha256.AppendData(buffer, 0, read);

                    var percent = totalBytes > 0 ? (int)Math.Clamp(bytesDownloaded * 100 / totalBytes, 0, 100) : 0;
                    progress?.Report(new UpdateStagingProgress(bytesDownloaded, totalBytes, percent, $"Pobieranie: {percent}%"));
                }

                await fileStream.FlushAsync(effectiveCt).ConfigureAwait(false);

                if (asset.Size > 0 && bytesDownloaded != asset.Size)
                {
                    return new UpdateStagingResult(false, null, null, $"Rozmiar pobranego pliku ({bytesDownloaded} B) nie zgadza się z deklarowanym w wydaniu ({asset.Size} B).");
                }

                var computedHash = Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant();
                if (!string.Equals(computedHash, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return new UpdateStagingResult(
                        false,
                        null,
                        computedHash,
                        $"Naruszenie integralności: obliczona suma SHA-256 ({computedHash}) nie zgadza się z oczekiwaną ({expectedSha256}).");
                }
            }

            // Validate binary integrity and magic
            var preflightError = ValidateDownloadedPreflight(tempFile, assetType);
            if (preflightError is not null)
            {
                return new UpdateStagingResult(false, null, null, preflightError);
            }

            // Atomic move to target
            File.Move(tempFile, targetFile, overwrite: true);
            progress?.Report(new UpdateStagingProgress(totalBytes, totalBytes, 100, "Pakiet zweryfikowany i gotowy do instalacji."));

            return new UpdateStagingResult(true, targetFile, expectedSha256.ToLowerInvariant(), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateStagingResult(false, null, null, $"Błąd podczas pobierania lub zapisu: {ex.Message}");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch
                {
                    // Ignore temp file deletion failure
                }
            }
        }
    }

    private async Task<HttpResponseMessage> SendWithSafeRedirectsAsync(string initialUrl, CancellationToken cancellationToken)
    {
        var currentUrl = initialUrl;
        for (var i = 0; i < 5; i++)
        {
            if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var uri) || !UpdateSecurityValidator.IsSafeDownloadRedirectUrl(uri))
            {
                throw new InvalidOperationException($"Niezaufany adres URL przekierowania podczas pobierania: {currentUrl}");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Moved
                or HttpStatusCode.Redirect
                or HttpStatusCode.RedirectMethod
                or HttpStatusCode.TemporaryRedirect
                or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                response.Dispose();

                if (location is null)
                {
                    throw new InvalidOperationException("Brak nagłówka Location w przekierowaniu pobierania.");
                }

                var nextUri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                currentUrl = nextUri.ToString();
                continue;
            }

            return response;
        }

        throw new InvalidOperationException("Zbyt wiele przekierowań pobierania.");
    }

    private static string? ValidateDownloadedPreflight(string filePath, UpdateAssetType assetType)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists || fileInfo.Length == 0)
        {
            return "Pobrany plik jest pusty.";
        }

        using var stream = File.OpenRead(filePath);
        if (assetType == UpdateAssetType.CYDFirmware)
        {
            if (fileInfo.Length > ArduinoOtaFirmwareUpdater.MaxFirmwareBytes)
            {
                return "Obraz oprogramowania układowego jest zbyt duży dla partycji OTA urządzenia.";
            }

            var firstByte = stream.ReadByte();
            if (firstByte != UpdateSecurityValidator.EspFirmwareMagic)
            {
                return "Plik nie jest poprawnym obrazem oprogramowania układowego ESP32 (błędna sygnatura).";
            }
        }
        else if (assetType == UpdateAssetType.WindowsAgentInstaller)
        {
            var header = new byte[2];
            var read = stream.Read(header, 0, 2);
            if (!UpdateSecurityValidator.ValidateInstallerMagicAndSize(header[..read], fileInfo.Length, out var safeError))
            {
                return safeError;
            }
        }

        return null;
    }
}
