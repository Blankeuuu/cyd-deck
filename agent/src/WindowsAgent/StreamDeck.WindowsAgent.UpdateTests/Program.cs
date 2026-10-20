using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using StreamDeck.Host;
using StreamDeck.Protocol;
using StreamDeck.WindowsAgent.Updates;

namespace StreamDeck.WindowsAgent.UpdateTests;

internal static class Program
{
    private static int testsRun;
    private static int testsPassed;

    private static async Task Main()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        Console.WriteLine("=== CYD-DECK In-App Auto-Updates (Agent & Firmware) Test Suite ===");

        await RunTestAsync("UpdateVersionParser", () => Task.Run(TestUpdateVersionParser));
        await RunTestAsync("UpdateSecurityValidator", () => Task.Run(TestUpdateSecurityValidatorOriginAndWhitelist));
        await RunTestAsync("ChecksumsParser", () => Task.Run(TestChecksumsParser));
        await RunTestAsync("MagicBytesAndPreflightValidation", () => Task.Run(TestMagicBytesAndPreflightValidation));
        await RunTestAsync("UpdateReleaseClientWithFakeHttp", TestUpdateReleaseClientWithFakeHttpAsync);
        await RunTestAsync("UpdateStagingService", TestUpdateStagingServiceAsync);
        await RunTestAsync("RedirectTransportPolicy", TestRedirectTransportPolicyAsync);
        await RunTestAsync("UpdateInstallerLauncher", () => Task.Run(TestUpdateInstallerLauncher));
        await RunTestAsync("FirmwareTelemetryVerifierAndDialogSafeguards", TestFirmwareTelemetryVerifierAndDialogSafeguardsAsync);
        await RunTestAsync("UpdateManagerLifecycleAndIntegrityVerification", TestUpdateManagerLifecycleAndIntegrityVerificationAsync);

