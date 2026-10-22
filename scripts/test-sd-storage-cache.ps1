$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$compilerCandidate = Join-Path $projectRoot '.tools\windows-host-compiler\ziglang\zig.exe'
$zig = if (Test-Path -LiteralPath $compilerCandidate) { $compilerCandidate } else { Join-Path $projectRoot '.tools\ziglang-host\ziglang\zig.exe' }
if (-not (Test-Path -LiteralPath $zig)) { throw 'Host test compiler missing; run scripts/setup-tools.ps1 -IncludeHostTestCompiler.' }
$cache = Join-Path $projectRoot '.tools\zig-cache'
$exe = Join-Path $projectRoot '.tools\sd_storage_cache_test.exe'
$log = Join-Path $projectRoot '.tools\sd_storage_cache_test.build.log'
$hadGlobal = Test-Path Env:ZIG_GLOBAL_CACHE_DIR
$oldGlobal = $env:ZIG_GLOBAL_CACHE_DIR
$hadLocal = Test-Path Env:ZIG_LOCAL_CACHE_DIR
$oldLocal = $env:ZIG_LOCAL_CACHE_DIR
$exitCode = 1
try {
    New-Item -ItemType Directory -Path $cache -Force | Out-Null
    $env:ZIG_GLOBAL_CACHE_DIR = $cache
    $env:ZIG_LOCAL_CACHE_DIR = $cache
    $fakes = Join-Path $projectRoot 'tests\fakes'
    $includes = Join-Path $projectRoot 'firmware\include'
    $source = Join-Path $projectRoot 'tests\sd_storage_cache_test.cpp'
    $storage = Join-Path $projectRoot 'firmware\src\storage\sd_storage.cpp'
    $paths = Join-Path $projectRoot 'firmware\src\storage\sd_paths.cpp'
    & $zig c++ -std=c++17 -Wall -Wextra -DSD_STORAGE_HOST_TEST -I $fakes -I $includes $source $storage $paths -o $exe *> $log
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $log; $exitCode = $LASTEXITCODE }
    else { & $exe; $exitCode = $LASTEXITCODE }
} finally {
    if ($hadGlobal) { $env:ZIG_GLOBAL_CACHE_DIR = $oldGlobal }
    else { Remove-Item Env:ZIG_GLOBAL_CACHE_DIR -ErrorAction SilentlyContinue }
    if ($hadLocal) { $env:ZIG_LOCAL_CACHE_DIR = $oldLocal }
    else { Remove-Item Env:ZIG_LOCAL_CACHE_DIR -ErrorAction SilentlyContinue }
}
exit $exitCode
