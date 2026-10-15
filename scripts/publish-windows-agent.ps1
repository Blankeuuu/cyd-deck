#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory,

    [string]$Configuration = "Release",

    [ValidateSet("win-x64")]
    [string]$RuntimeIdentifier = "win-x64",

    [string]$DotNetPath = "dotnet",

    [switch]$SkipQualityGate
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-RepositoryRoot {
    $scriptDirectory = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $scriptDirectory "..")).Path
}

function Assert-ValidPathInput {
    param([string]$PathValue, [string]$Name)

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        throw "$Name must not be empty."
    }

    $invalid = [System.IO.Path]::GetInvalidPathChars()
    if ($PathValue.IndexOfAny($invalid) -ge 0) {
        throw "$Name contains invalid path characters."
    }
}

Assert-ValidPathInput -PathValue $OutputDirectory -Name "OutputDirectory"
Assert-ValidPathInput -PathValue $Configuration -Name "Configuration"
Assert-ValidPathInput -PathValue $RuntimeIdentifier -Name "RuntimeIdentifier"

$repoRoot = Resolve-RepositoryRoot
$projectPath = Join-Path $repoRoot "agent/src/WindowsAgent/StreamDeck.WindowsAgent/StreamDeck.WindowsAgent.csproj"
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Windows Agent project not found: $projectPath"
}

$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$pathToCheck = $outputRoot
while (-not (Test-Path -LiteralPath $pathToCheck)) {
    $parent = Split-Path -Parent $pathToCheck
    if ([string]::IsNullOrEmpty($parent) -or $parent -eq $pathToCheck) {
        throw "Cannot resolve OutputDirectory parent: $outputRoot"
    }
    $pathToCheck = $parent
}
for ($ancestor = $pathToCheck; -not [string]::IsNullOrEmpty($ancestor); $ancestor = Split-Path -Parent $ancestor) {
    if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        throw "OutputDirectory must not traverse a junction or symbolic link: $OutputDirectory"
    }
    $parent = Split-Path -Parent $ancestor
    if ($parent -eq $ancestor) { break }
}
$repositoryRoot = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$outputRootNormalized = $outputRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$pathComparison = [System.StringComparison]::OrdinalIgnoreCase
$separator = [System.IO.Path]::DirectorySeparatorChar
$artifactsRoot = Join-Path $repositoryRoot "artifacts"
$insideRepository = $outputRootNormalized.StartsWith($repositoryRoot + $separator, $pathComparison)
$insideArtifacts = $outputRootNormalized.StartsWith($artifactsRoot + $separator, $pathComparison)
$containsRepository = $repositoryRoot.StartsWith($outputRootNormalized + $separator, $pathComparison)
if ([string]::Equals($outputRootNormalized, $repositoryRoot, $pathComparison) -or $containsRepository -or
    ($insideRepository -and -not $insideArtifacts)) {
    throw "OutputDirectory must be a dedicated directory under artifacts/ or outside the repository, never the repository root, its parent, or a source directory."
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$publishRoot = Join-Path $outputRoot "publish"
$publishDirectory = Join-Path $publishRoot "StreamDeck.WindowsAgent"
$zipPath = Join-Path $outputRoot "StreamDeck.WindowsAgent-$RuntimeIdentifier.zip"
$instructionsPath = Join-Path $publishDirectory "DEPLOYMENT.txt"
$thirdPartyNoticesPath = Join-Path $publishDirectory "THIRD-PARTY-NOTICES.txt"
$thirdPartyNoticesDirectory = Join-Path $publishDirectory "THIRD-PARTY-NOTICES"

function Get-NuGetPackageRoot {
    param([string]$PackageId, [string]$Version)

    $profileRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    if ([string]::IsNullOrWhiteSpace($profileRoot)) {
        throw "Cannot resolve the current user's profile directory for NuGet package notices."
    }

    $packageRoot = Join-Path $profileRoot ".nuget/packages/$($PackageId.ToLowerInvariant())/$Version"
    if (-not (Test-Path -LiteralPath $packageRoot -PathType Container)) {
        throw "Required NuGet package folder is missing: $packageRoot"
    }

    return $packageRoot
}

function Copy-RequiredNoticeFile {
    param(
        [string]$PackageId,
        [string]$Version,
        [string]$RelativePath,
        [string]$DestinationName
    )

    $packageRoot = Get-NuGetPackageRoot -PackageId $PackageId -Version $Version
    $sourcePath = Join-Path $packageRoot $RelativePath
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Required notice file is missing for $PackageId ${Version}: $sourcePath"
    }

    Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $thirdPartyNoticesDirectory $DestinationName) -Force
}

