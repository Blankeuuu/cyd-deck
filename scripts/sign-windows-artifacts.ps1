#Requires -Version 5.1
<#
.SYNOPSIS
    Secure Windows Authenticode signing and verification script for CYD-DECK artifacts.

.DESCRIPTION
    Provides fail-closed signing and signature verification for PE binaries (.exe, .dll)
    and PowerShell scripts (.ps1). Designed for CI/CD pipelines (e.g. SignPath Foundation integration),
    isolated test environments, and local developer validation.

    Security rules enforced:
    - Fail-closed: refuses to sign if certificate material is missing.
    - Strict validation: verification and post-sign validation REQUIRE SignatureStatus.Valid
      AND a non-null SignerCertificate. Any status such as UnknownError, NotTrusted, NotSigned,
      or HashMismatch is strictly treated as FAIL.
    - Fail-closed timestamping: if TimestampServer is configured and fails, the operation
      aborts immediately (no silent fallback to unsigned timestamp).
    - Ephemeral key loading: PFX certificates are loaded with EphemeralKeySet and disposed
      promptly, preventing persistent importation into local stores.
    - Zero secret leakage: passwords are never written to logs or stdout.
    - Dry-run capability: validates inputs and displays planned operations without modifying files.

.PARAMETER ArtifactPaths
    One or more file paths to the PE binaries (.exe, .dll) or scripts (.ps1) to sign or verify.

.PARAMETER VerifyOnly
    When specified, skips signing and only verifies Authenticode signature integrity.

.PARAMETER DryRun
    When specified, validates artifact paths and displays the signing plan without modifying files.

.PARAMETER CertificatePath
    Path to a .pfx or .p12 certificate file. Can also be supplied via $env:SIGNING_CERT_PATH.

.PARAMETER CertificatePassword
    SecureString password for the certificate. Can also be supplied via $env:SIGNING_CERT_PASSWORD.

.PARAMETER CertificateThumbprint
    Thumbprint of a certificate in the Windows Certificate Store. Can also be supplied via $env:SIGNING_CERT_THUMBPRINT.

.PARAMETER CertStoreLocation
    Certificate store location when using thumbprint (default: Cert:\CurrentUser\My).