        Console.WriteLine($"\n=== All {testsPassed}/{testsRun} Auto-Update Tests PASSED ===");
    }

    private static async Task RunTestAsync(string testName, Func<Task> action)
    {
        Console.WriteLine($"\n-- Running: {testName} --");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await action().WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Test '{testName}' exceeded 10-second timeout (possible deadlock)");
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        testsRun++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"FAILED: {message}. Expected: '{expected}', Actual: '{actual}'");
        }
        testsPassed++;
        Console.WriteLine($"  [PASS] {message}");
    }

    private static void AssertTrue(bool condition, string message)
    {
        testsRun++;
        if (!condition)
        {
            throw new InvalidOperationException($"FAILED: {message}");
        }
        testsPassed++;
        Console.WriteLine($"  [PASS] {message}");
    }

    private static void TestUpdateVersionParser()
    {
        AssertTrue(UpdateVersionParser.TryParse("1.4.0", out var v1), "Parses 1.4.0");
        AssertEqual(new Version(1, 4, 0), v1, "Version is 1.4.0");

        AssertTrue(UpdateVersionParser.TryParse("v1.5.0", out var v2), "Parses v1.5.0 prefix");
        AssertEqual(new Version(1, 5, 0), v2, "Version is 1.5.0");

        AssertTrue(UpdateVersionParser.TryParse("v2.1.3-beta.1", out var v3), "Parses prerelease tag prefix");
        AssertEqual(new Version(2, 1, 3), v3, "Version is 2.1.3");

        AssertTrue(!UpdateVersionParser.TryParse("invalid-version", out _), "Rejects non-numeric version");
        AssertTrue(!UpdateVersionParser.TryParse("", out _), "Rejects empty version string");
        AssertTrue(!UpdateVersionParser.TryParse(null, out _), "Rejects null version string");

        // Comparison & Downgrade Prevention
        AssertTrue(UpdateVersionParser.IsNewer(new Version(1, 5, 0), new Version(1, 4, 0)), "1.5.0 is newer than 1.4.0");
        AssertTrue(!UpdateVersionParser.IsNewer(new Version(1, 4, 0), new Version(1, 4, 0)), "1.4.0 is not newer than 1.4.0");
        AssertTrue(!UpdateVersionParser.IsNewer(new Version(1, 3, 0), new Version(1, 4, 0)), "1.3.0 is not newer than 1.4.0 (downgrade prevention)");
    }

    private static void TestUpdateSecurityValidatorOriginAndWhitelist()
    {
        // API URLs
        AssertTrue(UpdateSecurityValidator.IsValidGitHubApiUrl("https://api.github.com/repos/Blankeuuu/cyd-deck/releases/latest"), "Valid GitHub API URL accepted");
        AssertTrue(UpdateSecurityValidator.IsValidGitHubApiUrl("https://api.github.com/repos/Blankeuuu/cyd-deck/releases"), "Valid GitHub releases list API URL accepted");
        AssertTrue(!UpdateSecurityValidator.IsValidGitHubApiUrl("http://api.github.com/repos/Blankeuuu/cyd-deck/releases/latest"), "Insecure HTTP API URL rejected");
        AssertTrue(!UpdateSecurityValidator.IsValidGitHubApiUrl("https://evil.com/repos/Blankeuuu/cyd-deck/releases"), "Untrusted host API URL rejected");
        AssertTrue(!UpdateSecurityValidator.IsValidGitHubApiUrl("https://api.github.com/repos/attacker/malicious/releases"), "Wrong repo API URL rejected");

        // Download URLs & Redirect Whitelist
        AssertTrue(UpdateSecurityValidator.IsValidGitHubDownloadUrl("https://github.com/Blankeuuu/cyd-deck/releases/download/v1.5.0/CYD-DECK-Setup-1.5.0-win-x64.exe"), "Valid GitHub download URL accepted");
        AssertTrue(UpdateSecurityValidator.IsValidGitHubDownloadUrl("https://objects.githubusercontent.com/github-production-release-asset-2e65be/12345/67890"), "GitHub CDN download redirect URL accepted");
        AssertTrue(!UpdateSecurityValidator.IsValidGitHubDownloadUrl("http://github.com/Blankeuuu/cyd-deck/releases/download/v1.5.0/test.exe"), "Insecure HTTP download rejected");
        AssertTrue(!UpdateSecurityValidator.IsValidGitHubDownloadUrl("https://malicious.org/CYD-DECK-Setup-1.5.0-win-x64.exe"), "Untrusted download host rejected");

        // Safe Filename & Path Traversal
        AssertTrue(UpdateSecurityValidator.IsSafeSanitizedFileName("CYD-DECK-Setup-1.5.0-win-x64.exe"), "Safe filename accepted");
        AssertTrue(!UpdateSecurityValidator.IsSafeSanitizedFileName("../CYD-DECK-Setup-1.5.0-win-x64.exe"), "Path traversal ../ rejected");
        AssertTrue(!UpdateSecurityValidator.IsSafeSanitizedFileName("subdir/CYD-DECK-Setup-1.5.0-win-x64.exe"), "Directory slash rejected");
        AssertTrue(!UpdateSecurityValidator.IsSafeSanitizedFileName(@"C:\Windows\System32\malware.exe"), "Absolute path rejected");

        // Windows Agent Asset Whitelist
        AssertTrue(UpdateSecurityValidator.IsAgentInstallerAsset("CYD-DECK-Setup-1.5.0-win-x64.exe"), "Standard agent installer accepted");
        AssertTrue(UpdateSecurityValidator.IsAgentInstallerAsset("CYD-DECK-Setup-2.0.1-rc.1-win-x64.exe"), "Pre-release agent installer accepted");
        AssertTrue(!UpdateSecurityValidator.IsAgentInstallerAsset("CYD-DECK-win-x64.zip"), "Zip file rejected for auto-installer");
        AssertTrue(!UpdateSecurityValidator.IsAgentInstallerAsset("Setup.exe"), "Non-whitelisted exe rejected");
        AssertTrue(!UpdateSecurityValidator.IsAgentInstallerAsset("CYD-DECK-Setup-1.5.0-arm64.exe"), "Non-x64 installer rejected");

        // Exact Tag/Version Binding for Agent Installer
        AssertTrue(UpdateSecurityValidator.IsAgentInstallerAssetForVersion("CYD-DECK-Setup-1.5.0-win-x64.exe", "1.5.0"), "Exact agent installer matching version 1.5.0 accepted");
        AssertTrue(UpdateSecurityValidator.IsAgentInstallerAssetForVersion("CYD-DECK-Setup-1.5.0-win-x64.exe", "v1.5.0"), "Exact agent installer matching tag v1.5.0 accepted");
        AssertTrue(!UpdateSecurityValidator.IsAgentInstallerAssetForVersion("CYD-DECK-Setup-1.4.0-win-x64.exe", "1.5.0"), "Agent installer with mismatching version rejected");

        // Firmware Asset Whitelist & Protections
        AssertTrue(UpdateSecurityValidator.IsFirmwareAsset("firmware-ST7789-1.5.0.bin"), "ST7789 OTA firmware binary accepted");
        AssertTrue(!UpdateSecurityValidator.IsFirmwareAsset("firmware-ST7789-1.5.0-full.bin"), "Full flash image explicitly rejected (0x0 bootloader image protection)");
        AssertTrue(!UpdateSecurityValidator.IsFirmwareAsset("firmware-ST7789-1.5.0.full.bin"), "Full dot flash image explicitly rejected");
        AssertTrue(!UpdateSecurityValidator.IsFirmwareAsset("firmware-ili9341-1.5.0.bin"), "Wrong board / display controller explicitly rejected");
        AssertTrue(!UpdateSecurityValidator.IsFirmwareAsset("firmware.bin"), "Ambiguous non-tagged binary rejected");

        // Exact Tag/Version Binding for Firmware
        AssertTrue(UpdateSecurityValidator.IsFirmwareAssetForVersion("firmware-ST7789-1.5.0.bin", "1.5.0"), "Exact firmware binary matching version 1.5.0 accepted");
        AssertTrue(UpdateSecurityValidator.IsFirmwareAssetForVersion("firmware-ST7789-1.5.0.bin", "v1.5.0"), "Exact firmware binary matching tag v1.5.0 accepted");
        AssertTrue(!UpdateSecurityValidator.IsFirmwareAssetForVersion("firmware-ST7789-1.4.0.bin", "1.5.0"), "Firmware binary with mismatching version rejected");

        // Checksums Whitelist
        AssertTrue(UpdateSecurityValidator.IsChecksumsAsset("SHA256SUMS.txt"), "SHA256SUMS.txt accepted");
        AssertTrue(UpdateSecurityValidator.IsChecksumsAsset("checksums.txt"), "checksums.txt accepted");
        AssertTrue(UpdateSecurityValidator.IsChecksumsAsset("manifest.json"), "manifest.json accepted");
        AssertTrue(!UpdateSecurityValidator.IsChecksumsAsset("other.txt"), "Non-checksum text file rejected");
    }

    private static void TestChecksumsParser()
    {
        var gnuText = @"
# SHA256 checksums for CYD-DECK 1.5.0
e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855  CYD-DECK-Setup-1.5.0-win-x64.exe
ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb *firmware-ST7789-1.5.0.bin
";

        var parsedGnu = ChecksumsParser.Parse(gnuText);
        AssertEqual(2, parsedGnu.Count, "Parsed 2 entries from GNU format");
        AssertEqual("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", parsedGnu["CYD-DECK-Setup-1.5.0-win-x64.exe"], "Agent hash matches");
        AssertEqual("ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb", parsedGnu["firmware-ST7789-1.5.0.bin"], "Firmware hash matches");

        var jsonText = @"{
  ""CYD-DECK-Setup-1.5.0-win-x64.exe"": ""e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"",
  ""firmware-ST7789-1.5.0.bin"": ""ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb""
}";
        var parsedJson = ChecksumsParser.Parse(jsonText);
        AssertEqual(2, parsedJson.Count, "Parsed 2 entries from JSON format");
        AssertEqual("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", parsedJson["CYD-DECK-Setup-1.5.0-win-x64.exe"], "JSON agent hash matches");
    }

    private static void TestMagicBytesAndPreflightValidation()
    {
        // Firmware Preflight
        var validFw = new byte[1024];
        validFw[0] = UpdateSecurityValidator.EspFirmwareMagic;
        AssertTrue(UpdateSecurityValidator.ValidateFirmwareMagicAndSize(validFw, out var fwErr), "Valid ESP firmware header accepted");
        AssertTrue(string.IsNullOrEmpty(fwErr), "No error on valid firmware magic");

        var invalidFw = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        AssertTrue(!UpdateSecurityValidator.ValidateFirmwareMagicAndSize(invalidFw, out var invalidFwErr), "Invalid firmware magic 0x00 rejected");
        AssertTrue(!string.IsNullOrEmpty(invalidFwErr), "Error returned for invalid firmware magic");

        // Firmware size limit (max ~1.93MB)
        var oversizedFw = new byte[ArduinoOtaFirmwareUpdater.MaxFirmwareBytes + 1024];
        oversizedFw[0] = UpdateSecurityValidator.EspFirmwareMagic;
        AssertTrue(!UpdateSecurityValidator.ValidateFirmwareMagicAndSize(oversizedFw, out var tooBigFwErr), "Oversized firmware binary rejected");
        AssertTrue(!string.IsNullOrEmpty(tooBigFwErr), "Error returned for oversized firmware binary");

        // Windows Agent Installer Preflight
        var validPe = new byte[] { 0x4D, 0x5A }; // 'MZ'
        AssertTrue(UpdateSecurityValidator.ValidateInstallerMagicAndSize(validPe, 1024 * 1024, out var peErr), "Valid PE MZ header accepted");
        AssertTrue(string.IsNullOrEmpty(peErr), "No error on valid PE header");

        var invalidPe = new byte[] { 0x7F, 0x45 }; // ELF magic
        AssertTrue(!UpdateSecurityValidator.ValidateInstallerMagicAndSize(invalidPe, 1024 * 1024, out var invalidPeErr), "Non-PE header rejected");
        AssertTrue(!string.IsNullOrEmpty(invalidPeErr), "Error returned for non-PE header");

        // Agent size limit (max 300MB)
        AssertTrue(!UpdateSecurityValidator.ValidateInstallerMagicAndSize(validPe, 400 * 1024 * 1024, out var tooBigAgentErr), "Oversized agent installer rejected");
        AssertTrue(!string.IsNullOrEmpty(tooBigAgentErr), "Error returned for oversized agent installer");
    }

    private static async Task TestUpdateReleaseClientWithFakeHttpAsync()
    {
        var handler = new FakeHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var updateClient = new UpdateReleaseClient(httpClient);

        var currentAgentVer = new Version(1, 4, 0);

        // Case 1: Already up-to-date
        handler.SetResponse(
            "https://api.github.com/repos/Blankeuuu/cyd-deck/releases/latest",
            @"{
                ""tag_name"": ""v1.4.0"",
                ""name"": ""CYD-DECK 1.4.0"",
                ""body"": ""Current release"",
                ""prerelease"": false,
                ""draft"": false,
                ""assets"": []
            }");

        var upToDateResult = await updateClient.CheckForUpdatesAsync(currentAgentVer, currentFirmwareVersion: "1.4.0");
        AssertEqual(UpdateCheckStatus.UpToDate, upToDateResult.Status, "Status is UpToDate when latest tag equals current");

        // Case 2: New release available with checksums
        var agentAssetBytes = CreateDummyInstallerBytes();
        var fwAssetBytes = CreateDummyFirmwareBytes();
        var agentHash = ComputeHexSha256(agentAssetBytes);
        var fwHash = ComputeHexSha256(fwAssetBytes);

        var checksumsContent = $"{agentHash}  CYD-DECK-Setup-1.5.0-win-x64.exe\n{fwHash}  firmware-ST7789-1.5.0.bin\n";

        handler.SetResponse(
            "https://api.github.com/repos/Blankeuuu/cyd-deck/releases/latest",
            @"{
                ""tag_name"": ""v1.5.0"",
                ""name"": ""CYD-DECK 1.5.0"",
                ""body"": ""New release"",
                ""prerelease"": false,
                ""draft"": false,
                ""assets"": [
                    {
                        ""name"": ""CYD-DECK-Setup-1.5.0-win-x64.exe"",
                        ""browser_download_url"": ""https://github.com/Blankeuuu/cyd-deck/releases/download/v1.5.0/CYD-DECK-Setup-1.5.0-win-x64.exe"",
                        ""size"": 1048576,
                        ""content_type"": ""application/octet-stream""
                    },
                    {
                        ""name"": ""firmware-ST7789-1.5.0.bin"",
                        ""browser_download_url"": ""https://github.com/Blankeuuu/cyd-deck/releases/download/v1.5.0/firmware-ST7789-1.5.0.bin"",
                        ""size"": 524288,
                        ""content_type"": ""application/octet-stream""
                    },
                    {
                        ""name"": ""SHA256SUMS.txt"",
                        ""browser_download_url"": ""https://github.com/Blankeuuu/cyd-deck/releases/download/v1.5.0/SHA256SUMS.txt"",
                        ""size"": 200,
                        ""content_type"": ""text/plain""
                    }
                ]
            }");

        handler.SetResponse("https://github.com/Blankeuuu/cyd-deck/releases/download/v1.5.0/SHA256SUMS.txt", checksumsContent);

        var updateAvailableResult = await updateClient.CheckForUpdatesAsync(currentAgentVer, currentFirmwareVersion: "1.4.0");
        AssertEqual(UpdateCheckStatus.UpdateAvailable, updateAvailableResult.Status, "Status is UpdateAvailable");
        AssertTrue(updateAvailableResult.HasAgentUpdate, "HasAgentUpdate is true");
        AssertTrue(updateAvailableResult.HasFirmwareUpdate, "HasFirmwareUpdate is true");
        AssertEqual(agentHash, updateAvailableResult.ExpectedAgentSha256, "ExpectedAgentSha256 extracted accurately");
        AssertEqual(fwHash, updateAvailableResult.ExpectedFirmwareSha256, "ExpectedFirmwareSha256 extracted accurately");

        // Case 3: Missing Checksums Asset -> ChecksumMissing (no silent security downgrade)
        handler.SetResponse(
            "https://api.github.com/repos/Blankeuuu/cyd-deck/releases/latest",
            @"{
                ""tag_name"": ""v1.5.0"",
                ""name"": ""CYD-DECK 1.5.0"",
                ""body"": ""Release without checksums"",
                ""prerelease"": false,
                ""draft"": false,
                ""assets"": [
                    {
                        ""name"": ""CYD-DECK-Setup-1.5.0-win-x64.exe"",
                        ""browser_download_url"": ""https://github.com/Blankeuuu/cyd-deck/releases/download/v1.5.0/CYD-DECK-Setup-1.5.0-win-x64.exe"",
                        ""size"": 1048576,
                        ""content_type"": ""application/octet-stream""
                    }
                ]
            }");

        var checksumMissingResult = await updateClient.CheckForUpdatesAsync(currentAgentVer);
        AssertEqual(UpdateCheckStatus.ChecksumMissing, checksumMissingResult.Status, "Status is ChecksumMissing when release has no checksum asset");

        // Case 4: Prerelease skipped
        handler.SetResponse(
            "https://api.github.com/repos/Blankeuuu/cyd-deck/releases/latest",
            @"{
                ""tag_name"": ""v2.0.0-beta1"",
                ""name"": ""CYD-DECK 2.0.0 Beta"",
                ""prerelease"": true,
                ""draft"": false,
                ""assets"": []
            }");

        var prereleaseResult = await updateClient.CheckForUpdatesAsync(currentAgentVer);
        AssertEqual(UpdateCheckStatus.UpToDate, prereleaseResult.Status, "Prerelease ignored, returns UpToDate");
    }

    private static async Task TestUpdateStagingServiceAsync()
    {
        var tempStaging = Path.Combine(Path.GetTempPath(), $"cyd-deck-staging-test-{Guid.NewGuid():N}");
        try
        {
            var handler = new FakeHttpMessageHandler();
            using var client = new HttpClient(handler);
            var stagingService = new UpdateStagingService(client, tempStaging);

            var installerBytes = CreateDummyInstallerBytes();
            var installerHash = ComputeHexSha256(installerBytes);
            var downloadUrl = "https://github.com/Blankeuuu/cyd-deck/releases/download/v1.5.0/CYD-DECK-Setup-1.5.0-win-x64.exe";
            handler.SetBinaryResponse(downloadUrl, installerBytes);

            var asset = new ReleaseAssetInfo("CYD-DECK-Setup-1.5.0-win-x64.exe", installerBytes.Length, downloadUrl, "application/octet-stream");

            // Success case
            var stageResult = await stagingService.StageAssetAsync(asset, installerHash, UpdateAssetType.WindowsAgentInstaller);
            AssertTrue(stageResult.Success, "Staging installer succeeded");
            AssertTrue(File.Exists(stageResult.StagedFilePath!), "Staged file exists at target location");
            AssertEqual(installerHash, stageResult.Sha256Hash, "Staged file SHA-256 matches expected");

            // Verify no leftover .tmp files
            var tmpFiles = Directory.GetFiles(tempStaging, "*.tmp");
            AssertEqual(0, tmpFiles.Length, "No temporary files left in staging directory");

            // Mismatched hash case
            var wrongHash = "0000000000000000000000000000000000000000000000000000000000000000";
            var badHashResult = await stagingService.StageAssetAsync(asset, wrongHash, UpdateAssetType.WindowsAgentInstaller);
            AssertTrue(!badHashResult.Success, "Staging with corrupted hash fails");
            AssertTrue(badHashResult.SafeError?.Contains("Naruszenie integralności") == true, "Safe integrity violation error returned");

            // Invalid magic bytes case (same declared size, valid hash, but corrupt MZ)
            var invalidMagicBytes = new byte[installerBytes.Length]; // Zeroed, no 'MZ'
            var invalidMagicHash = ComputeHexSha256(invalidMagicBytes);
            handler.SetBinaryResponse(downloadUrl, invalidMagicBytes);

            var invalidMagicResult = await stagingService.StageAssetAsync(asset, invalidMagicHash, UpdateAssetType.WindowsAgentInstaller);
            AssertTrue(!invalidMagicResult.Success, "Staging invalid PE magic fails preflight");
            AssertTrue(invalidMagicResult.SafeError?.Contains("sygnatury MZ") == true, "Safe preflight magic error returned");

            // Streaming size limit violation case: firmware exceeding max allowed size
            var oversizedFwUrl = "https://github.com/Blankeuuu/cyd-deck/releases/download/v1.5.0/firmware-ST7789-1.5.0.bin";
            var oversizedFwAsset = new ReleaseAssetInfo("firmware-ST7789-1.5.0.bin", 2_500_000, oversizedFwUrl, "application/octet-stream");
            var oversizedFwBytes = new byte[2_500_000];
            oversizedFwBytes[0] = UpdateSecurityValidator.EspFirmwareMagic;
            var oversizedFwHash = ComputeHexSha256(oversizedFwBytes);
            handler.SetBinaryResponse(oversizedFwUrl, oversizedFwBytes);

            var oversizedResult = await stagingService.StageAssetAsync(oversizedFwAsset, oversizedFwHash, UpdateAssetType.CYDFirmware);
            AssertTrue(!oversizedResult.Success, "Oversized firmware download rejected by streaming size limit");
            AssertTrue(oversizedResult.SafeError?.Contains("przekracza dopuszczalny limit") == true, "Safe size limit error message returned");
        }
        finally
        {
            if (Directory.Exists(tempStaging))
            {
                try { Directory.Delete(tempStaging, recursive: true); } catch { }
            }
        }
    }

    private static void TestUpdateInstallerLauncher()
    {
        ProcessStartInfo? capturedPsi = null;
        var exiterCalled = false;

        var tempInstaller = Path.Combine(Path.GetTempPath(), $"CYD-DECK-Setup-1.5.0-win-x64.exe");
        try
        {
            var validBytes = CreateDummyInstallerBytes();
            File.WriteAllBytes(tempInstaller, validBytes);

            // Case A: processLauncher returns null -> TryLaunchInstaller returns false, applicationExiter NOT called
            var failingLauncher = new UpdateInstallerLauncher(
                psi => { capturedPsi = psi; return null; },
                () => { exiterCalled = true; });

            var failSuccess = failingLauncher.TryLaunchInstaller(tempInstaller, out var failErr, silent: true, exitCurrentProcess: true);
            AssertTrue(!failSuccess, "TryLaunchInstaller returns false when processLauncher returns null");
            AssertTrue(!exiterCalled, "applicationExiter is NOT called when process launch fails");
            AssertTrue(!string.IsNullOrEmpty(failErr), "Error message provided when process launch fails");

            // Case B: processLauncher returns a non-null Process -> TryLaunchInstaller returns true, applicationExiter called
            capturedPsi = null;
            exiterCalled = false;
            var successLauncher = new UpdateInstallerLauncher(
                psi => { capturedPsi = psi; return Process.GetCurrentProcess(); },
                () => { exiterCalled = true; });

            var success = successLauncher.TryLaunchInstaller(tempInstaller, out var err, silent: true, exitCurrentProcess: true);
            AssertTrue(success, "TryLaunchInstaller returns true when process starts successfully");
            AssertTrue(string.IsNullOrEmpty(err), "No error on valid launch");
            AssertTrue(capturedPsi is not null, "ProcessStartInfo captured");
            AssertEqual(tempInstaller, capturedPsi?.FileName, "ProcessStartInfo FileName matches installer path");
            AssertTrue(capturedPsi?.Arguments.Contains("/SILENT") == true, "Silent argument passed to installer");
            AssertTrue(exiterCalled, "Application exiter invoked on successful launch");
            AssertTrue(capturedPsi?.Arguments.Contains("/NORESTART") == true, "Silent update never reboots Windows");
            AssertTrue(UpdateInstallerLauncher.BuildInstallerArguments(false).Contains("/NORESTART"), "Interactive update also prevents host reboot");

            // Case C: Non-whitelisted file rejected
            var badNameFile = Path.Combine(Path.GetTempPath(), "setup.exe");
            File.WriteAllBytes(badNameFile, validBytes);
            try
            {
                var badSuccess = successLauncher.TryLaunchInstaller(badNameFile, out var badErr);
                AssertTrue(!badSuccess, "Non-whitelisted executable rejected");
                AssertTrue(!string.IsNullOrEmpty(badErr), "Error returned for non-whitelisted executable");
            }
            finally
            {
                File.Delete(badNameFile);
            }
        }
        finally
        {
            if (File.Exists(tempInstaller))
            {
                File.Delete(tempInstaller);
            }
        }
    }

    private static async Task TestFirmwareTelemetryVerifierAndDialogSafeguardsAsync()
    {
        var state = new AgentState();

        // Direct test of FirmwareTelemetryVerifier (isolated from WinForms window context)
        var updateStarted = DateTimeOffset.UtcNow.AddSeconds(1);

        // 1. Simulate stale telemetry before update
        state.UpdateDeviceInfo(new DeviceInfoMessage(
            "1.5.0",
            10,
            new DeviceWifiInfo(true, "SSID", -50, "192.168.1.100"),
            "PC",
            new DeviceStorageInfo(true, true, 1000, 500),
            new DeviceHeapInfo(100, 50, 20)));

        var resultStale = await FirmwareTelemetryVerifier.WaitForVersionVerificationAsync(
            state, updateStarted, "1.5.0", "1.4.0", TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10));
        AssertTrue(resultStale is null, "Stale telemetry before update is rejected");

        // 2. Simulate fresh telemetry with unexpected version
        updateStarted = DateTimeOffset.UtcNow.AddSeconds(-2);
        state.UpdateDeviceInfo(new DeviceInfoMessage(
            "1.4.5",
            15,
            new DeviceWifiInfo(true, "SSID", -50, "192.168.1.100"),
            "PC",
            new DeviceStorageInfo(true, true, 1000, 500),
            new DeviceHeapInfo(100, 50, 20)));

        var resultWrong = await FirmwareTelemetryVerifier.WaitForVersionVerificationAsync(
            state, updateStarted, "1.5.0", "1.4.0", TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10));
        AssertTrue(resultWrong is null, "Mismatching firmware version in fresh telemetry is rejected");

        // 3. Simulate exact matching fresh version
        state.UpdateDeviceInfo(new DeviceInfoMessage(
            "1.5.0",
            20,
            new DeviceWifiInfo(true, "SSID", -50, "192.168.1.100"),
            "PC",
            new DeviceStorageInfo(true, true, 1000, 500),
            new DeviceHeapInfo(100, 50, 20)));

        var resultValid = await FirmwareTelemetryVerifier.WaitForVersionVerificationAsync(
            state, updateStarted, "1.5.0", "1.4.0", TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10));
        AssertEqual("1.5.0", resultValid, "Exact expected firmware version in fresh telemetry is verified");

        // 4. Test Dialog FormClosing safeguard mid-update
        var tempFw = Path.Combine(Path.GetTempPath(), "firmware-ST7789-1.5.0.bin");
        File.WriteAllBytes(tempFw, CreateDummyFirmwareBytes());
        try
        {
            using var dialog = new FirmwareUpdateDialog(
                state,
                tempFw,
                deviceIp: "192.168.1.100",
                previousVersion: "1.4.0",
                expectedVersion: "1.5.0");

            dialog.SetIsUpdatingForTests(true);
            var closeArgs = new FormClosingEventArgs(CloseReason.UserClosing, cancel: false);
            typeof(FirmwareUpdateDialog).GetMethod("OnFormClosing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(dialog, [closeArgs]);
            AssertTrue(closeArgs.Cancel, "Form close is cancelled when firmware update is actively in progress");

            dialog.SetIsUpdatingForTests(false);
            var allowedCloseArgs = new FormClosingEventArgs(CloseReason.UserClosing, cancel: false);
            typeof(FirmwareUpdateDialog).GetMethod("OnFormClosing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(dialog, [allowedCloseArgs]);
            AssertTrue(!allowedCloseArgs.Cancel, "Form close is permitted when update is completed or idle");
        }
        finally
        {
            if (File.Exists(tempFw))
            {
                File.Delete(tempFw);
            }
        }
    }

    private static async Task TestUpdateManagerLifecycleAndIntegrityVerificationAsync()
    {
        var tempStaging = Path.Combine(Path.GetTempPath(), $"cyd-deck-mgr-test-{Guid.NewGuid():N}");
        try
        {
            var handler = new FakeHttpMessageHandler();
            using var httpClient = new HttpClient(handler);
            var releaseClient = new UpdateReleaseClient(httpClient);
            var stagingService = new UpdateStagingService(httpClient, tempStaging);

            ProcessStartInfo? capturedPsi = null;
            var launcher = new UpdateInstallerLauncher(
                psi => { capturedPsi = psi; return Process.GetCurrentProcess(); },
                () => { });

            using var manager = new UpdateManager(
                new Version(1, 4, 0),
                releaseClient,
                stagingService,
                launcher);

            // 1. Throttling
            AssertTrue(manager.ShouldCheckAutomatically(TimeSpan.FromHours(4)), "Initial check is eligible (never checked before)");

            var installerBytes = CreateDummyInstallerBytes();
            var installerHash = ComputeHexSha256(installerBytes);
            var fwBytes = CreateDummyFirmwareBytes();
            var fwHash = ComputeHexSha256(fwBytes);

            var checksums = $"{installerHash}  CYD-DECK-Setup-1.5.0-win-x64.exe\n{fwHash}  firmware-ST7789-1.5.0.bin\n";

            handler.SetResponse(
                "https://api.github.com/repos/Blankeuuu/cyd-deck/releases/latest",
                @"{
                    ""tag_name"": ""v1.5.0"",
                    ""name"": ""CYD-DECK 1.5.0"",
                    ""prerelease"": false,
                    ""draft"": false,
                    ""assets"": [
                        {
                            ""name"": ""CYD-DECK-Setup-1.5.0-win-x64.exe"",
                            ""browser_download_url"": ""https://github.com/Blankeuuu/cyd-deck/releases/download/v1.5.0/CYD-DECK-Setup-1.5.0-win-x64.exe"",
                            ""size"": 2048,
                            ""content_type"": ""application/octet-stream""
                        },
                        {
                            ""name"": ""firmware-ST7789-1.5.0.bin"",
                            ""browser_download_url"": ""https://github.com/Blankeuuu/cyd-deck/releases/download/v1.5.0/firmware-ST7789-1.5.0.bin"",
                            ""size"": 4096,
                            ""content_type"": ""application/octet-stream""
                        },
                        {
                            ""name"": ""SHA256SUMS.txt"",
                            ""browser_download_url"": ""https://github.com/Blankeuuu/cyd-deck/releases/download/v1.5.0/SHA256SUMS.txt"",
                            ""size"": 200,
                            ""content_type"": ""text/plain""
                        }
                    ]
                }");
            handler.SetResponse("https://github.com/Blankeuuu/cyd-deck/releases/download/v1.5.0/SHA256SUMS.txt", checksums);
            handler.SetBinaryResponse("https://github.com/Blankeuuu/cyd-deck/releases/download/v1.5.0/CYD-DECK-Setup-1.5.0-win-x64.exe", installerBytes);
            handler.SetBinaryResponse("https://github.com/Blankeuuu/cyd-deck/releases/download/v1.5.0/firmware-ST7789-1.5.0.bin", fwBytes);

            var releaseJson = handler.GetTextResponse("https://api.github.com/repos/Blankeuuu/cyd-deck/releases/latest");
            var checkResult = await manager.CheckForUpdatesAsync("1.4.0");
            AssertEqual(UpdateCheckStatus.UpdateAvailable, checkResult.Status, "Check returns UpdateAvailable");
            AssertTrue(!manager.ShouldCheckAutomatically(TimeSpan.FromHours(4)), "Subsequent check is throttled within 4 hours");

            // 2. Stage Agent & Firmware
            var agentStage = await manager.StageAgentUpdateAsync();
            AssertTrue(agentStage.Success, "Agent staging succeeds");
            AssertTrue(manager.StagedAgentInstallerPath is not null, "StagedAgentInstallerPath populated");

            var fwProgressValues = new List<UpdateStagingProgress>();
            var fwStage = await manager.StageFirmwareUpdateAsync(new Progress<UpdateStagingProgress>(fwProgressValues.Add));
            AssertTrue(fwStage.Success, "Firmware staging succeeds");
            AssertTrue(manager.StagedFirmwarePath is not null, "StagedFirmwarePath populated");
            AssertTrue(fwProgressValues.Count > 0, "Firmware staging reports progress");

            AssertTrue(manager.TryGetVerifiedFirmware(out _, out var verifiedVersion, out _), "Firmware rehash accepts original image");
            AssertEqual("v1.5.0", verifiedVersion, "Verified firmware carries its captured release version");
            var changedFirmware = (byte[])fwBytes.Clone();
            changedFirmware[100] ^= 0xff;
            File.WriteAllBytes(manager.StagedFirmwarePath!, changedFirmware);
            AssertTrue(!manager.TryGetVerifiedFirmware(out _, out _, out _), "Tampered firmware cannot open OTA flow");
            AssertTrue(manager.StagedFirmwarePath is null, "Tampered firmware path invalidated");

            // 3. Invalidation upon fresh check
            await manager.CheckForUpdatesAsync("1.4.0");
            AssertTrue(manager.StagedAgentInstallerPath is null, "Previous staged agent path invalidated upon new check");
            AssertTrue(manager.StagedFirmwarePath is null, "Previous staged firmware path invalidated upon new check");

            // Stage again for integrity test
            await manager.StageAgentUpdateAsync();
            AssertTrue(manager.StagedAgentInstallerPath is not null, "Re-staged agent installer");

            // 4. Pre-install hash tampering prevention
            var tamperedBytes = CreateDummyInstallerBytes();
            tamperedBytes[50] = (byte)(tamperedBytes[50] ^ 0xFF); // Flip a byte
            File.WriteAllBytes(manager.StagedAgentInstallerPath!, tamperedBytes);

            var installTampered = manager.TryInstallStagedAgent(silent: true, out var tamperErr);
            AssertTrue(!installTampered, "Installation rejected when file hash was tampered on disk");
            AssertTrue(tamperErr?.Contains("Suma kontrolna instalatora na dysku uległa zmianie") == true, "Safe tamper error returned");
            AssertTrue(manager.StagedAgentInstallerPath is null, "Tampered staged path invalidated immediately");

            // Restage valid and install
            await manager.StageAgentUpdateAsync();
            var installValid = manager.TryInstallStagedAgent(silent: true, out var validErr);
            AssertTrue(installValid, "Installation succeeds when hash matches");
            AssertTrue(string.IsNullOrEmpty(validErr), "No error on valid install");
            AssertTrue(capturedPsi is not null, "Process launcher triggered with valid installer");

            foreach (var errorCheck in new[] { true, false })
            {
                await manager.CheckForUpdatesAsync("1.4.0");
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                handler.Interceptor = request =>
                {
                    if (!request.RequestUri!.AbsolutePath.EndsWith(".exe")) return null;
                    started.TrySetResult();
                    return pending.Task;
                };
                var stage = manager.StageAgentUpdateAsync();
                await started.Task;
                handler.SetResponse("https://api.github.com/repos/Blankeuuu/cyd-deck/releases/latest", errorCheck
                    ? "invalid JSON"
                    : "{\"tag_name\":\"v1.6.0\",\"name\":\"1.6.0\",\"assets\":[]}");
                await manager.CheckForUpdatesAsync("1.4.0");
                if (errorCheck) AssertEqual(UpdateCheckStatus.Error, manager.LastCheckResult!.Status, "New check returns error while stage is pending");
                pending.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(installerBytes) });
                AssertTrue(!(await stage).Success, "Old stage completion rejected after newer check");
                AssertTrue(manager.StagedAgentInstallerPath is null, "Old stage cannot restore invalidated installer path");
                capturedPsi = null;
                AssertTrue(!manager.TryInstallStagedAgent(true, out _), "Install forbidden after stale stage completion");
                AssertTrue(capturedPsi is null, "Stale stage never invokes process launcher");
                handler.Interceptor = null;
                handler.SetResponse("https://api.github.com/repos/Blankeuuu/cyd-deck/releases/latest", releaseJson);
            }
        }
        finally
        {
            if (Directory.Exists(tempStaging))
            {
                try { Directory.Delete(tempStaging, recursive: true); } catch { }
            }
        }
    }

    private static async Task TestRedirectTransportPolicyAsync()
    {
        using var productionHandler = UpdateStagingService.CreateDownloadHandler();
        AssertTrue(!productionHandler.AllowAutoRedirect, "Production HTTP adapter disables automatic redirects");
        var handler = new FakeHttpMessageHandler();
        using var client = new HttpClient(handler);
        var directory = Path.Combine(Path.GetTempPath(), $"cyd-redirect-{Guid.NewGuid():N}");
        var asset = new ReleaseAssetInfo("CYD-DECK-Setup-1.5.0-win-x64.exe", 2048,
            "https://github.com/Blankeuuu/cyd-deck/releases/download/v1.5.0/CYD-DECK-Setup-1.5.0-win-x64.exe", "application/octet-stream");
        try
        {
            using (var service = new UpdateStagingService(client, directory))
            {
                foreach (var destination in new[] { "https://evil.example/setup.exe", "http://github.com/setup.exe" })
                {
                    var requests = 0;
                    handler.Interceptor = request =>
                    {
                        requests++;
                        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                        response.Headers.Location = new Uri(destination);
                        return Task.FromResult(response);
                    };
                    var result = await service.StageAssetAsync(asset, ComputeHexSha256(CreateDummyInstallerBytes()), UpdateAssetType.WindowsAgentInstaller);
                    AssertTrue(!result.Success, "Unsafe redirect rejected by staging send path");
                    AssertEqual(1, requests, "Untrusted redirect destination never requested");
                }
                var safeRequests = 0;
                handler.Interceptor = request =>
                {
                    safeRequests++;
                    if (safeRequests == 1)
                    {
                        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                        response.Headers.Location = new Uri("https://objects.githubusercontent.com/asset");
                        return Task.FromResult(response);
                    }
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(CreateDummyInstallerBytes()) });
                };
                AssertTrue((await service.StageAssetAsync(asset, ComputeHexSha256(CreateDummyInstallerBytes()), UpdateAssetType.WindowsAgentInstaller)).Success,
                    "Trusted GitHub CDN redirect stages successfully");
                AssertEqual(2, safeRequests, "Safe redirect followed manually");
            }
            handler.Interceptor = null;
            using var responseAfterDispose = await client.GetAsync(asset.DownloadUrl);
            AssertEqual(HttpStatusCode.NotFound, responseAfterDispose.StatusCode, "Injected HttpClient remains caller-owned after disposal");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static byte[] CreateDummyInstallerBytes()
    {
        var bytes = new byte[2048];
        bytes[0] = 0x4D; // 'M'
        bytes[1] = 0x5A; // 'Z'
        for (var i = 2; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i % 256);
        }
        return bytes;
    }

    private static byte[] CreateDummyFirmwareBytes()
    {
        var bytes = new byte[4096];
        bytes[0] = UpdateSecurityValidator.EspFirmwareMagic; // 0xE9
        for (var i = 1; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i % 256);
        }
        return bytes;
    }

    private static string ComputeHexSha256(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }
}

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (string Content, string ContentType)> textResponses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (byte[] Bytes, string ContentType)> binaryResponses = new(StringComparer.OrdinalIgnoreCase);

    public void SetResponse(string url, string content, string contentType = "application/json")
    {
        textResponses[url] = (content, contentType);
    }

    public void SetBinaryResponse(string url, byte[] bytes, string contentType = "application/octet-stream")
    {
        binaryResponses[url] = (bytes, contentType);
    }

    public Func<HttpRequestMessage, Task<HttpResponseMessage>?>? Interceptor { get; set; }
    public string GetTextResponse(string url) => textResponses[url].Content;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var intercepted = Interceptor?.Invoke(request);
        if (intercepted is not null) return intercepted;
        var url = request.RequestUri?.ToString() ?? string.Empty;

        if (textResponses.TryGetValue(url, out var textResp))
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(textResp.Content, Encoding.UTF8, textResp.ContentType)
            };
            return Task.FromResult(msg);
        }

        if (binaryResponses.TryGetValue(url, out var binResp))
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(binResp.Bytes)
            };
            msg.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(binResp.ContentType);
            return Task.FromResult(msg);
        }

        var notFound = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("Not found in fake HTTP handler", Encoding.UTF8, "text/plain")
        };
        return Task.FromResult(notFound);
    }
}
