$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$python = Join-Path $projectRoot '.tools\pio-venv\Scripts\python.exe'
$zig = Join-Path $projectRoot '.tools\ziglang-host\ziglang\zig.exe'
$source = Join-Path $projectRoot 'tests\affine_calibration_test.cpp'
$executable = Join-Path $projectRoot '.tools\affine_calibration_test.exe'
$buildLog = Join-Path $projectRoot '.tools\affine_calibration_test.build.log'
$zigCache = Join-Path $projectRoot '.tools\zig-cache'
if (-not (Test-Path -LiteralPath $python)) { throw "Run scripts/setup-tools.ps1 first." }
if (-not (Test-Path -LiteralPath $zig)) { throw "Host test compiler missing; run scripts/setup-tools.ps1 -IncludeHostTestCompiler." }
$hadPythonPath = Test-Path Env:PYTHONPATH
$originalPythonPath = $env:PYTHONPATH
$hadZigGlobalCache = Test-Path Env:ZIG_GLOBAL_CACHE_DIR
$originalZigGlobalCache = $env:ZIG_GLOBAL_CACHE_DIR
$hadZigLocalCache = Test-Path Env:ZIG_LOCAL_CACHE_DIR
$originalZigLocalCache = $env:ZIG_LOCAL_CACHE_DIR
$exitCode = 1
try {
    New-Item -ItemType Directory -Path $zigCache -Force | Out-Null
    $env:ZIG_GLOBAL_CACHE_DIR = $zigCache
    $env:ZIG_LOCAL_CACHE_DIR = $zigCache
    & $zig c++ -std=c++17 -Wall -Wextra -Wno-nullability-completeness $source -o $executable *> $buildLog
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $buildLog
        $exitCode = $LASTEXITCODE
    } else {
        & $executable
        $exitCode = $LASTEXITCODE
    }
} finally {
    if ($hadPythonPath) { $env:PYTHONPATH = $originalPythonPath }
    else { Remove-Item Env:PYTHONPATH -ErrorAction SilentlyContinue }
    if ($hadZigGlobalCache) { $env:ZIG_GLOBAL_CACHE_DIR = $originalZigGlobalCache }
    else { Remove-Item Env:ZIG_GLOBAL_CACHE_DIR -ErrorAction SilentlyContinue }
    if ($hadZigLocalCache) { $env:ZIG_LOCAL_CACHE_DIR = $originalZigLocalCache }
    else { Remove-Item Env:ZIG_LOCAL_CACHE_DIR -ErrorAction SilentlyContinue }
}
exit $exitCode