.PARAMETER TimestampServer
    Authenticode timestamp authority URL (default: http://timestamp.digicert.com).
    Pass an empty string to disable timestamping during offline/local tests.

.PARAMETER HashAlgorithm
    Digest algorithm for Authenticode signature (default: SHA256).

.PARAMETER ExpectedSubject
    Optional wildcard or substring pattern that the Signer Certificate Subject must match during verification.

.PARAMETER PassThru
    Outputs custom PSCustomObject result records for downstream scripting and test assertions.

.PARAMETER Quiet
    Suppresses informational messages.
#>
[CmdletBinding(DefaultParameterSetName = 'Sign')]
param(
    [Parameter(Mandatory = $true, ValueFromPipeline = $true, Position = 0)]
    [ValidateNotNullOrEmpty()]
    [string[]]$ArtifactPaths,

    [Parameter(ParameterSetName = 'Verify')]
    [switch]$VerifyOnly,

    [Parameter(ParameterSetName = 'Sign')]
    [switch]$DryRun,

    [Parameter(ParameterSetName = 'Sign')]
    [Alias('CertPath')]
    [string]$CertificatePath,

    [Parameter(ParameterSetName = 'Sign')]
    [System.Security.SecureString]$CertificatePassword,

    [Parameter(ParameterSetName = 'Sign')]
    [Alias('Thumbprint')]
    [string]$CertificateThumbprint,

    [Parameter(ParameterSetName = 'Sign')]
    [string]$CertStoreLocation = 'Cert:\CurrentUser\My',

    [string]$TimestampServer = 'http://timestamp.digicert.com',

    [ValidateSet('SHA256', 'SHA384', 'SHA512')]
    [string]$HashAlgorithm = 'SHA256',

    [string]$ExpectedSubject,

    [switch]$PassThru,

    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Info {
    param([string]$Message)
    if (-not $Quiet) {
        Write-Host "[INFO] $Message"
    }
}

function Write-WarningLog {
    param([string]$Message)
    if (-not $Quiet) {
        Write-Warning "[WARN] $Message"
    }
}

function Assert-ValidArtifactPath {
    param([string]$PathValue)

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        throw 'Artifact path must not be null or empty.'
    }

    $invalidChars = [System.IO.Path]::GetInvalidPathChars()
    if ($PathValue.IndexOfAny($invalidChars) -ge 0) {
        throw "Artifact path contains invalid characters: $PathValue"
    }

    if (-not (Test-Path -LiteralPath $PathValue -PathType Leaf)) {
        throw "Artifact file not found: $PathValue"
    }

    $ext = [System.IO.Path]::GetExtension($PathValue).ToLowerInvariant()
    $validExtensions = @('.exe', '.dll', '.ps1')
    if ($ext -notin $validExtensions) {
        throw "Unsupported artifact extension '$ext' for Authenticode signing/verification. Supported extensions: $($validExtensions -join ', ')."
    }

    return (Resolve-Path -LiteralPath $PathValue).Path
}

function Get-CertificateMaterial {
    # Resolve certificate path from parameter or environment
    $certFile = $CertificatePath
    if ([string]::IsNullOrWhiteSpace($certFile) -and [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        if (-not [string]::IsNullOrWhiteSpace($env:SIGNING_CERT_PATH)) {
            $certFile = $env:SIGNING_CERT_PATH
        }
    }

    # Resolve thumbprint from parameter or environment
    $thumb = $CertificateThumbprint
    if ([string]::IsNullOrWhiteSpace($thumb) -and [string]::IsNullOrWhiteSpace($certFile)) {
        if (-not [string]::IsNullOrWhiteSpace($env:SIGNING_CERT_THUMBPRINT)) {
            $thumb = $env:SIGNING_CERT_THUMBPRINT
        }
    }

    # Fail-closed check
    if ([string]::IsNullOrWhiteSpace($certFile) -and [string]::IsNullOrWhiteSpace($thumb)) {
        throw @"
Signing credentials not provided (fail-closed).
Cannot sign artifacts without valid certificate material.
To resolve:
  1. Specify -CertificatePath <path.pfx> (or env:SIGNING_CERT_PATH), OR
  2. Specify -CertificateThumbprint <thumbprint> (or env:SIGNING_CERT_THUMBPRINT), OR
  3. Use -DryRun to validate artifact paths without signing, OR
  4. Use -VerifyOnly to inspect signatures on pre-signed artifacts (e.g. from SignPath Foundation CI).
"@
    }

    if (-not [string]::IsNullOrWhiteSpace($certFile)) {
        if (-not (Test-Path -LiteralPath $certFile -PathType Leaf)) {
            throw "Certificate file not found: $certFile"
        }

        # Resolve password securely without logging
        $secPass = $CertificatePassword
        if ($null -eq $secPass -and -not [string]::IsNullOrWhiteSpace($env:SIGNING_CERT_PASSWORD)) {
            $bstr = [System.Runtime.InteropServices.Marshal]::StringToBSTR($env:SIGNING_CERT_PASSWORD)
            try {
                $secPass = [System.Security.SecureString]::new()
                for ($i = 0; $i -lt $env:SIGNING_CERT_PASSWORD.Length; $i++) {
                    $secPass.AppendChar([System.Runtime.InteropServices.Marshal]::ReadInt16($bstr, $i * 2) -as [char])
                }
                $secPass.MakeReadOnly()
            }
            finally {
                [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
            }
        }

        # EphemeralKeySet ensures private key is not persisted into disk key containers
        $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
        try {
            if ($null -ne $secPass) {
                $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certFile, $secPass, $flags)
            }
            else {
                $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certFile, '', $flags)
            }
        }
        catch {
            throw "Failed to load certificate from '$certFile'. Verify that the password is correct. Inner error: $($_.Exception.Message)"
        }

        if (-not $cert.HasPrivateKey) {
            $cert.Dispose()
            throw "Certificate at '$certFile' does not contain a private key required for signing."
        }

        return $cert
    }

    if (-not [string]::IsNullOrWhiteSpace($thumb)) {
        $cleanThumb = $thumb.Replace(' ', '').Replace(':', '').ToUpperInvariant()
        $storePath = Join-Path $CertStoreLocation $cleanThumb
        $cert = Get-Item -LiteralPath $storePath -ErrorAction SilentlyContinue
        if ($null -eq $cert) {
            throw "Certificate with thumbprint '$cleanThumb' not found in store '$CertStoreLocation'."
        }
        if (-not $cert.HasPrivateKey) {
            throw "Certificate '$cleanThumb' in store '$CertStoreLocation' does not have an accessible private key."
        }
        return $cert
    }

    throw 'Unexpected error resolving certificate material.'
}

function Test-SingleArtifactSignature {
    param(
        [string]$PathValue,
        [string]$ExpectedSignerSubject,
        [switch]$RequireTimestamp
    )

    $sig = Get-AuthenticodeSignature -LiteralPath $PathValue
    $statusStr = $sig.Status.ToString()
    $statusMsg = $sig.StatusMessage
    $signer = if ($null -ne $sig.SignerCertificate) { $sig.SignerCertificate.Subject } else { $null }
    $thumb = if ($null -ne $sig.SignerCertificate) { $sig.SignerCertificate.Thumbprint } else { $null }
    $hasTimestamp = ($null -ne $sig.TimeStamperCertificate)

    $passed = $false
    $failReason = $null

    # Strict validation rule: MUST be Valid AND have non-null SignerCertificate.
    # UnknownError, NotTrusted, NotSigned, HashMismatch, NotSupportedFileFormat etc. strictly FAIL.
    if ($sig.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        $failReason = "Artifact signature validation failed ($($statusStr): $($statusMsg)): $PathValue"
    }
    elseif ($null -eq $sig.SignerCertificate) {
        $failReason = "Artifact signature is missing a valid SignerCertificate: $PathValue"
    }
    elseif ($RequireTimestamp -and (-not $hasTimestamp)) {
        $failReason = "Artifact signature is missing required timestamp certificate (TimeStamperCertificate): $PathValue"
    }
    else {
        $passed = $true
    }

    # Verify expected subject if requested
    if ($passed -and -not [string]::IsNullOrWhiteSpace($ExpectedSignerSubject)) {
        if ([string]::IsNullOrWhiteSpace($signer) -or ($signer -notlike "*$ExpectedSignerSubject*")) {
            $passed = $false
            $failReason = "Signer subject '$signer' does not match expected pattern '*$ExpectedSignerSubject*': $PathValue"
        }
    }

    return [PSCustomObject]@{
        Path           = $PathValue
        Passed         = $passed
        Status         = $statusStr
        StatusMessage  = $statusMsg
        SignerSubject  = $signer
        Thumbprint     = $thumb
        HasTimestamp   = $hasTimestamp
        FailureReason  = $failReason
    }
}

# --- Main Execution ---

$resolvedArtifacts = @()
foreach ($item in $ArtifactPaths) {
    $resolvedArtifacts += Assert-ValidArtifactPath -PathValue $item
}

Write-Info "Processing $($resolvedArtifacts.Count) artifact(s)."

# Scenario 1: VerifyOnly
if ($VerifyOnly) {
    Write-Info 'Mode: VerifyOnly (Strict Authenticode signature verification).'
    $results = @()
    $hasFailure = $false

    foreach ($artifact in $resolvedArtifacts) {
        $verifyParams = @{
            PathValue             = $artifact
            ExpectedSignerSubject = $ExpectedSubject
        }
        $res = Test-SingleArtifactSignature @verifyParams
        $results += $res

        if ($res.Passed) {
            Write-Info "PASS signature verified: $artifact [Status: $($res.Status), Signer: $($res.SignerSubject), Timestamp: $($res.HasTimestamp)]"
        }
        else {
            Write-WarningLog "FAIL signature verification: $($res.FailureReason)"
            $hasFailure = $true
        }
    }

    if ($PassThru) {
        $results
    }

    if ($hasFailure) {
        throw 'One or more artifacts failed Authenticode signature verification.'
    }

    Write-Info 'All artifacts passed signature verification successfully.'
    return
}

# Scenario 2: DryRun
if ($DryRun) {
    Write-Info 'Mode: DryRun (simulation). No files will be modified.'
    $results = @()

    $simulatedCertSource = 'Not specified (would fail-closed in live run unless credentials supplied)'
    if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
        $simulatedCertSource = "PFX file: $CertificatePath"
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:SIGNING_CERT_PATH)) {
        $simulatedCertSource = "PFX file from env: $env:SIGNING_CERT_PATH"
    }
    elseif (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        $simulatedCertSource = "Cert store thumbprint: $CertificateThumbprint"
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:SIGNING_CERT_THUMBPRINT)) {
        $simulatedCertSource = "Cert store thumbprint from env: $env:SIGNING_CERT_THUMBPRINT"
    }

    foreach ($artifact in $resolvedArtifacts) {
        $currentSig = Get-AuthenticodeSignature -LiteralPath $artifact
        $itemResult = [PSCustomObject]@{
            Path                 = $artifact
            Action               = 'DryRun-Sign'
            CurrentStatus        = $currentSig.Status.ToString()
            TargetHashAlgorithm  = $HashAlgorithm
            TargetTimestampServer= if ([string]::IsNullOrWhiteSpace($TimestampServer)) { 'None' } else { $TimestampServer }
            CertificateSource    = $simulatedCertSource
            WouldExecute         = 'Set-AuthenticodeSignature'
        }
        $results += $itemResult
        Write-Info "DRY-RUN plan for '$artifact': Sign using $HashAlgorithm with cert [$simulatedCertSource] (Current Authenticode status: $($currentSig.Status))."
    }

    if ($PassThru) {
        $results
    }

    Write-Info 'Dry-run inspection completed successfully.'
    return
}

