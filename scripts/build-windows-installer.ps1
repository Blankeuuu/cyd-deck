#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.3.0',
    [string]$OutputDirectory,
    [string]$IsccPath,
    [switch]$SkipPublish
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo "artifacts/release-v$Version" }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not $outputRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Installer output must be a dedicated directory under project artifacts/.'
}
if (-not $IsccPath) { $IsccPath = Join-Path $repo '.tools/inno-setup/ISCC.exe' }
if (-not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) { throw 'Install Inno Setup and pass -IsccPath to its ISCC.exe.' }
if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'publish-windows-agent.ps1') -OutputDirectory $outputRoot
}
$publishRoot = Join-Path $outputRoot 'publish/StreamDeck.WindowsAgent'
if (-not (Test-Path -LiteralPath (Join-Path $publishRoot 'StreamDeck.WindowsAgent.exe'))) { throw 'Published Agent executable is missing.' }
$actualVersion = (Get-Item -LiteralPath (Join-Path $publishRoot 'StreamDeck.WindowsAgent.exe')).VersionInfo.ProductVersion.Split('+')[0]
if ($actualVersion -ne $Version) { throw "Published version $actualVersion does not match installer version $Version." }
& $IsccPath '/Qp' "/DAppVersion=$Version" "/DPublishDir=$publishRoot" "/DReleaseDir=$outputRoot" (Join-Path $repo 'installer/windows-agent.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }
$installer = Join-Path $outputRoot "CYD-DECK-Setup-$Version-win-x64.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw 'Expected installer output is missing.' }
Write-Host "Installer ready: $installer"