function Get-ReleaseNoticeInventory {
    return @(
        [PSCustomObject]@{ Id = "SkiaSharp"; Version = "4.153.1" }
        [PSCustomObject]@{ Id = "SkiaSharp.NativeAssets.Win32"; Version = "4.153.1" }
        [PSCustomObject]@{ Id = "System.IO.Ports"; Version = "10.0.0" }
        [PSCustomObject]@{ Id = "Microsoft.NETCore.App.Runtime.win-x64"; Version = "10.0.13" }
        [PSCustomObject]@{ Id = "Microsoft.WindowsDesktop.App.Runtime.win-x64"; Version = "10.0.13" }
        [PSCustomObject]@{ Id = "Microsoft.AspNetCore.App.Runtime.win-x64"; Version = "10.0.13" }
        [PSCustomObject]@{ Id = "Microsoft.Windows.SDK.NET.Ref"; Version = "10.0.17763.57" }
    )
}

function Assert-ReleaseNoticeInventoryMatchesDeps {
    param([string]$DepsManifestPath)

    if (-not (Test-Path -LiteralPath $DepsManifestPath -PathType Leaf)) {
        throw "Published deps manifest is missing: $DepsManifestPath"
    }

    $deps = Get-Content -LiteralPath $DepsManifestPath -Raw | ConvertFrom-Json
    $expected = Get-ReleaseNoticeInventory
    $expectedKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($item in $expected) {
        [void]$expectedKeys.Add("$($item.Id)/$($item.Version)")
    }

    $actualKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($target in $deps.targets.PSObject.Properties) {
        foreach ($library in $target.Value.PSObject.Properties) {
            $libraryName = $library.Name
            if ($libraryName -notmatch '/') {
                continue
            }

            if ($libraryName.StartsWith("runtimepack.", [System.StringComparison]::OrdinalIgnoreCase)) {
                $libraryName = $libraryName.Substring("runtimepack.".Length)
            }

            if ($expectedKeys.Contains($libraryName)) {
                [void]$actualKeys.Add($libraryName)
                continue
            }

            $libraryMetadataProperty = $deps.libraries.PSObject.Properties[$libraryName]
            $libraryMetadata = $null
            if ($null -ne $libraryMetadataProperty) {
                $libraryMetadata = $libraryMetadataProperty.Value
            }
            if ($null -ne $libraryMetadata -and $libraryMetadata.type -eq "project") {
                continue
            }

            if ($libraryName.StartsWith("StreamDeck.", [System.StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            throw "Release notice inventory does not cover published dependency '$libraryName'. Update publish-windows-agent.ps1 notices and DEPLOYMENT.txt before creating the archive."
        }
    }

    foreach ($item in $expected) {
        $key = "$($item.Id)/$($item.Version)"
        if (-not $actualKeys.Contains($key)) {
            throw "Release notice inventory expects '$key', but the published deps manifest does not contain it. Update publish-windows-agent.ps1 notices and DEPLOYMENT.txt before creating the archive."
        }
    }
}

function Remove-PublishSymbols {
    param([string]$Directory)

    Get-ChildItem -LiteralPath $Directory -Filter "*.pdb" -File -Recurse | Remove-Item -Force
}

function Assert-NoPublishSymbols {
    param([string]$Directory)

    $symbols = @(Get-ChildItem -LiteralPath $Directory -Filter "*.pdb" -File -Recurse)
    if ($symbols.Count -ne 0) {
        $symbolList = ($symbols | ForEach-Object { $_.FullName }) -join [Environment]::NewLine
        throw "Published portable package must not include PDB symbol files. Remove symbols from the dedicated publish directory before archiving:$([Environment]::NewLine)$symbolList"
    }
}

function Assert-ZipHasNoSymbols {
    param([string]$ArchivePath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $symbolEntries = @($archive.Entries | Where-Object { $_.FullName.EndsWith(".pdb", [System.StringComparison]::OrdinalIgnoreCase) })
        if ($symbolEntries.Count -ne 0) {
            $symbolList = ($symbolEntries | ForEach-Object { $_.FullName }) -join [Environment]::NewLine
            throw "Portable ZIP contains PDB symbol files:$([Environment]::NewLine)$symbolList"
        }
    }
    finally {
        $archive.Dispose()
    }
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $thirdPartyNoticesDirectory -Force | Out-Null

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

if (-not $SkipQualityGate) {
    & $DotNetPath build $projectPath --configuration $Configuration --runtime $RuntimeIdentifier --self-contained true
    if ($LASTEXITCODE -ne 0) {
        throw "Prepublish quality gate failed: dotnet build exited with code $LASTEXITCODE."
    }
}

& $DotNetPath publish $projectPath `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish exited with code $LASTEXITCODE."
}

Remove-PublishSymbols -Directory $publishDirectory
Assert-NoPublishSymbols -Directory $publishDirectory

$exePath = Join-Path $publishDirectory "StreamDeck.WindowsAgent.exe"
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Publish completed but expected executable is missing: $exePath"
}

$depsManifestPath = Join-Path $publishDirectory "StreamDeck.WindowsAgent.deps.json"
Assert-ReleaseNoticeInventoryMatchesDeps -DepsManifestPath $depsManifestPath

@"
Third-party notices for StreamDeck Windows Agent portable package

Status:
- This file accompanies exact-version package and runtime notices copied from the artifacts listed in the published deps manifest.
- It is not a substitute for a separate legal/compliance review before release.

SkiaSharp
- Package: SkiaSharp
- Version: 4.153.1
- Copyright metadata: © Microsoft Corporation. All rights reserved.
- Source: official NuGet package artifact SkiaSharp 4.153.1 (.nupkg)
- License metadata in that exact artifact: MIT
- License acceptance metadata in that exact artifact: true
- LICENSE.txt from that exact artifact is copied to THIRD-PARTY-NOTICES/SkiaSharp-4.153.1-LICENSE.txt.

SkiaSharp.NativeAssets.Win32
- Package: SkiaSharp.NativeAssets.Win32
- Version: 4.153.1
- Copyright metadata: © Microsoft Corporation. All rights reserved.
- Source: official NuGet package artifact SkiaSharp.NativeAssets.Win32 4.153.1 (.nupkg)
- License metadata in that exact artifact: MIT
- License acceptance metadata in that exact artifact: true
- LICENSE.txt and THIRD-PARTY-NOTICES.txt from that exact artifact are copied to THIRD-PARTY-NOTICES/.

System.IO.Ports
- Package: System.IO.Ports
- Version: 10.0.0
- Copyright: Copyright (c) Microsoft Corporation. All rights reserved.
- Source: official NuGet package artifact System.IO.Ports 10.0.0 (.nupkg)
- License metadata in that exact artifact: <license type="expression">MIT</license>
- Repository metadata in that exact artifact: https://github.com/dotnet/dotnet commit b0f34d51fccc69fd334253924abd8d6853fad7aa
- Third-party notice file from that exact artifact is copied to THIRD-PARTY-NOTICES/System.IO.Ports-10.0.0-THIRD-PARTY-NOTICES.TXT.

"@ | Set-Content -LiteralPath $thirdPartyNoticesPath -Encoding UTF8

Copy-RequiredNoticeFile -PackageId "SkiaSharp" -Version "4.153.1" -RelativePath "LICENSE.txt" -DestinationName "SkiaSharp-4.153.1-LICENSE.txt"
Copy-RequiredNoticeFile -PackageId "SkiaSharp.NativeAssets.Win32" -Version "4.153.1" -RelativePath "LICENSE.txt" -DestinationName "SkiaSharp.NativeAssets.Win32-4.153.1-LICENSE.txt"
Copy-RequiredNoticeFile -PackageId "SkiaSharp.NativeAssets.Win32" -Version "4.153.1" -RelativePath "THIRD-PARTY-NOTICES.txt" -DestinationName "SkiaSharp.NativeAssets.Win32-4.153.1-THIRD-PARTY-NOTICES.txt"
Copy-RequiredNoticeFile -PackageId "System.IO.Ports" -Version "10.0.0" -RelativePath "THIRD-PARTY-NOTICES.TXT" -DestinationName "System.IO.Ports-10.0.0-THIRD-PARTY-NOTICES.TXT"
Copy-RequiredNoticeFile -PackageId "Microsoft.NETCore.App.Runtime.win-x64" -Version "10.0.13" -RelativePath "LICENSE.TXT" -DestinationName "Microsoft.NETCore.App.Runtime.win-x64-10.0.13-LICENSE.TXT"
Copy-RequiredNoticeFile -PackageId "Microsoft.NETCore.App.Runtime.win-x64" -Version "10.0.13" -RelativePath "THIRD-PARTY-NOTICES.TXT" -DestinationName "Microsoft.NETCore.App.Runtime.win-x64-10.0.13-THIRD-PARTY-NOTICES.TXT"
Copy-RequiredNoticeFile -PackageId "Microsoft.AspNetCore.App.Runtime.win-x64" -Version "10.0.13" -RelativePath "LICENSE.txt" -DestinationName "Microsoft.AspNetCore.App.Runtime.win-x64-10.0.13-LICENSE.txt"
Copy-RequiredNoticeFile -PackageId "Microsoft.AspNetCore.App.Runtime.win-x64" -Version "10.0.13" -RelativePath "THIRD-PARTY-NOTICES.TXT" -DestinationName "Microsoft.AspNetCore.App.Runtime.win-x64-10.0.13-THIRD-PARTY-NOTICES.TXT"
Copy-RequiredNoticeFile -PackageId "Microsoft.WindowsDesktop.App.Runtime.win-x64" -Version "10.0.13" -RelativePath "LICENSE" -DestinationName "Microsoft.WindowsDesktop.App.Runtime.win-x64-10.0.13-LICENSE"

@"
StreamDeck Windows Agent portable deployment

Install:
1. Extract this ZIP to a stable per-user writable directory, for example:
   %LOCALAPPDATA%\Programs\StreamDeckWindowsAgent
2. Run StreamDeck.WindowsAgent.exe from that directory.
3. On first use, keep the PC and device on the same private LAN, allow the agent executable/port in Windows Firewall when prompted or by a narrow manual allow rule, then approve pairing in the agent UI.

Network:
- The device LAN WebSocket listener defaults to port 8765 and path /ws.
- The local dashboard binds only to 127.0.0.1 on an ephemeral port; do not expose or forward the dashboard.
- This package does not create firewall rules, registry entries, services, scheduled tasks, or machine-wide state.

Startup:
- Optional per-user startup can be enabled from the tray/settings UI after launch.

Update and rollback:
1. Exit the agent from the tray menu.
2. Keep a copy of the previous extracted directory.
3. Extract the new ZIP to the same stable install directory, replacing application files only.
4. Restart StreamDeck.WindowsAgent.exe.
5. To roll back, exit the agent and restore the previous extracted directory.

Uninstall and data retention:
- To uninstall the portable app, exit the agent and delete the install directory.
- User configuration, logs, pairing records, and cache are stored outside this ZIP under the user's local application data. Delete %LOCALAPPDATA%\StreamDeckAgent only if you intentionally want to remove retained data and pairing state.

Package contents:
- Self-contained win-x64 publish output for StreamDeck.WindowsAgent.
- Release PDB symbol files are intentionally excluded from this end-user portable ZIP to avoid redistributing local diagnostic/source-path metadata; local Release build outputs may still keep PDBs for diagnostics.
- No secrets, pairing state, configuration export, logs, cache, or user data are included.
- THIRD-PARTY-NOTICES.txt with exact package license metadata and THIRD-PARTY-NOTICES/ files copied from the published NuGet artifacts and .NET runtime packs.
- THIRD-PARTY-NOTICES/ with exact-version notices copied from SkiaSharp 4.153.1, SkiaSharp.NativeAssets.Win32 4.153.1, System.IO.Ports 10.0.0, and the .NET runtime packs that ship in this self-contained package.

Notice inventory status:
- The published deps manifest for the validated portable package lists SkiaSharp/4.153.1, SkiaSharp.NativeAssets.Win32/4.153.1, System.IO.Ports/10.0.0, Microsoft.NETCore.App.Runtime.win-x64/10.0.13, Microsoft.WindowsDesktop.App.Runtime.win-x64/10.0.13, Microsoft.AspNetCore.App.Runtime.win-x64/10.0.13, and Microsoft.Windows.SDK.NET.Ref/10.0.17763.57.
- SkiaSharp and SkiaSharp.NativeAssets.Win32 4.153.1 are included because their exact pinned NuGet artifacts declare MIT and requireLicenseAcceptance=true; the Win32 package also supplies third-party notices.
- System.IO.Ports 10.0.0 is included because the exact pinned NuGet artifact declares MIT and ships THIRD-PARTY-NOTICES.TXT.
- Runtime-pack license and notice files are copied from the exact local NuGet package folders; packaging fails if a required file is unavailable.
- Microsoft.Windows.SDK.NET.Ref/10.0.17763.57 is documented from Microsoft's REDIST.TXT at https://go.microsoft.com/fwlink/?LinkId=524842 for unmodified package files used as part of a program to call WinRT APIs. This is not legal advice and makes no broader redistribution claim.
- This package covers the exact published deps/runtime-pack inventory named above. Re-run inventory before release if dependencies, target framework, runtime identifier, or publish contents change.
"@ | Set-Content -LiteralPath $instructionsPath -Encoding UTF8

Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $zipPath -Force
Assert-ZipHasNoSymbols -ArchivePath $zipPath

Write-Host "Published Windows Agent package: $zipPath"
Write-Host "Publish directory: $publishDirectory"
