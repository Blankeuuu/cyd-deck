param(
    [Parameter(Mandatory = $true)]
    [string]$DeviceIp,

    [Parameter(Mandatory = $true)]
    [string]$SetupPassword,

    [string]$Environment = 'cyd28_st7789_app',

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$python = Join-Path $projectRoot '.tools\pio-venv\Scripts\python.exe'
$espota = Join-Path $projectRoot '.tools\platformio-core\packages\framework-arduinoespressif32\tools\espota.py'
$firmwareBin = Join-Path $projectRoot ".pio-app\$Environment\firmware.bin"

if (-not (Test-Path -LiteralPath $python)) { throw "PlatformIO Python not found: $python" }
if (-not (Test-Path -LiteralPath $espota)) { throw "espota.py not found: $espota" }

if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot 'build-firmware.ps1') run -e $Environment
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not (Test-Path -LiteralPath $firmwareBin)) {
    throw "Firmware binary not found: $firmwareBin"
}

& $python $espota --debug --progress -i $DeviceIp -p 3232 -a $SetupPassword -f $firmwareBin
exit $LASTEXITCODE
