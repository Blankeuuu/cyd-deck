param([string]$OutputDirectory = 'artifacts/settings-preview')
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$compilerCandidate = Join-Path $projectRoot '.tools/windows-host-compiler/ziglang/zig.exe'
$zig = if (Test-Path -LiteralPath $compilerCandidate) { $compilerCandidate } else { Join-Path $projectRoot '.tools/ziglang-host/ziglang/zig.exe' }
$lvgl = 'firmware/.pio/libdeps/cyd28_st7789_app/lvgl'
$output = Join-Path $projectRoot $OutputDirectory
$executable = Join-Path $projectRoot '.tools/ui_render_preview.exe'
$response = Join-Path $projectRoot '.tools/ui_render_preview.rsp'
$buildLog = Join-Path $projectRoot '.tools/ui_render_preview.build.log'
if (-not (Test-Path $zig)) { throw 'Run scripts/setup-tools.ps1 -IncludeHostTestCompiler first.' }
$originalLocation = Get-Location
$hadGlobalCache = Test-Path Env:ZIG_GLOBAL_CACHE_DIR
$originalGlobalCache = $env:ZIG_GLOBAL_CACHE_DIR
$hadLocalCache = Test-Path Env:ZIG_LOCAL_CACHE_DIR
$originalLocalCache = $env:ZIG_LOCAL_CACHE_DIR
try {
    Set-Location $projectRoot
    if (-not (Test-Path "$lvgl/src")) { throw 'Build cyd28_st7789_app first to install pinned LVGL.' }
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $env:ZIG_GLOBAL_CACHE_DIR = Join-Path $projectRoot '.tools/zig-cache'
    $env:ZIG_LOCAL_CACHE_DIR = $env:ZIG_GLOBAL_CACHE_DIR
    $sources = @(rg --files "$lvgl/src" -g '*.c')
    if ($LASTEXITCODE -ne 0) { throw 'Could not list LVGL sources.' }
    $arguments = @('-O1', '-DLV_CONF_INCLUDE_SIMPLE', '-Ifirmware/include', "-I$lvgl", '-x', 'c')
    $arguments += $sources
    $arguments += @(rg --files 'firmware/src/ui/fonts' -g '*.c')
    $arguments += @('-x', 'c++', 'tests/ui_render_preview.cpp', '-o', '.tools/ui_render_preview.exe')
    [IO.File]::WriteAllLines($response, $arguments, [Text.UTF8Encoding]::new($false))
    $previousErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $zig c++ "@$response" *> $buildLog
    } finally { $ErrorActionPreference = $previousErrorAction }
    if ($LASTEXITCODE -ne 0) { Get-Content $buildLog -Tail 40; exit $LASTEXITCODE }
    & $executable $output
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Add-Type -AssemblyName System.Drawing
    foreach ($file in Get-ChildItem -LiteralPath $output -Filter '*.bmp') {
        $bitmap = [Drawing.Bitmap]::FromFile($file.FullName)
        try { $bitmap.Save([IO.Path]::ChangeExtension($file.FullName, '.png'), [Drawing.Imaging.ImageFormat]::Png) }
        finally { $bitmap.Dispose() }
    }
    Write-Output "Rendered real LVGL UI previews: $output"
} finally {
    Set-Location $originalLocation
    if ($hadGlobalCache) { $env:ZIG_GLOBAL_CACHE_DIR = $originalGlobalCache }
    else { Remove-Item Env:ZIG_GLOBAL_CACHE_DIR -ErrorAction SilentlyContinue }
    if ($hadLocalCache) { $env:ZIG_LOCAL_CACHE_DIR = $originalLocalCache }
    else { Remove-Item Env:ZIG_LOCAL_CACHE_DIR -ErrorAction SilentlyContinue }
}
