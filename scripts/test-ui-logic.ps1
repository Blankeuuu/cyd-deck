$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$python = Join-Path $projectRoot '.tools\pio-venv\Scripts\python.exe'
$zig = Join-Path $projectRoot '.tools\ziglang-host\ziglang\zig.exe'
$source = Join-Path $projectRoot 'tests\ui_logic_test.cpp'
$includeDir = Join-Path $projectRoot 'firmware\include'
$fakeIncludeDir = Join-Path $projectRoot 'tests\fakes'
$arduinoJsonIncludeDir = Join-Path $projectRoot 'firmware\.pio\libdeps\cyd28_st7789_app\ArduinoJson\src'
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
    $tests = @(
        @{ Source = $source; Executable = (Join-Path $projectRoot '.tools\ui_logic_test.exe'); Includes = @($includeDir) },
        @{ Source = (Join-Path $projectRoot 'tests\onboarding_state_test.cpp'); Executable = (Join-Path $projectRoot '.tools\onboarding_state_test.exe'); Includes = @($includeDir) },
        @{ Source = (Join-Path $projectRoot 'tests\network_protocol_test.cpp'); Executable = (Join-Path $projectRoot '.tools\network_protocol_test.exe'); Includes = @($includeDir) },
        @{ Source = (Join-Path $projectRoot 'tests\paged_index_test.cpp'); Executable = (Join-Path $projectRoot '.tools\paged_index_test.exe'); Includes = @($includeDir) },
        @{ Source = (Join-Path $projectRoot 'tests\ui_layout_test.cpp'); Executable = (Join-Path $projectRoot '.tools\ui_layout_test.exe'); Includes = @($fakeIncludeDir, $includeDir) }
    )
    foreach ($test in $tests) {
        $includeArgs = @()
        foreach ($include in $test.Includes) {
            $includeArgs += '-I'
            $includeArgs += $include
        }
        $buildLog = "$($test.Executable).build.log"
        & $zig c++ -std=c++17 -Wall -Wextra @includeArgs $test.Source -o $test.Executable *> $buildLog
        if ($LASTEXITCODE -ne 0) {
            Get-Content -LiteralPath $buildLog
            $exitCode = $LASTEXITCODE
            break
        }
        & $test.Executable
        if ($LASTEXITCODE -ne 0) {
            $exitCode = $LASTEXITCODE
            break
        }
        $exitCode = 0
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
