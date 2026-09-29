$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$uiManager = Get-Content -Raw (Join-Path $projectRoot 'firmware\src\ui\ui_manager.cpp')
$lvglPort = Get-Content -Raw (Join-Path $projectRoot 'firmware\src\ui\lvgl_port.cpp')
$app = Get-Content -Raw (Join-Path $projectRoot 'firmware\src\ui\app.cpp')

function ConvertTo-Rgb565([int]$rgb888) {
    $r = ($rgb888 -shr 16) -band 0xff
    $g = ($rgb888 -shr 8) -band 0xff
    $b = $rgb888 -band 0xff
    return (($r -band 0xf8) -shl 8) -bor (($g -band 0xfc) -shl 3) -bor ($b -shr 3)
}

function ConvertFrom-Rgb565([int]$rgb565) {
    $r5 = ($rgb565 -shr 11) -band 0x1f
    $g6 = ($rgb565 -shr 5) -band 0x3f
    $b5 = $rgb565 -band 0x1f
    return [pscustomobject]@{
        R = ($r5 -shl 3) -bor ($r5 -shr 2)
        G = ($g6 -shl 2) -bor ($g6 -shr 4)
        B = ($b5 -shl 3) -bor ($b5 -shr 2)
    }
}

function Swap-Bytes16([int]$value) {
    return (($value -band 0xff) -shl 8) -bor (($value -shr 8) -band 0xff)
}

function ConvertTo-HexColor($rgb) {
    return '#{0:X2}{1:X2}{2:X2}' -f $rgb.R, $rgb.G, $rgb.B
}

function Assert-Equal($name, $actual, $expected) {
    if ($actual -ne $expected) {
        Write-Error "$name expected $expected, got $actual."
        exit 1
    }
}

function Is-PurpleLike($rgb) {
    return $rgb.R -gt 80 -and $rgb.B -gt 80 -and $rgb.G -lt 90
}

function Is-GreenLike($rgb) {
    return $rgb.G -gt 150 -and $rgb.R -lt 100 -and $rgb.B -lt 80
}

$palette = [ordered]@{}
$matches = [regex]::Matches($uiManager, 'constexpr\s+uint32_t\s+(k\w+)\s*=\s*0x([0-9A-Fa-f]{6})')
foreach ($match in $matches) {
    $palette[$match.Groups[1].Value] = [Convert]::ToInt32($match.Groups[2].Value, 16)
}

foreach ($required in @('kBg', 'kPanel', 'kButton', 'kText', 'kMuted', 'kAccent')) {
    if (-not $palette.Contains($required)) {
        Write-Error "Missing UI palette constant $required."
        exit 1
    }
}

$rawRgb565Push = $lvglPort -match 'LV_COLOR_FORMAT_RGB565' -and
    $lvglPort -match 'pushImage\([^;]+reinterpret_cast<uint16_t\*>\(px\)'
$explicitSwap = ($lvglPort + "`n" + $app) -match 'setSwapBytes|lv_draw_sw_rgb565_swap|LV_COLOR_16_SWAP'

Assert-Equal 'RGB565 #101216' (('0x{0:X4}' -f (ConvertTo-Rgb565 0x101216))) '0x1082'
Assert-Equal 'byte-swapped #101216 as RGB888' (ConvertTo-HexColor (ConvertFrom-Rgb565 (Swap-Bytes16 (ConvertTo-Rgb565 0x101216)))) '#844184'
Assert-Equal 'RGB565 #252B33' (('0x{0:X4}' -f (ConvertTo-Rgb565 0x252B33))) '0x2146'
Assert-Equal 'byte-swapped #252B33 as RGB888' (ConvertTo-HexColor (ConvertFrom-Rgb565 (Swap-Bytes16 (ConvertTo-Rgb565 0x252B33)))) '#42C708'
Assert-Equal 'red RGB565 byte swap' (('0x{0:X4}' -f (Swap-Bytes16 (ConvertTo-Rgb565 0xFF0000)))) '0x00F8'
Assert-Equal 'green RGB565 byte swap' (('0x{0:X4}' -f (Swap-Bytes16 (ConvertTo-Rgb565 0x00FF00)))) '0xE007'
Assert-Equal 'blue RGB565 byte swap' (('0x{0:X4}' -f (Swap-Bytes16 (ConvertTo-Rgb565 0x0000FF)))) '0x1F00'

$rows = foreach ($name in $palette.Keys) {
    $source = $palette[$name]
    $rgb565 = ConvertTo-Rgb565 $source
    $swappedRgb = ConvertFrom-Rgb565 (Swap-Bytes16 $rgb565)
    [pscustomobject]@{
        Name = $name
        SourceHex = ('#{0:X6}' -f $source)
        Rgb565 = ('0x{0:X4}' -f $rgb565)
        ByteSwappedAsRgb = ('#{0:X2}{1:X2}{2:X2}' -f $swappedRgb.R, $swappedRgb.G, $swappedRgb.B)
    }
}

$rows | Format-Table -AutoSize

$bgSwapped = ConvertFrom-Rgb565 (Swap-Bytes16 (ConvertTo-Rgb565 $palette['kBg']))
$buttonSwapped = ConvertFrom-Rgb565 (Swap-Bytes16 (ConvertTo-Rgb565 $palette['kButton']))
$panelSwapped = ConvertFrom-Rgb565 (Swap-Bytes16 (ConvertTo-Rgb565 $palette['kPanel']))

if ($rawRgb565Push -and -not $explicitSwap -and
    (Is-PurpleLike $bgSwapped) -and (Is-GreenLike $buttonSwapped)) {
    Write-Error 'Current LVGL flush path pushes RGB565 as raw uint16_t without an explicit byte swap; byte-swapped palette predicts the purple background and green buttons visible in the photos.'
    exit 1
}

if ($rawRgb565Push -and -not $explicitSwap -and
    (Is-PurpleLike $panelSwapped)) {
    Write-Warning 'Panel color also maps toward purple under byte-swap; verify with physical RED/GREEN/BLUE bars after adding an explicit conversion.'
}

Write-Output 'display color path check passed'
