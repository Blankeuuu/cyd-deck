param(
  [switch]$BuildOnly,
  [ValidateRange(1, 600)]
  [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$environmentName = 'cyd28_ili9341_wokwi_app'
$firmwareRoot = Join-Path $projectRoot 'firmware'
$buildScript = Join-Path $projectRoot 'scripts\build-firmware.ps1'

if (-not $BuildOnly -and [string]::IsNullOrWhiteSpace($env:WOKWI_CLI_TOKEN)) {
  throw 'WOKWI_CLI_TOKEN is required to run Wokwi CI. Set it in the process environment; do not save it in the repository.'
}

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $buildScript run -e $environmentName
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$buildRoots = @(
  (Join-Path $projectRoot '.pio-app'),
  (Join-Path $firmwareRoot '.pio\build')
)
$buildElf = $null
foreach ($buildRoot in $buildRoots) {
  if (-not (Test-Path -LiteralPath $buildRoot)) { continue }
  $buildElf = Get-ChildItem -LiteralPath $buildRoot -Filter 'firmware.elf' -File -Recurse |
    Where-Object { $_.Directory.Name -eq $environmentName } |
    Select-Object -First 1
  if ($buildElf) { break }
}
if (-not $buildElf) { throw "Could not locate firmware.elf for PlatformIO environment $environmentName." }

$buildDirectory = $buildElf.Directory.FullName
$artifactDirectory = Join-Path $firmwareRoot '.pio\wokwi'
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
foreach ($fileName in @('firmware.bin', 'firmware.elf', 'bootloader.bin', 'partitions.bin')) {
  $sourcePath = Join-Path $buildDirectory $fileName
  if (-not (Test-Path -LiteralPath $sourcePath)) { throw "Required Wokwi build artifact is missing: $sourcePath" }
  Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $artifactDirectory $fileName) -Force
}

$bootApp0 = Join-Path $projectRoot '.tools\platformio-core\packages\framework-arduinoespressif32\tools\partitions\boot_app0.bin'
$esptool = Join-Path $projectRoot '.tools\platformio-core\packages\tool-esptoolpy\esptool.py'
$python = Join-Path $projectRoot '.tools\pio-venv\Scripts\python.exe'
foreach ($requiredPath in @($bootApp0, $esptool, $python)) {
  if (-not (Test-Path -LiteralPath $requiredPath)) { throw "Required local PlatformIO tool is missing: $requiredPath" }
}

$mergedFirmware = Join-Path $artifactDirectory 'firmware-merged.bin'
& $python $esptool --chip esp32 merge_bin --output $mergedFirmware --flash_mode dio --flash_freq 40m --flash_size 4MB `
  0x1000 (Join-Path $artifactDirectory 'bootloader.bin') `
  0x8000 (Join-Path $artifactDirectory 'partitions.bin') `
  0xe000 $bootApp0 `
  0x10000 (Join-Path $artifactDirectory 'firmware.bin')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($BuildOnly) {
  Write-Host "Wokwi firmware image prepared: $mergedFirmware"
  exit 0
}

$cli = Get-Command 'wokwi-cli' -ErrorAction SilentlyContinue
$cliPath = if ($cli) { $cli.Source } else { Join-Path $projectRoot '.tools\wokwi-cli\wokwi-cli.exe' }
if (-not (Test-Path -LiteralPath $cliPath)) {
  throw 'Wokwi CLI is not installed. Install it from https://docs.wokwi.com/wokwi-ci/cli-installation.'
}

& $cliPath $firmwareRoot --timeout $TimeoutSeconds --expect-text '[WOKWI] APP_UI_READY'
exit $LASTEXITCODE
