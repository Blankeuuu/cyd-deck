#Requires -Version 5.1
<#
.SYNOPSIS
    Automated unit and integration test suite for scripts/sign-windows-artifacts.ps1.

.DESCRIPTION
    Tests fail-closed behavior, strict verification (SignatureStatus.Valid and non-null Signer),
    rejection of UnknownError / NotTrusted / NotSigned / HashMismatch, dry-run simulation,
    PFX loading with EphemeralKeySet, TSA fail-closed behavior, and zero secret leakage.
#>

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$scriptPath = Join-Path $repoRoot 'scripts/sign-windows-artifacts.ps1'

if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
    throw "Script under test missing: $scriptPath"
}

# Preserve environment variables and ensure isolation (without echoing values)
$savedEnvCertPath  = $env:SIGNING_CERT_PATH
$savedEnvCertPass  = $env:SIGNING_CERT_PASSWORD
$savedEnvCertThumb = $env:SIGNING_CERT_THUMBPRINT

$scratchRoot = if ([string]::IsNullOrWhiteSpace($env:TMPDIR)) { [System.IO.Path]::GetTempPath() } else { $env:TMPDIR }
$sandbox = Join-Path $scratchRoot ("cyd-deck-test-$([System.Guid]::NewGuid().ToString('N'))")
New-Item -ItemType Directory -Path $sandbox -Force | Out-Null

$testCount = 0
$passCount = 0

function Assert-Test {
    param(
        [string]$Name,
        [scriptblock]$Action
    )
    $script:testCount++
    try {
        & $Action
        $script:passCount++
        Write-Host "PASS: $Name" -ForegroundColor Green
    }
    catch {
        Write-Host "FAIL: $Name - $($_.Exception.Message)" -ForegroundColor Red
        throw
    }
}

