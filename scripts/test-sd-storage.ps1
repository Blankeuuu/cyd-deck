$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$zig = Join-Path $projectRoot '.tools\ziglang-host\ziglang\zig.exe'
$testSource = Join-Path $projectRoot 'tests\sd_storage_paths_test.cpp'
$pathSource = Join-Path $projectRoot 'firmware\src\storage\sd_paths.cpp'
$executable = Join-Path $projectRoot '.tools\sd_storage_paths_test.exe'
$buildLog = Join-Path $projectRoot '.tools\sd_storage_paths_test.build.log'
$includeDir = Join-Path $projectRoot 'firmware\include'
$zigCache = Join-Path $projectRoot '.tools\zig-cache'
if (-not (Test-Path -LiteralPath $zig)) { throw "Host test compiler missing; run scripts/setup-tools.ps1 -IncludeHostTestCompiler." }
$hadZigGlobalCache = Test-Path Env:ZIG_GLOBAL_CACHE_DIR
$originalZigGlobalCache = $env:ZIG_GLOBAL_CACHE_DIR
$hadZigLocalCache = Test-Path Env:ZIG_LOCAL_CACHE_DIR
$originalZigLocalCache = $env:ZIG_LOCAL_CACHE_DIR
$exitCode = 1
try {
    New-Item -ItemType Directory -Path $zigCache -Force | Out-Null
    $env:ZIG_GLOBAL_CACHE_DIR = $zigCache
    $env:ZIG_LOCAL_CACHE_DIR = $zigCache
    & $zig c++ -std=c++17 -Wall -Wextra -I $includeDir $testSource $pathSource -o $executable *> $buildLog
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $buildLog; $exitCode = $LASTEXITCODE }
    else { & $executable; $exitCode = $LASTEXITCODE }
} finally {
    if ($hadZigGlobalCache) { $env:ZIG_GLOBAL_CACHE_DIR = $originalZigGlobalCache }
    else { Remove-Item Env:ZIG_GLOBAL_CACHE_DIR -ErrorAction SilentlyContinue }
    if ($hadZigLocalCache) { $env:ZIG_LOCAL_CACHE_DIR = $originalZigLocalCache }
    else { Remove-Item Env:ZIG_LOCAL_CACHE_DIR -ErrorAction SilentlyContinue }
}
exit $exitCode
