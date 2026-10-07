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
        [PSCustomObject]@{ Id = "SixLabors.ImageSharp"; Version = "2.1.11" }
        [PSCustomObject]@{ Id = "Microsoft.NETCore.App.Runtime.win-x64"; Version = "10.0.12" }
        [PSCustomObject]@{ Id = "Microsoft.WindowsDesktop.App.Runtime.win-x64"; Version = "10.0.12" }
        [PSCustomObject]@{ Id = "Microsoft.AspNetCore.App.Runtime.win-x64"; Version = "10.0.12" }
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
- This file includes the known notice for SixLabors.ImageSharp 2.1.11 identified in package checks.
- It is not a complete transitive/runtime notice inventory unless a separate release inventory confirms that no additional notices are required.

SixLabors.ImageSharp
- Package: SixLabors.ImageSharp
- Version: 2.1.11
- Copyright: Copyright © Six Labors
- Source: official NuGet package artifact SixLabors.ImageSharp 2.1.11 (.nupkg)
- License metadata in that exact artifact: <license type="expression">Apache-2.0</license>
- License acceptance metadata in that exact artifact: <requireLicenseAcceptance>true</requireLicenseAcceptance>
- Note: Do not apply the current ImageSharp repository/main-branch or newer-version Six Labors Split License to this pinned 2.1.11 artifact without re-verifying the exact package being redistributed.

Apache License
Version 2.0, January 2004
http://www.apache.org/licenses/

TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

1. Definitions.

"License" shall mean the terms and conditions for use, reproduction,
and distribution as defined by Sections 1 through 9 of this document.

"Licensor" shall mean the copyright owner or entity authorized by
the copyright owner that is granting the License.

"Legal Entity" shall mean the union of the acting entity and all
other entities that control, are controlled by, or are under common
control with that entity. For the purposes of this definition,
"control" means (i) the power, direct or indirect, to cause the
direction or management of such entity, whether by contract or
otherwise, or (ii) ownership of fifty percent (50%) or more of the
outstanding shares, or (iii) beneficial ownership of such entity.

"You" (or "Your") shall mean an individual or Legal Entity
exercising permissions granted by this License.

"Source" form shall mean the preferred form for making modifications,
including but not limited to software source code, documentation
source, and configuration files.

"Object" form shall mean any form resulting from mechanical
transformation or translation of a Source form, including but
not limited to compiled object code, generated documentation,
and conversions to other media types.

"Work" shall mean the work of authorship, whether in Source or
Object form, made available under the License, as indicated by a
copyright notice that is included in or attached to the work
(an example is provided in the Appendix below).

"Derivative Works" shall mean any work, whether in Source or Object
form, that is based on (or derived from) the Work and for which the
editorial revisions, annotations, elaborations, or other modifications
represent, as a whole, an original work of authorship. For the purposes
of this License, Derivative Works shall not include works that remain
separable from, or merely link (or bind by name) to the interfaces of,
the Work and Derivative Works thereof.

"Contribution" shall mean any work of authorship, including
the original version of the Work and any modifications or additions
to that Work or Derivative Works thereof, that is intentionally
submitted to Licensor for inclusion in the Work by the copyright owner
or by an individual or Legal Entity authorized to submit on behalf of
the copyright owner. For the purposes of this definition, "submitted"
means any form of electronic, verbal, or written communication sent
to the Licensor or its representatives, including but not limited to
communication on electronic mailing lists, source code control systems,
and issue tracking systems that are managed by, or on behalf of, the
Licensor for the purpose of discussing and improving the Work, but
excluding communication that is conspicuously marked or otherwise
designated in writing by the copyright owner as "Not a Contribution."

"Contributor" shall mean Licensor and any individual or Legal Entity
on behalf of whom a Contribution has been received by Licensor and
subsequently incorporated within the Work.

2. Grant of Copyright License. Subject to the terms and conditions of
this License, each Contributor hereby grants to You a perpetual,
worldwide, non-exclusive, no-charge, royalty-free, irrevocable
copyright license to reproduce, prepare Derivative Works of,
publicly display, publicly perform, sublicense, and distribute the
Work and such Derivative Works in Source or Object form.

