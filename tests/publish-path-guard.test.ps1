#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script = Join-Path $repo 'scripts/publish-windows-agent.ps1'
if (-not (Test-Path -LiteralPath $script -PathType Leaf)) {
    throw "Publish script missing: $script"
}

# All cases must fail before New-Item, Remove-Item, or dotnet publish can run.
$blocked = @(
    $repo,
    (Split-Path $repo -Parent),
    (Join-Path $repo 'firmware'),
    (Join-Path $repo 'firmware/src'),
    (Join-Path $repo 'artifacts/../firmware')
)
foreach ($directory in $blocked) {
    try {
        & $script -OutputDirectory $directory -DotNetPath '__nonexistent_dotnet__' -SkipQualityGate
        throw "Unsafe output path accepted: $directory"
    }
    catch {
        if ($_.Exception.Message -notlike 'OutputDirectory must be a dedicated directory*') {
            throw "Unexpected failure for $directory`: $($_.Exception.Message)"
        }
        Write-Host "PASS reject: $directory"
    }
}

$scratchRoot = if ([string]::IsNullOrWhiteSpace($env:TMPDIR)) { [System.IO.Path]::GetTempPath() } else { $env:TMPDIR }
$allowedSandbox = Join-Path $scratchRoot ("stream-deck-publish-allowed-$([guid]::NewGuid().ToString('N'))")
try {
    try {
        & $script -OutputDirectory $allowedSandbox -DotNetPath '__nonexistent_dotnet__' -SkipQualityGate
        throw 'Publish unexpectedly completed with a nonexistent dotnet command'
    }
    catch [System.Management.Automation.CommandNotFoundException] {
        Write-Host 'PASS allow: dedicated scratch output reached fake dotnet'
    }
}
finally {
    if (Test-Path -LiteralPath $allowedSandbox) {
        Remove-Item -LiteralPath $allowedSandbox -Recurse -Force
    }
}

$sandbox = Join-Path $scratchRoot ("stream-deck-publish-path-guard-$([guid]::NewGuid().ToString('N'))")
$link = Join-Path $sandbox 'repo-link'
New-Item -ItemType Directory -Path $sandbox -Force | Out-Null
try {
    if (Test-Path -LiteralPath $link) {
        throw "Refusing to replace an existing test path: $link"
    }
    New-Item -ItemType SymbolicLink -Path $link -Target $repo -ErrorAction Stop | Out-Null
    try {
        & $script -OutputDirectory (Join-Path $link 'firmware') -DotNetPath '__nonexistent_dotnet__' -SkipQualityGate
        throw 'Unsafe linked output path accepted'
    }
    catch {
        if ($_.Exception.Message -notlike 'OutputDirectory must not traverse a junction or symbolic link*') {
            throw "Unexpected linked path failure: $($_.Exception.Message)"
        }
        Write-Host 'PASS reject: symlink into repository'
    }
}
finally {
    if (Test-Path -LiteralPath $link) {
        [System.IO.Directory]::Delete($link)
    }
    [System.IO.Directory]::Delete($sandbox)
}

$tokens = $null
$parseErrors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile($script, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) {
    throw "Publish script has a parser error: $($parseErrors[0])"
}
Write-Host 'PASS publish script syntax'