# Scenario 3: Live Sign and Verify
Write-Info 'Mode: Sign and Verify.'
$cert = Get-CertificateMaterial
try {
    Write-Info "Loaded signing certificate: Subject='$($cert.Subject)', Thumbprint='$($cert.Thumbprint)', HasPrivateKey=$($cert.HasPrivateKey)"

    $results = @()
    foreach ($artifact in $resolvedArtifacts) {
        Write-Info "Signing artifact: $artifact"

        $signParams = @{
            FilePath      = $artifact
            Certificate   = $cert
            HashAlgorithm = $HashAlgorithm
        }
        if (-not [string]::IsNullOrWhiteSpace($TimestampServer)) {
            $signParams['TimestampServer'] = $TimestampServer
        }

        # Live signing: failure of TSA or signing must abort immediately (no silent fallback)
        try {
            $signOutput = Set-AuthenticodeSignature @signParams
        }
        catch {
            throw "Set-AuthenticodeSignature failed for '$artifact': $($_.Exception.Message)"
        }

        if ($null -eq $signOutput) {
            throw "Signing operation returned null for '$artifact'."
        }

        if ($signOutput.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
            $statusMsg = if ($null -ne $signOutput.StatusMessage) { $signOutput.StatusMessage } else { 'Unknown error' }
            throw "Set-AuthenticodeSignature failed with status '$($signOutput.Status)' ($statusMsg) for '$artifact'."
        }

        if ($null -eq $signOutput.SignerCertificate) {
            throw "Set-AuthenticodeSignature returned signature without SignerCertificate for '$artifact'."
        }

        # Immediately verify signed artifact with strict rules
        $verifyParams = @{
            PathValue             = $artifact
            ExpectedSignerSubject = $ExpectedSubject
            RequireTimestamp      = (-not [string]::IsNullOrWhiteSpace($TimestampServer))
        }
        $verifyResult = Test-SingleArtifactSignature @verifyParams

        $results += $verifyResult

        if ($verifyResult.Passed) {
            Write-Info "SUCCESS: Signed and verified '$artifact' [Status: $($verifyResult.Status), Signer: $($verifyResult.SignerSubject), Timestamp: $($verifyResult.HasTimestamp)]"
        }
        else {
            throw "Post-signing verification failed for '$artifact': $($verifyResult.FailureReason)"
        }
    }

    if ($PassThru) {
        $results
    }

    Write-Info "Successfully signed and verified all $($resolvedArtifacts.Count) artifact(s)."
}
finally {
    if ($null -ne $cert) {
        $cert.Dispose()
    }
}
