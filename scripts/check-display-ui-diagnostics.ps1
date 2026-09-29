param(
    [int]$ExpectedLandscapeRotation = 3
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$boardPins = Get-Content -Raw (Join-Path $projectRoot 'firmware\include\board_pins.h')
$uiManager = Get-Content -Raw (Join-Path $projectRoot 'firmware\src\ui\ui_manager.cpp')
$lvglPort = Get-Content -Raw (Join-Path $projectRoot 'firmware\src\ui\lvgl_port.cpp')
$app = Get-Content -Raw (Join-Path $projectRoot 'firmware\src\ui\app.cpp')

$failures = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]

function Add-Failure([string]$message) {
    $script:failures.Add($message)
}

function Add-Warning([string]$message) {
    $script:warnings.Add($message)
}

$rotationMatch = [regex]::Match($boardPins, 'landscapeRotation\s*=\s*(\d+)')
if (-not $rotationMatch.Success) {
    Add-Failure 'board::landscapeRotation was not found in firmware/include/board_pins.h.'
} else {
    $actualRotation = [int]$rotationMatch.Groups[1].Value
    if ($actualRotation -ne $ExpectedLandscapeRotation) {
        Add-Failure "landscapeRotation is $actualRotation, expected $ExpectedLandscapeRotation for the current physical 180-degree display report."
    } else {
        Write-Output "OK rotation: landscapeRotation=$actualRotation"
    }
}

if ($app -notmatch 'display\.setRotation\(board::landscapeRotation\)') {
    Add-Failure 'app.cpp does not apply board::landscapeRotation to the display.'
} else {
    Write-Output 'OK rotation plumbing: app.cpp applies board::landscapeRotation.'
}

$hasScrollableClear = $uiManager -match 'LV_OBJ_FLAG_SCROLLABLE'
$hasScrollbarOff = $uiManager -match 'LV_SCROLLBAR_MODE_OFF'
$hasZeroPadding = $uiManager -match 'lv_obj_set_style_pad_(all|left|right|top|bottom)\s*\([^,]+,\s*0\s*,'
$hasStyleReset = $uiManager -match 'lv_obj_remove_style_all'

if (-not ($hasScrollableClear -or $hasScrollbarOff)) {
    Add-Failure 'ui_manager.cpp creates full-screen LVGL containers but never disables scrollability/scrollbars.'
} else {
    Write-Output 'OK scrollbars: UI explicitly disables scrollability or scrollbar mode.'
}

if (-not ($hasZeroPadding -or $hasStyleReset)) {
    Add-Failure 'ui_manager.cpp uses default LVGL object styles without clearing/resetting container padding.'
} else {
    Write-Output 'OK padding: UI explicitly clears/reset padding or styles.'
}

$usesRgb565 = $lvglPort -match 'LV_COLOR_FORMAT_RGB565'
$pushesRawBuffer = $lvglPort -match 'pushImage\([^;]+reinterpret_cast<uint16_t\*>\(px\)'
$hasByteSwap = ($lvglPort + "`n" + $app) -match 'setSwapBytes|lv_draw_sw_rgb565_swap|LV_COLOR_16_SWAP'
if ($usesRgb565 -and $pushesRawBuffer -and -not $hasByteSwap) {
    Add-Warning 'LVGL RGB565 draw buffer is pushed to LovyanGFX without an explicit byte-swap step; verify with red/green/blue test after geometry fixes.'
} else {
    Write-Output 'OK color path: RGB565 transfer has an explicit swap/conversion marker or does not use the raw RGB565 path.'
}

foreach ($warning in $warnings) {
    Write-Warning $warning
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Error $failure -ErrorAction Continue
    }
    exit 1
}

Write-Output 'display UI diagnostics passed'