3. Grant of Patent License. Subject to the terms and conditions of
this License, each Contributor hereby grants to You a perpetual,
worldwide, non-exclusive, no-charge, royalty-free, irrevocable
(except as stated in this section) patent license to make, have made,
use, offer to sell, sell, import, and otherwise transfer the Work,
where such license applies only to those patent claims licensable
by such Contributor that are necessarily infringed by their
Contribution(s) alone or by combination of their Contribution(s)
with the Work to which such Contribution(s) was submitted. If You
institute patent litigation against any entity (including a
cross-claim or counterclaim in a lawsuit) alleging that the Work
or a Contribution incorporated within the Work constitutes direct
or contributory patent infringement, then any patent licenses
granted to You under this License for that Work shall terminate
as of the date such litigation is filed.

4. Redistribution. You may reproduce and distribute copies of the
Work or Derivative Works thereof in any medium, with or without
modifications, and in Source or Object form, provided that You
meet the following conditions:

(a) You must give any other recipients of the Work or
Derivative Works a copy of this License; and

(b) You must cause any modified files to carry prominent notices
stating that You changed the files; and

(c) You must retain, in the Source form of any Derivative Works
that You distribute, all copyright, patent, trademark, and
attribution notices from the Source form of the Work,
excluding those notices that do not pertain to any part of
the Derivative Works; and

(d) If the Work includes a "NOTICE" text file as part of its
distribution, then any Derivative Works that You distribute must
include a readable copy of the attribution notices contained
within such NOTICE file, excluding those notices that do not
pertain to any part of the Derivative Works, in at least one
of the following places: within a NOTICE text file distributed
as part of the Derivative Works; within the Source form or
documentation, if provided along with the Derivative Works; or,
within a display generated by the Derivative Works, if and
wherever such third-party notices normally appear. The contents
of the NOTICE file are for informational purposes only and
do not modify the License. You may add Your own attribution
notices within Derivative Works that You distribute, alongside
or as an addendum to the NOTICE text from the Work, provided
that such additional attribution notices cannot be construed
as modifying the License.

You may add Your own copyright statement to Your modifications and
may provide additional or different license terms and conditions
for use, reproduction, or distribution of Your modifications, or
for any such Derivative Works as a whole, provided Your use,
reproduction, and distribution of the Work otherwise complies with
the conditions stated in this License.

5. Submission of Contributions. Unless You explicitly state otherwise,
any Contribution intentionally submitted for inclusion in the Work
by You to the Licensor shall be under the terms and conditions of
this License, without any additional terms or conditions.
Notwithstanding the above, nothing herein shall supersede or modify
the terms of any separate license agreement you may have executed
with Licensor regarding such Contributions.

6. Trademarks. This License does not grant permission to use the trade
names, trademarks, service marks, or product names of the Licensor,
except as required for reasonable and customary use in describing the
origin of the Work and reproducing the content of the NOTICE file.

7. Disclaimer of Warranty. Unless required by applicable law or
agreed to in writing, Licensor provides the Work (and each
Contributor provides its Contributions) on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
implied, including, without limitation, any warranties or conditions
of TITLE, NON-INFRINGEMENT, MERCHANTABILITY, or FITNESS FOR A
PARTICULAR PURPOSE. You are solely responsible for determining the
appropriateness of using or redistributing the Work and assume any
risks associated with Your exercise of permissions under this License.

8. Limitation of Liability. In no event and under no legal theory,
whether in tort (including negligence), contract, or otherwise,
unless required by applicable law (such as deliberate and grossly
negligent acts) or agreed to in writing, shall any Contributor be
liable to You for damages, including any direct, indirect, special,
incidental, or consequential damages of any character arising as a
result of this License or out of the use or inability to use the
Work (including but not limited to damages for loss of goodwill,
work stoppage, computer failure or malfunction, or any and all
other commercial damages or losses), even if such Contributor
has been advised of the possibility of such damages.

9. Accepting Warranty or Additional Liability. While redistributing
the Work or Derivative Works thereof, You may choose to offer,
and charge a fee for, acceptance of support, warranty, indemnity,
or other liability obligations and/or rights consistent with this
License. However, in accepting such obligations, You may act only
on Your own behalf and on Your sole responsibility, not on behalf
of any other Contributor, and only if You agree to indemnify,
defend, and hold each Contributor harmless for any liability
incurred by, or claims asserted against, such Contributor by reason
of your accepting any such warranty or additional liability.

