using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace StreamDeck.WindowsAgent.Updates;

public sealed class UpdateReleaseClient : IDisposable
{
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public UpdateReleaseClient(HttpClient? httpClient = null)
    {
        if (httpClient is not null)
        {
            this.httpClient = httpClient;
            ownsHttpClient = false;
        }
        else
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false, // We manually validate redirects for security
                ConnectTimeout = TimeSpan.FromSeconds(10)
            };

            this.httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
            this.httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("CYD-DECK-Agent", "1.4.0"));
            this.httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
            ownsHttpClient = true;
        }
    }

    public async Task<HttpResponseMessage> SendWithSafeRedirectsAsync(
        string requestUrl,
        string? expectedTagName = null,
        string? expectedAssetName = null,
        CancellationToken cancellationToken = default)
    {
        var currentUrl = requestUrl;
        for (var i = 0; i < 5; i++)
        {
            if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var uri) ||
                !UpdateSecurityValidator.IsSafeDownloadRedirectUrl(uri, expectedTagName, expectedAssetName))
            {
                throw new InvalidOperationException($"Niezaufany adres URL przekierowania: {currentUrl}");
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
                    throw new InvalidOperationException("Brak nagłówka Location w odpowiedzi przekierowania.");
                }

                var nextUri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                currentUrl = nextUri.ToString();
                continue;
            }

            return response;
        }

        throw new InvalidOperationException("Zbyt wiele przekierowań HTTP.");
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        Version currentAgentVersion,
        string? currentFirmwareVersion = null,
        string? apiUrl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentAgentVersion);

        var targetUrl = apiUrl ?? $"{UpdateSecurityValidator.ExpectedApiBase}releases/latest";
        if (!UpdateSecurityValidator.IsValidGitHubApiUrl(targetUrl))
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.Error,
                currentAgentVersion,
                currentFirmwareVersion,
                LatestRelease: null,
                AgentAsset: null,
                FirmwareAsset: null,
                ChecksumsAsset: null,
                ExpectedAgentSha256: null,
                ExpectedFirmwareSha256: null,
                ErrorMessage: "Nieprawidłowy lub niezaufany adres API wydań.");
        }

        try
        {
            using var response = await httpClient.GetAsync(targetUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.Error,
                    currentAgentVersion,
                    currentFirmwareVersion,
                    LatestRelease: null,
                    AgentAsset: null,
                    FirmwareAsset: null,
                    ChecksumsAsset: null,
                    ExpectedAgentSha256: null,
                    ExpectedFirmwareSha256: null,
                    ErrorMessage: $"Błąd podczas sprawdzania wydań: serwer zwrócił kod {(int)response.StatusCode}.");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var release = ParseReleaseJson(content);
            if (release is null)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.Error,
                    currentAgentVersion,
                    currentFirmwareVersion,
                    LatestRelease: null,
                    AgentAsset: null,
                    FirmwareAsset: null,
                    ChecksumsAsset: null,
                    ExpectedAgentSha256: null,
                    ExpectedFirmwareSha256: null,
                    ErrorMessage: "Nie udało się sparsować informacji o wydaniu.");
            }

            if (release.IsPrerelease || release.IsDraft)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.UpToDate,
                    currentAgentVersion,
                    currentFirmwareVersion,
                    LatestRelease: release,
                    AgentAsset: null,
                    FirmwareAsset: null,
                    ChecksumsAsset: null,
                    ExpectedAgentSha256: null,
                    ExpectedFirmwareSha256: null,
                    ErrorMessage: null);
            }

            var isAgentNewer = UpdateVersionParser.IsNewer(release.Version, currentAgentVersion);
            var isFirmwareNewer = false;
            if (UpdateVersionParser.TryParse(currentFirmwareVersion, out var parsedFw))
            {
                isFirmwareNewer = UpdateVersionParser.IsNewer(release.Version, parsedFw);
            }

            if (!isAgentNewer && !isFirmwareNewer)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.UpToDate,
                    currentAgentVersion,
                    currentFirmwareVersion,
                    LatestRelease: release,
                    AgentAsset: null,
                    FirmwareAsset: null,
                    ChecksumsAsset: null,
                    ExpectedAgentSha256: null,
                    ExpectedFirmwareSha256: null,
                    ErrorMessage: null);
            }

            // Version-bound asset lookup (exact version binding required by Lead)
            var agentAsset = release.Assets.FirstOrDefault(a =>
                UpdateSecurityValidator.IsAgentInstallerAssetForVersion(a.Name, release.TagName));
            var firmwareAsset = release.Assets.FirstOrDefault(a =>
                UpdateSecurityValidator.IsFirmwareAssetForVersion(a.Name, release.TagName));
            var checksumsAsset = release.Assets.FirstOrDefault(a =>
                UpdateSecurityValidator.IsChecksumsAsset(a.Name));

            if (checksumsAsset is null)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.ChecksumMissing,
                    currentAgentVersion,
                    currentFirmwareVersion,
                    LatestRelease: release,
                    AgentAsset: agentAsset,
                    FirmwareAsset: firmwareAsset,
                    ChecksumsAsset: null,
                    ExpectedAgentSha256: null,
                    ExpectedFirmwareSha256: null,
                    ErrorMessage: "Wydanie nie zawiera pliku sum kontrolnych (SHA256SUMS.txt). Automatyczna instalacja zablokowana ze względów bezpieczeństwa.");
            }

            // Download and parse checksums
            var checksums = await FetchChecksumsAsync(checksumsAsset.DownloadUrl, release.TagName, checksumsAsset.Name, cancellationToken).ConfigureAwait(false);
            string? expectedAgentSha = null;
            if (agentAsset is not null)
            {
                checksums.TryGetValue(agentAsset.Name, out expectedAgentSha);
                if (string.IsNullOrEmpty(expectedAgentSha) && isAgentNewer)
                {
                    return new UpdateCheckResult(
                        UpdateCheckStatus.ChecksumMissing,
                        currentAgentVersion,
                        currentFirmwareVersion,
                        LatestRelease: release,
                        AgentAsset: agentAsset,
                        FirmwareAsset: firmwareAsset,
                        ChecksumsAsset: checksumsAsset,
                        ExpectedAgentSha256: null,
                        ExpectedFirmwareSha256: null,
                        ErrorMessage: $"Brak sumy kontrolnej SHA-256 dla pliku {agentAsset.Name} w pliku sum wydania.");
                }
            }

            string? expectedFwSha = null;
            if (firmwareAsset is not null)
            {
                checksums.TryGetValue(firmwareAsset.Name, out expectedFwSha);
                if (string.IsNullOrEmpty(expectedFwSha) && isFirmwareNewer)
                {
                    return new UpdateCheckResult(
                        UpdateCheckStatus.ChecksumMissing,
                        currentAgentVersion,
                        currentFirmwareVersion,
                        LatestRelease: release,
                        AgentAsset: agentAsset,
                        FirmwareAsset: firmwareAsset,
                        ChecksumsAsset: checksumsAsset,
                        ExpectedAgentSha256: expectedAgentSha,
                        ExpectedFirmwareSha256: null,
                        ErrorMessage: $"Brak sumy kontrolnej SHA-256 dla pliku {firmwareAsset.Name} w pliku sum wydania.");
                }
            }

            return new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                currentAgentVersion,
                currentFirmwareVersion,
                LatestRelease: release,
                AgentAsset: isAgentNewer ? agentAsset : null,
                FirmwareAsset: isFirmwareNewer ? firmwareAsset : null,
                ChecksumsAsset: checksumsAsset,
                ExpectedAgentSha256: expectedAgentSha,
                ExpectedFirmwareSha256: expectedFwSha,
                ErrorMessage: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.Error,
                currentAgentVersion,
                currentFirmwareVersion,
                LatestRelease: null,
                AgentAsset: null,
                FirmwareAsset: null,
                ChecksumsAsset: null,
                ExpectedAgentSha256: null,
                ExpectedFirmwareSha256: null,
                ErrorMessage: $"Błąd sieciowy podczas sprawdzania aktualizacji: {ex.Message}");
        }
    }

    private async Task<Dictionary<string, string>> FetchChecksumsAsync(
        string downloadUrl,
        string expectedTagName,
        string expectedAssetName,
        CancellationToken cancellationToken)
    {
        using var response = await SendWithSafeRedirectsAsync(
            downloadUrl,
            expectedTagName,
            expectedAssetName,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        // Limit checksums file read to max 1MB
        var length = response.Content.Headers.ContentLength ?? 0;
        if (length > 1024 * 1024)
        {
            throw new InvalidOperationException("Plik sum kontrolnych przekracza dopuszczalny rozmiar.");
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ChecksumsParser.Parse(content);
    }

    public static ReleaseInfo? ParseReleaseJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    var parsed = ParseSingleRelease(element);
                    if (parsed is not null && !parsed.IsPrerelease && !parsed.IsDraft)
                    {
                        return parsed;
                    }
                }
                return null;
            }

            return ParseSingleRelease(root);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ReleaseInfo? ParseSingleRelease(JsonElement element)
    {
        if (!element.TryGetProperty("tag_name", out var tagProp))
        {
            return null;
        }

        var tagName = tagProp.GetString() ?? string.Empty;
        if (!UpdateVersionParser.TryParse(tagName, out var version))
        {
            return null;
        }

        var name = element.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? tagName : tagName;
        var body = element.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? string.Empty : string.Empty;
        var htmlUrl = element.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? string.Empty : string.Empty;
        var isPrerelease = element.TryGetProperty("prerelease", out var preProp) && preProp.GetBoolean();
        var isDraft = element.TryGetProperty("draft", out var draftProp) && draftProp.GetBoolean();

        DateTimeOffset publishedAt = DateTimeOffset.UtcNow;
        if (element.TryGetProperty("published_at", out var pubProp) && pubProp.GetString() is { } pubStr)
        {
            DateTimeOffset.TryParse(pubStr, out publishedAt);
        }

        var assets = new List<ReleaseAssetInfo>();
        if (element.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var assetElement in assetsProp.EnumerateArray())
            {
                var assetName = assetElement.TryGetProperty("name", out var anProp) ? anProp.GetString() : null;
                var downloadUrl = assetElement.TryGetProperty("browser_download_url", out var bdlProp) ? bdlProp.GetString() : null;
                var size = assetElement.TryGetProperty("size", out var sizeProp) ? sizeProp.GetInt64() : 0L;
                var contentType = assetElement.TryGetProperty("content_type", out var ctProp) ? ctProp.GetString() ?? string.Empty : string.Empty;

                if (!string.IsNullOrWhiteSpace(assetName) && !string.IsNullOrWhiteSpace(downloadUrl))
                {
                    assets.Add(new ReleaseAssetInfo(assetName, size, downloadUrl, contentType));
                }
            }
        }

        return new ReleaseInfo(
            tagName,
            version,
            name,
            body,
            publishedAt,
            isPrerelease,
            isDraft,
            htmlUrl,
            assets);
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }
}
