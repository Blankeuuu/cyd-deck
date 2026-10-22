$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$zigCandidate = Join-Path $root '.tools\windows-host-compiler\ziglang\zig.exe'
$zig = if (Test-Path -LiteralPath $zigCandidate) { $zigCandidate } else { Join-Path $root '.tools\ziglang-host\ziglang\zig.exe' }
if (-not (Test-Path -LiteralPath $zig)) { throw 'Host test compiler missing; run scripts/setup-tools.ps1 -IncludeHostTestCompiler.' }
$zigCache = Join-Path $root '.tools\zig-cache'
$hadGlobalCache = Test-Path Env:ZIG_GLOBAL_CACHE_DIR
$oldGlobalCache = $env:ZIG_GLOBAL_CACHE_DIR
$hadLocalCache = Test-Path Env:ZIG_LOCAL_CACHE_DIR
$oldLocalCache = $env:ZIG_LOCAL_CACHE_DIR
$exe = Join-Path $root '.tools\cover_pixel_buffers_test.exe'
New-Item -ItemType Directory -Path (Split-Path $exe) -Force | Out-Null
try {
    New-Item -ItemType Directory -Path $zigCache -Force | Out-Null
    $env:ZIG_GLOBAL_CACHE_DIR = $zigCache
    $env:ZIG_LOCAL_CACHE_DIR = $zigCache
    & $zig c++ '-std=c++17' '-Wall' '-Wextra' '-Werror' "-I$(Join-Path $root 'firmware\include')" (Join-Path $root 'tests\cover_pixel_buffers_test.cpp') '-o' $exe
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $exe
    exit $LASTEXITCODE
} finally {
    if ($hadGlobalCache) { $env:ZIG_GLOBAL_CACHE_DIR = $oldGlobalCache } else { Remove-Item Env:ZIG_GLOBAL_CACHE_DIR -ErrorAction SilentlyContinue }
    if ($hadLocalCache) { $env:ZIG_LOCAL_CACHE_DIR = $oldLocalCache } else { Remove-Item Env:ZIG_LOCAL_CACHE_DIR -ErrorAction SilentlyContinue }
}