END OF TERMS AND CONDITIONS

APPENDIX: How to apply the Apache License to your work.

To apply the Apache License to your work, attach the following
boilerplate notice, with the fields enclosed by brackets "[]" replaced
with your own identifying information. (Don't include the brackets!)
The text should be enclosed in the appropriate comment syntax for the
file format. We also recommend that a file or class name and description
of purpose be included on the same "printed page" as the copyright
notice for easier identification within third-party archives.

Copyright (c) Six Labors
Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at
http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
"@ | Set-Content -LiteralPath $thirdPartyNoticesPath -Encoding UTF8

Copy-RequiredNoticeFile -PackageId "Microsoft.NETCore.App.Runtime.win-x64" -Version "10.0.12" -RelativePath "LICENSE.TXT" -DestinationName "Microsoft.NETCore.App.Runtime.win-x64-10.0.12-LICENSE.TXT"
Copy-RequiredNoticeFile -PackageId "Microsoft.NETCore.App.Runtime.win-x64" -Version "10.0.12" -RelativePath "THIRD-PARTY-NOTICES.TXT" -DestinationName "Microsoft.NETCore.App.Runtime.win-x64-10.0.12-THIRD-PARTY-NOTICES.TXT"
Copy-RequiredNoticeFile -PackageId "Microsoft.AspNetCore.App.Runtime.win-x64" -Version "10.0.12" -RelativePath "LICENSE.txt" -DestinationName "Microsoft.AspNetCore.App.Runtime.win-x64-10.0.12-LICENSE.txt"
Copy-RequiredNoticeFile -PackageId "Microsoft.AspNetCore.App.Runtime.win-x64" -Version "10.0.12" -RelativePath "THIRD-PARTY-NOTICES.TXT" -DestinationName "Microsoft.AspNetCore.App.Runtime.win-x64-10.0.12-THIRD-PARTY-NOTICES.TXT"
Copy-RequiredNoticeFile -PackageId "Microsoft.WindowsDesktop.App.Runtime.win-x64" -Version "10.0.12" -RelativePath "LICENSE" -DestinationName "Microsoft.WindowsDesktop.App.Runtime.win-x64-10.0.12-LICENSE"

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
- THIRD-PARTY-NOTICES.txt with the SixLabors.ImageSharp 2.1.11 Apache-2.0 notice from the exact pinned NuGet artifact.
- THIRD-PARTY-NOTICES/ with exact-version notice files copied from the .NET runtime packs that ship in this self-contained package.

Notice inventory status:
- The published deps manifest for the validated portable package lists SixLabors.ImageSharp/2.1.11 plus Microsoft.NETCore.App.Runtime.win-x64/10.0.12, Microsoft.WindowsDesktop.App.Runtime.win-x64/10.0.12, Microsoft.AspNetCore.App.Runtime.win-x64/10.0.12, and Microsoft.Windows.SDK.NET.Ref/10.0.17763.57.
- SixLabors.ImageSharp 2.1.11 notice is included because the exact pinned NuGet artifact declares Apache-2.0 and requireLicenseAcceptance=true.
- Runtime-pack license and notice files are copied from the exact local NuGet package folders; packaging fails if a required file is unavailable.
- Microsoft.Windows.SDK.NET.Ref/10.0.17763.57 is documented from Microsoft's REDIST.TXT at https://go.microsoft.com/fwlink/?LinkId=524842 for unmodified package files used as part of a program to call WinRT APIs. This is not legal advice and makes no broader redistribution claim.
- Do not substitute the current ImageSharp main-branch/newer-version Six Labors Split License for this pinned 2.1.11 artifact without re-verifying the redistributed package.
- This package covers the exact published deps/runtime-pack inventory named above. Re-run inventory before release if dependencies, target framework, runtime identifier, or publish contents change.
"@ | Set-Content -LiteralPath $instructionsPath -Encoding UTF8

Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $zipPath -Force
Assert-ZipHasNoSymbols -ArchivePath $zipPath

Write-Host "Published Windows Agent package: $zipPath"
Write-Host "Publish directory: $publishDirectory"