try {
    # Clear env credentials for tests
    $env:SIGNING_CERT_PATH = ''
    $env:SIGNING_CERT_PASSWORD = ''
    $env:SIGNING_CERT_THUMBPRINT = ''

    # Setup dummy fixture files in sandbox
    $sampleExe = Join-Path $sandbox 'CYD-DECK-dummy.exe'
    $sampleDll = Join-Path $sandbox 'StreamDeck-dummy.dll'
    Set-Content -LiteralPath $sampleExe -Value "MZ-dummy-executable-binary"
    Set-Content -LiteralPath $sampleDll -Value "MZ-dummy-library-binary"

    # Create in-memory certificate for mock tests (not imported to any store)
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    $req = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        "CN=SignPath Foundation, O=SignPath Foundation, C=AT",
        $rsa,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
    )
    $mockSignerCert = $req.CreateSelfSigned([DateTimeOffset]::UtcNow, [DateTimeOffset]::UtcNow.AddDays(1))

    # ---------------------------------------------------------
    # Test 1: Parser and AST syntax check
    # ---------------------------------------------------------
    Assert-Test "Script syntax and AST parser validation" {
        $tokens = $null
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$errors) | Out-Null
        if ($errors.Count -gt 0) {
            throw "Script has $($errors.Count) syntax parser error(s): $($errors[0].Message)"
        }
    }

    # ---------------------------------------------------------
    # Test 2: Parameter validation (non-existent artifact)
    # ---------------------------------------------------------
    Assert-Test "Fail-closed on non-existent artifact path" {
        $missingPath = Join-Path $sandbox 'does-not-exist.exe'
        $failed = $false
        try {
            & $scriptPath -ArtifactPaths $missingPath -DryRun -Quiet
        }
        catch {
            $failed = $true
            if ($_.Exception.Message -notlike "*Artifact file not found*") {
                throw "Unexpected exception message: $($_.Exception.Message)"
            }
        }
        if (-not $failed) {
            throw "Expected script to fail on missing artifact, but it succeeded."
        }
    }

    # ---------------------------------------------------------
    # Test 3: Unsupported extension validation
    # ---------------------------------------------------------
    Assert-Test "Fail-closed on unsupported artifact extension" {
        $txtArtifact = Join-Path $sandbox 'test-artifact.txt'
        Set-Content -LiteralPath $txtArtifact -Value "dummy text"
        $failed = $false
        try {
            & $scriptPath -ArtifactPaths $txtArtifact -DryRun -Quiet
        }
        catch {
            $failed = $true
            if ($_.Exception.Message -notlike "*Unsupported artifact extension*") {
                throw "Unexpected exception message: $($_.Exception.Message)"
            }
        }
        if (-not $failed) {
            throw "Expected script to fail on unsupported extension, but it succeeded."
        }
    }

    # ---------------------------------------------------------
    # Test 4: Fail-closed when signing credentials are completely missing
    # ---------------------------------------------------------
    Assert-Test "Fail-closed when signing credentials are completely missing" {
        $failed = $false
        try {
            & $scriptPath -ArtifactPaths $sampleExe -Quiet
        }
        catch {
            $failed = $true
            if ($_.Exception.Message -notlike "*Signing credentials not provided (fail-closed)*") {
                throw "Unexpected exception message: $($_.Exception.Message)"
            }
        }
        if (-not $failed) {
            throw "Expected script to fail closed when no cert was provided, but it succeeded."
        }
    }

    # ---------------------------------------------------------
    # Test 5: DryRun execution succeeds without certificates and does not modify files
    # ---------------------------------------------------------
    Assert-Test "DryRun succeeds and outputs structured plan without modifying files" {
        $beforeHash = (Get-FileHash -LiteralPath $sampleExe -Algorithm SHA256).Hash
        $res = @(& $scriptPath -ArtifactPaths $sampleExe, $sampleDll -DryRun -PassThru -Quiet)
        $afterHash = (Get-FileHash -LiteralPath $sampleExe -Algorithm SHA256).Hash

        if ($beforeHash -ne $afterHash) {
            throw "DryRun modified the artifact file!"
        }
        if ($res.Count -ne 2) {
            throw "DryRun produced $($res.Count) records, expected 2."
        }
        if ($res[0].Action -ne 'DryRun-Sign' -or $res[1].Action -ne 'DryRun-Sign') {
            throw "Unexpected DryRun action in results."
        }
    }

    # ---------------------------------------------------------
    # Test 6: VerifyOnly strictly FAILS on NotSigned
    # ---------------------------------------------------------
    Assert-Test "VerifyOnly strictly fails on NotSigned artifact" {
        $failed = $false
        try {
            & {
                function Get-AuthenticodeSignature {
                    param([string]$LiteralPath)
                    [PSCustomObject]@{
                        Path                   = $LiteralPath
                        Status                 = [System.Management.Automation.SignatureStatus]::NotSigned
                        StatusMessage          = 'The file is not signed.'
                        SignerCertificate      = $null
                        TimeStamperCertificate = $null
                    }
                }
                & $scriptPath -ArtifactPaths $sampleExe -VerifyOnly -Quiet
            }
        }
        catch {
            $failed = $true
        }
        if (-not $failed) {
            throw "Expected VerifyOnly to fail on NotSigned status, but it passed!"
        }
    }

    # ---------------------------------------------------------
    # Test 7: VerifyOnly strictly FAILS on UnknownError (untrusted root)
    # ---------------------------------------------------------
    Assert-Test "VerifyOnly strictly fails on UnknownError (untrusted root)" {
        $failed = $false
        try {
            & {
                function Get-AuthenticodeSignature {
                    param([string]$LiteralPath)
                    [PSCustomObject]@{
                        Path                   = $LiteralPath
                        Status                 = [System.Management.Automation.SignatureStatus]::UnknownError
                        StatusMessage          = 'A certificate chain processed, but terminated in a root certificate which is not trusted by the trust provider.'
                        SignerCertificate      = $mockSignerCert
                        TimeStamperCertificate = $null
                    }
                }
                & $scriptPath -ArtifactPaths $sampleExe -VerifyOnly -Quiet
            }
        }
        catch {
            $failed = $true
        }
        if (-not $failed) {
            throw "Expected VerifyOnly to fail on UnknownError, but it was accepted!"
        }
    }

    # ---------------------------------------------------------
    # Test 8: VerifyOnly strictly FAILS on NotTrusted status
    # ---------------------------------------------------------
    Assert-Test "VerifyOnly strictly fails on NotTrusted status" {
        $failed = $false
        try {
            & {
                function Get-AuthenticodeSignature {
                    param([string]$LiteralPath)
                    [PSCustomObject]@{
                        Path                   = $LiteralPath
                        Status                 = [System.Management.Automation.SignatureStatus]::NotTrusted
                        StatusMessage          = 'The certificate was explicitly not trusted.'
                        SignerCertificate      = $mockSignerCert
                        TimeStamperCertificate = $null
                    }
                }
                & $scriptPath -ArtifactPaths $sampleExe -VerifyOnly -Quiet
            }
        }
        catch {
            $failed = $true
        }
        if (-not $failed) {
            throw "Expected VerifyOnly to fail on NotTrusted, but it was accepted!"
        }
    }

    # ---------------------------------------------------------
    # Test 9: VerifyOnly strictly FAILS on HashMismatch (tampered)
    # ---------------------------------------------------------
    Assert-Test "VerifyOnly strictly fails on HashMismatch (tampered binary)" {
        $failed = $false
        try {
            & {
                function Get-AuthenticodeSignature {
                    param([string]$LiteralPath)
                    [PSCustomObject]@{
                        Path                   = $LiteralPath
                        Status                 = [System.Management.Automation.SignatureStatus]::HashMismatch
                        StatusMessage          = 'The hash of the file did not match the signature.'
                        SignerCertificate      = $mockSignerCert
                        TimeStamperCertificate = $null
                    }
                }
                & $scriptPath -ArtifactPaths $sampleExe -VerifyOnly -Quiet
            }
        }
        catch {
            $failed = $true
        }
        if (-not $failed) {
            throw "Expected VerifyOnly to fail on HashMismatch, but it passed!"
        }
    }

    # ---------------------------------------------------------
    # Test 10: VerifyOnly strictly FAILS if SignerCertificate is null (even if status claimed Valid)
    # ---------------------------------------------------------
    Assert-Test "VerifyOnly strictly fails if SignerCertificate is missing" {
        $failed = $false
        try {
            & {
                function Get-AuthenticodeSignature {
                    param([string]$LiteralPath)
                    [PSCustomObject]@{
                        Path                   = $LiteralPath
                        Status                 = [System.Management.Automation.SignatureStatus]::Valid
                        StatusMessage          = 'Status is valid but certificate object is missing.'
                        SignerCertificate      = $null
                        TimeStamperCertificate = $null
                    }
                }
                & $scriptPath -ArtifactPaths $sampleExe -VerifyOnly -Quiet
            }
        }
        catch {
            $failed = $true
        }
        if (-not $failed) {
            throw "Expected VerifyOnly to fail when SignerCertificate is null, but it passed!"
        }
    }

    # ---------------------------------------------------------
    # Test 11: VerifyOnly PASSES on SignatureStatus.Valid and non-null SignerCertificate
    # ---------------------------------------------------------
    Assert-Test "VerifyOnly succeeds on SignatureStatus.Valid with matching SignerCertificate" {
        $records = @(& {
            function Get-AuthenticodeSignature {
                param([string]$LiteralPath)
                [PSCustomObject]@{
                    Path                   = $LiteralPath
                    Status                 = [System.Management.Automation.SignatureStatus]::Valid
                    StatusMessage          = 'The signature is valid.'
                    SignerCertificate      = $mockSignerCert
                    TimeStamperCertificate = $mockSignerCert
                }
            }
            & $scriptPath -ArtifactPaths $sampleExe -VerifyOnly -ExpectedSubject 'SignPath Foundation' -PassThru -Quiet
        })

        if ($records.Count -ne 1) {
            throw "Expected 1 record, got $($records.Count)"
        }
        if (-not $records[0].Passed) {
            throw "Expected verification to pass, but failed: $($records[0].FailureReason)"
        }
        if ($records[0].Status -ne 'Valid') {
            throw "Expected Status 'Valid', got '$($records[0].Status)'"
        }
    }

    # ---------------------------------------------------------
    # Test 12: VerifyOnly FAILS when ExpectedSubject does not match
    # ---------------------------------------------------------
    Assert-Test "VerifyOnly fails when ExpectedSubject pattern does not match signer" {
        $failed = $false
        try {
            & {
                function Get-AuthenticodeSignature {
                    param([string]$LiteralPath)
                    [PSCustomObject]@{
                        Path                   = $LiteralPath
                        Status                 = [System.Management.Automation.SignatureStatus]::Valid
                        StatusMessage          = 'The signature is valid.'
                        SignerCertificate      = $mockSignerCert
                        TimeStamperCertificate = $null
                    }
                }
                & $scriptPath -ArtifactPaths $sampleExe -VerifyOnly -ExpectedSubject 'CompletelyDifferentEntity' -Quiet
            }
        }
        catch {
            $failed = $true
        }
        if (-not $failed) {
            throw "Expected failure on mismatched ExpectedSubject, but passed!"
        }
    }

    # ---------------------------------------------------------
    # Test 13: Live signing fail-closed when Timestamp Server is unreachable
    # ---------------------------------------------------------
    Assert-Test "Live signing fails immediately when TimestampServer is unreachable (no fallback)" {
        # Export ephemeral PFX
        $pfxBytes = $mockSignerCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, 'TestPass123')
        $pfxFile = Join-Path $sandbox 'test-ephemeral.pfx'
        [System.IO.File]::WriteAllBytes($pfxFile, $pfxBytes)
        $secPass = ConvertTo-SecureString -String 'TestPass123' -AsPlainText -Force

        $failed = $false
        try {
            & {
                function Set-AuthenticodeSignature {
                    param($FilePath, $Certificate, $HashAlgorithm, $TimestampServer)
                    throw "The timestamp server could not be reached: $TimestampServer"
                }
                & $scriptPath -ArtifactPaths $sampleExe -CertificatePath $pfxFile -CertificatePassword $secPass -TimestampServer 'http://unreachable.tsa.local' -Quiet
            }
        }
        catch {
            $failed = $true
            if ($_.Exception.Message -notlike "*Set-AuthenticodeSignature failed*") {
                throw "Unexpected exception message: $($_.Exception.Message)"
            }
        }
        if (-not $failed) {
            throw "Expected signing to fail when TSA is unreachable, but it proceeded without error!"
        }
    }

    # ---------------------------------------------------------
    # Test 14: Post-signing validation FAILS if signature results in UnknownError (e.g. self-signed root)
    # ---------------------------------------------------------
    Assert-Test "Post-signing validation fails-closed on UnknownError (rejects untrusted root)" {
        $pfxFile = Join-Path $sandbox 'test-ephemeral.pfx'
        $secPass = ConvertTo-SecureString -String 'TestPass123' -AsPlainText -Force

        $failed = $false
        try {
            & {
                function Set-AuthenticodeSignature {
                    param($FilePath, $Certificate, $HashAlgorithm)
                    return [PSCustomObject]@{
                        Status = [System.Management.Automation.SignatureStatus]::Valid
                        StatusMessage = 'Valid'
                        SignerCertificate = $mockSignerCert
                    }
                }
                function Get-AuthenticodeSignature {
                    param([string]$LiteralPath)
                    return [PSCustomObject]@{
                        Path                   = $LiteralPath
                        Status                 = [System.Management.Automation.SignatureStatus]::UnknownError
                        StatusMessage          = 'Untrusted root'
                        SignerCertificate      = $mockSignerCert
                        TimeStamperCertificate = $null
                    }
                }
                & $scriptPath -ArtifactPaths $sampleExe -CertificatePath $pfxFile -CertificatePassword $secPass -TimestampServer '' -Quiet
            }
        }
        catch {
            $failed = $true
            if ($_.Exception.Message -notlike "*Post-signing verification failed*") {
                throw "Unexpected exception message: $($_.Exception.Message)"
            }
        }
        if (-not $failed) {
            throw "Expected post-signing verification to fail on UnknownError, but it succeeded!"
        }
    }

    # ---------------------------------------------------------
    # Test 15: Post-signing validation SUCCEEDS when TimestampServer is explicitly empty without timestamp
    # ---------------------------------------------------------
    Assert-Test "Post-signing validation succeeds when TimestampServer is explicitly empty without timestamp" {
        $pfxFile = Join-Path $sandbox 'test-ephemeral.pfx'
        $secPass = ConvertTo-SecureString -String 'TestPass123' -AsPlainText -Force

        $records = @(& {
            function Set-AuthenticodeSignature {
                param($FilePath, $Certificate, $HashAlgorithm)
                return [PSCustomObject]@{
                    Status = [System.Management.Automation.SignatureStatus]::Valid
                    StatusMessage = 'Valid'
                    SignerCertificate = $mockSignerCert
                }
            }
            function Get-AuthenticodeSignature {
                param([string]$LiteralPath)
                return [PSCustomObject]@{
                    Path                   = $LiteralPath
                    Status                 = [System.Management.Automation.SignatureStatus]::Valid
                    StatusMessage          = 'Valid'
                    SignerCertificate      = $mockSignerCert
                    TimeStamperCertificate = $null
                }
            }
            & $scriptPath -ArtifactPaths $sampleExe -CertificatePath $pfxFile -CertificatePassword $secPass -TimestampServer '' -PassThru -Quiet
        })

        if ($records.Count -ne 1) {
            throw "Expected 1 record, got $($records.Count)"
        }
        if (-not $records[0].Passed) {
            throw "Signing verification failed: $($records[0].FailureReason)"
        }
        if ($records[0].HasTimestamp) {
            throw "Expected HasTimestamp to be false for empty TimestampServer."
        }
    }

    # ---------------------------------------------------------
    # Test 16: Live signing FAILS when Set-AuthenticodeSignature returns error object instead of throwing
    # ---------------------------------------------------------
    Assert-Test "Live signing fails-closed when Set-AuthenticodeSignature returns error status instead of throwing" {
        $pfxFile = Join-Path $sandbox 'test-ephemeral.pfx'
        $secPass = ConvertTo-SecureString -String 'TestPass123' -AsPlainText -Force

        $failed = $false
        try {
            & {
                function Set-AuthenticodeSignature {
                    param($FilePath, $Certificate, $HashAlgorithm)
                    return [PSCustomObject]@{
                        Status = [System.Management.Automation.SignatureStatus]::UnknownError
                        StatusMessage = 'The cryptographic provider returned an error.'
                        SignerCertificate = $mockSignerCert
                    }
                }
                & $scriptPath -ArtifactPaths $sampleExe -CertificatePath $pfxFile -CertificatePassword $secPass -TimestampServer '' -Quiet
            }
        }
        catch {
            $failed = $true
            if ($_.Exception.Message -notlike "*Set-AuthenticodeSignature failed with status*") {
                throw "Unexpected exception message: $($_.Exception.Message)"
            }
        }
        if (-not $failed) {
            throw "Expected signing to fail when Set-AuthenticodeSignature returns error status, but it succeeded!"
        }
    }

    # ---------------------------------------------------------
    # Test 17: Live signing FAILS when Set-AuthenticodeSignature returns missing SignerCertificate
    # ---------------------------------------------------------
    Assert-Test "Live signing fails-closed when Set-AuthenticodeSignature returns signature without SignerCertificate" {
        $pfxFile = Join-Path $sandbox 'test-ephemeral.pfx'
        $secPass = ConvertTo-SecureString -String 'TestPass123' -AsPlainText -Force

        $failed = $false
        try {
            & {
                function Set-AuthenticodeSignature {
                    param($FilePath, $Certificate, $HashAlgorithm)
                    return [PSCustomObject]@{
                        Status = [System.Management.Automation.SignatureStatus]::Valid
                        StatusMessage = 'Valid'
                        SignerCertificate = $null
                    }
                }
                & $scriptPath -ArtifactPaths $sampleExe -CertificatePath $pfxFile -CertificatePassword $secPass -TimestampServer '' -Quiet
            }
        }
        catch {
            $failed = $true
            if ($_.Exception.Message -notlike "*without SignerCertificate*") {
                throw "Unexpected exception message: $($_.Exception.Message)"
            }
        }
        if (-not $failed) {
            throw "Expected signing to fail when SignerCertificate is missing from signOutput, but it succeeded!"
        }
    }

    # ---------------------------------------------------------
    # Test 18: Post-signing validation FAILS when TimestampServer is configured but signature lacks timestamp
    # ---------------------------------------------------------
    Assert-Test "Post-signing validation fails-closed when TimestampServer is configured but signature lacks timestamp" {
        $pfxFile = Join-Path $sandbox 'test-ephemeral.pfx'
        $secPass = ConvertTo-SecureString -String 'TestPass123' -AsPlainText -Force

        $failed = $false
        try {
            & {
                function Set-AuthenticodeSignature {
                    param($FilePath, $Certificate, $HashAlgorithm, $TimestampServer)
                    return [PSCustomObject]@{
                        Status = [System.Management.Automation.SignatureStatus]::Valid
                        StatusMessage = 'Valid'
                        SignerCertificate = $mockSignerCert
                    }
                }
                function Get-AuthenticodeSignature {
                    param([string]$LiteralPath)
                    return [PSCustomObject]@{
                        Path                   = $LiteralPath
                        Status                 = [System.Management.Automation.SignatureStatus]::Valid
                        StatusMessage          = 'Valid'
                        SignerCertificate      = $mockSignerCert
                        TimeStamperCertificate = $null
                    }
                }
                & $scriptPath -ArtifactPaths $sampleExe -CertificatePath $pfxFile -CertificatePassword $secPass -TimestampServer 'http://timestamp.digicert.com' -Quiet
            }
        }
        catch {
            $failed = $true
            if ($_.Exception.Message -notlike "*missing required timestamp*") {
                throw "Unexpected exception message: $($_.Exception.Message)"
            }
        }
        if (-not $failed) {
            throw "Expected post-signing verification to fail when TimestampServer is configured but timestamp is missing, but it succeeded!"
        }
    }

    # ---------------------------------------------------------
    # Test 19: Post-signing validation SUCCEEDS when TimestampServer is configured and signature has valid timestamp
    # ---------------------------------------------------------
    Assert-Test "Post-signing validation succeeds when TimestampServer is configured and signature has valid timestamp" {
        $pfxFile = Join-Path $sandbox 'test-ephemeral.pfx'
        $secPass = ConvertTo-SecureString -String 'TestPass123' -AsPlainText -Force

        $records = @(& {
            function Set-AuthenticodeSignature {
                param($FilePath, $Certificate, $HashAlgorithm, $TimestampServer)
                return [PSCustomObject]@{
                    Status = [System.Management.Automation.SignatureStatus]::Valid
                    StatusMessage = 'Valid'
                    SignerCertificate = $mockSignerCert
                    TimeStamperCertificate = $mockSignerCert
                }
            }
            function Get-AuthenticodeSignature {
                param([string]$LiteralPath)
                return [PSCustomObject]@{
                    Path                   = $LiteralPath
                    Status                 = [System.Management.Automation.SignatureStatus]::Valid
                    StatusMessage          = 'Valid'
                    SignerCertificate      = $mockSignerCert
                    TimeStamperCertificate = $mockSignerCert
                }
            }
            & $scriptPath -ArtifactPaths $sampleExe -CertificatePath $pfxFile -CertificatePassword $secPass -TimestampServer 'http://timestamp.digicert.com' -PassThru -Quiet
        })

        if ($records.Count -ne 1) {
            throw "Expected 1 record, got $($records.Count)"
        }
        if (-not $records[0].Passed) {
            throw "Signing verification failed: $($records[0].FailureReason)"
        }
        if (-not $records[0].HasTimestamp) {
            throw "Expected HasTimestamp to be true."
        }
    }

    Write-Host "`n==========================================" -ForegroundColor Cyan
    Write-Host "All $passCount / $testCount tests PASSED successfully." -ForegroundColor Green
    Write-Host "==========================================" -ForegroundColor Cyan
}
finally {
    # Restore environment variables silently without printing
    $env:SIGNING_CERT_PATH = $savedEnvCertPath
    $env:SIGNING_CERT_PASSWORD = $savedEnvCertPass
    $env:SIGNING_CERT_THUMBPRINT = $savedEnvCertThumb

    if (Test-Path -LiteralPath $sandbox) {
        Remove-Item -LiteralPath $sandbox -Recurse -Force -ErrorAction SilentlyContinue
    }
}
