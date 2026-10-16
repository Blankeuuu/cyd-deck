$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$python = Join-Path $projectRoot '.tools\pio-venv\Scripts\python.exe'
$zig = Join-Path $projectRoot '.tools\ziglang-host\ziglang\zig.exe'
$source = Join-Path $projectRoot 'tests\ui_logic_test.cpp'
$includeDir = Join-Path $projectRoot 'firmware\include'
$fakeIncludeDir = Join-Path $projectRoot 'tests\fakes'
$lvglDir = Join-Path $projectRoot 'firmware\.pio\libdeps\cyd28_st7789_app\lvgl'
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
        @{ Compiler = 'c++'; Standard = 'c++17'; Sources = @($source); Executable = (Join-Path $projectRoot '.tools\ui_logic_test.exe'); Includes = @($includeDir) },
        @{ Compiler = 'c++'; Standard = 'c++17'; Sources = @((Join-Path $projectRoot 'tests\onboarding_state_test.cpp')); Executable = (Join-Path $projectRoot '.tools\onboarding_state_test.exe'); Includes = @($includeDir) },
        @{ Compiler = 'c++'; Standard = 'c++17'; Sources = @((Join-Path $projectRoot 'tests\network_protocol_test.cpp')); Executable = (Join-Path $projectRoot '.tools\network_protocol_test.exe'); Includes = @($includeDir) },
        @{ Compiler = 'c++'; Standard = 'c++17'; Sources = @((Join-Path $projectRoot 'tests\usb_protocol_test.cpp')); Executable = (Join-Path $projectRoot '.tools\usb_protocol_test.exe'); Includes = @($includeDir) },
        @{ Compiler = 'c++'; Standard = 'c++17'; Sources = @((Join-Path $projectRoot 'tests\paged_index_test.cpp')); Executable = (Join-Path $projectRoot '.tools\paged_index_test.exe'); Includes = @($includeDir) },
        @{ Compiler = 'c++'; Standard = 'c++17'; Sources = @((Join-Path $projectRoot 'tests\ui_layout_test.cpp')); Executable = (Join-Path $projectRoot '.tools\ui_layout_test.exe'); Includes = @($fakeIncludeDir, $includeDir) },
        @{
            Compiler = 'cc'
            Standard = 'c11'
            Sources = @(
                (Join-Path $projectRoot 'tests\font_glyph_test.c'),
                (Join-Path $projectRoot 'firmware\src\ui\fonts\streamdeck_polish_14.c'),
                (Join-Path $projectRoot 'firmware\src\ui\fonts\streamdeck_polish_16.c'),
                (Join-Path $projectRoot 'firmware\src\ui\fonts\streamdeck_polish_20.c'),
                (Join-Path $projectRoot 'firmware\src\ui\fonts\cyd_icons_20.c'),
                (Join-Path $lvglDir 'src\font\lv_font_montserrat_16.c'),
                (Join-Path $lvglDir 'src\font\lv_font_montserrat_20.c')
            )
            Executable = (Join-Path $projectRoot '.tools\font_glyph_test.exe')
            Includes = @($lvglDir, (Join-Path $lvglDir 'src'), $includeDir)
        }
    )
    foreach ($test in $tests) {
        $includeArgs = @()
        foreach ($include in $test.Includes) {
            $includeArgs += '-I'
            $includeArgs += $include
        }
        $buildLog = "$($test.Executable).build.log"
        & $zig $test.Compiler "-std=$($test.Standard)" -Wall -Wextra @includeArgs @($test.Sources) -o $test.Executable *> $buildLog
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
