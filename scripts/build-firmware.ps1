$PioArgs = @($args)
if ($PioArgs.Count -eq 0) { $PioArgs = @('run') }
if ($PioArgs.Count -gt 0 -and $PioArgs[0] -eq 'run') {
  $hasJobLimit = $false
  foreach ($arg in $PioArgs) {
    if ($arg -eq '-j' -or $arg -eq '--jobs') { $hasJobLimit = $true }
  }
  if (-not $hasJobLimit) { $PioArgs += @('-j', '1') }
}
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$pioExecutable = Join-Path $projectRoot '.tools\pio-venv\Scripts\platformio.exe'
if (-not (Test-Path -LiteralPath $pioExecutable)) { throw "PlatformIO executable not found: $pioExecutable" }
$originalLocation = Get-Location
$hadCoreDir = Test-Path Env:PLATFORMIO_CORE_DIR
$originalCoreDir = $env:PLATFORMIO_CORE_DIR
$hadCanonicalCoreDir = Test-Path Env:STREAMDECK_PIO_CORE_CANONICAL_DIR
$originalCanonicalCoreDir = $env:STREAMDECK_PIO_CORE_CANONICAL_DIR
$hadBuildDir = Test-Path Env:PLATFORMIO_BUILD_DIR
$originalBuildDir = $env:PLATFORMIO_BUILD_DIR
$createdSubst = $false
$substDrive = $null
$driveRoot = $null
$exitCode = 1
try {
  $usedLetters = @(Get-PSDrive -PSProvider FileSystem | ForEach-Object { $_.Name.ToUpperInvariant() })
  foreach ($number in 90..68) {
    $letter = [char]$number
    if ($usedLetters -contains $letter.ToString()) { continue }
    $candidateDrive = $letter.ToString() + ':'
    & subst.exe $candidateDrive $projectRoot
    if ($LASTEXITCODE -ne 0) { continue }
    $createdSubst = $true
    $substDrive = $candidateDrive
    $driveRoot = $candidateDrive + [IO.Path]::DirectorySeparatorChar
    break
  }
  if (-not $createdSubst) { throw 'No free drive letter for the short project path.' }
  $env:PLATFORMIO_CORE_DIR = Join-Path $driveRoot '.tools\platformio-core'
  $env:STREAMDECK_PIO_CORE_CANONICAL_DIR = Join-Path $projectRoot '.tools\platformio-core'
  if (-not $hadBuildDir) {
    $explicitEnvs = @()
    for ($i = 0; $i -lt $PioArgs.Count; ++$i) {
      if (($PioArgs[$i] -eq '-e' -or $PioArgs[$i] -eq '--environment') -and $i + 1 -lt $PioArgs.Count) {
        $explicitEnvs += $PioArgs[$i + 1]
        ++$i
      }
    }
    $isAppBuild = $explicitEnvs.Count -gt 0 -and -not ($explicitEnvs | Where-Object { $_ -notmatch '_app$' })
    $buildFamily = if ($isAppBuild) { 'app' } else { 'diag' }
    $env:PLATFORMIO_BUILD_DIR = Join-Path $driveRoot ".pio-$buildFamily"
  }
  Set-Location (Join-Path $driveRoot 'firmware')
  $shortPio = Join-Path $driveRoot '.tools\pio-venv\Scripts\platformio.exe'
  & $shortPio @PioArgs
  $exitCode = $LASTEXITCODE
} catch {
  Write-Error $_
  $exitCode = 1
} finally {
  Set-Location $originalLocation
  if ($hadCoreDir) { $env:PLATFORMIO_CORE_DIR = $originalCoreDir }
  else { Remove-Item Env:PLATFORMIO_CORE_DIR -ErrorAction SilentlyContinue }
  if ($hadCanonicalCoreDir) { $env:STREAMDECK_PIO_CORE_CANONICAL_DIR = $originalCanonicalCoreDir }
  else { Remove-Item Env:STREAMDECK_PIO_CORE_CANONICAL_DIR -ErrorAction SilentlyContinue }
  if ($hadBuildDir) { $env:PLATFORMIO_BUILD_DIR = $originalBuildDir }
  else { Remove-Item Env:PLATFORMIO_BUILD_DIR -ErrorAction SilentlyContinue }
  if ($createdSubst -and $substDrive) {
    & subst.exe $substDrive /D
    if ($LASTEXITCODE -ne 0) {
      Write-Warning "Failed to remove temporary drive mapping $substDrive"
      if ($exitCode -eq 0) { $exitCode = 1 }
    }
  }
}
exit $exitCode
