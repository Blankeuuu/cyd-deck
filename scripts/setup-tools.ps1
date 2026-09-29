[CmdletBinding()]
param(
    [switch] $IncludeHostTestCompiler
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$toolsRoot = Join-Path $projectRoot '.tools'
$venv = Join-Path $toolsRoot 'pio-venv'
New-Item -ItemType Directory -Path $toolsRoot -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $venv 'Scripts\python.exe'))) {
    $pyLauncher = Get-Command py -ErrorAction SilentlyContinue
    if ($pyLauncher) {
        & $pyLauncher.Source -3 -m venv $venv
    } else {
        $pythonLauncher = Get-Command python -ErrorAction SilentlyContinue
        if (-not $pythonLauncher) { throw 'Python 3 is required; install the py launcher or add python to PATH.' }
        & $pythonLauncher.Source -m venv $venv
    }
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$python = Join-Path $venv 'Scripts\python.exe'
& $python -m pip install platformio==6.2.0
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($IncludeHostTestCompiler) {
    $zigTarget = Join-Path $toolsRoot 'ziglang-host'
    if (-not (Test-Path -LiteralPath $zigTarget)) {
        & $python -m pip install --target $zigTarget ziglang==0.16.0
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}
Write-Host 'Local tools installed under .tools/. Use scripts/build-firmware.ps1 for firmware builds.'
