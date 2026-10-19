#Requires -Version 5.1
[CmdletBinding()]
param(
    [int]$Port = 8080
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$python = Join-Path $repoRoot '.tools\pio-venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $python)) {
    $python = 'python'
}

$webRoot = Join-Path $repoRoot 'installer\web'
if (-not (Test-Path -LiteralPath $webRoot)) {
    throw "Web installer directory not found: $webRoot"
}

# Verify Python is accessible
$pythonInstalled = $false
try {
    if (Test-Path -LiteralPath $python) {
        $pythonInstalled = $true
    } elseif (Get-Command $python -ErrorAction SilentlyContinue) {
        $pythonInstalled = $true
    }
} catch {
    $pythonInstalled = $false
}

if (-not $pythonInstalled) {
    Write-Host "[Error] Python was not found." -ForegroundColor Red
    Write-Host "Please install Python or use 'esptool' directly to flash the firmware." -ForegroundColor Yellow
    Write-Host "Direct flashing command:" -ForegroundColor Cyan
    Write-Host "  pip install esptool"
    Write-Host "  esptool.py --chip esp32 --port <PORT> --baud 460800 write_flash -z 0x0 installer\web\firmware-ST7789-1.3.0.bin"
    exit 1
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   CYD-DECK - Web Installer (Web Serial Flasher)          " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Local web server running at: http://localhost:$Port/" -ForegroundColor Green
Write-Host "Serving directory: $webRoot" -ForegroundColor Gray
Write-Host "Opening default browser... Press Ctrl+C to stop the server." -ForegroundColor Yellow
Write-Host "----------------------------------------------------------"

Start-Process "http://localhost:$Port/"
& $python -m http.server $Port --directory $webRoot
