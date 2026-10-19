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

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   CYD-DECK — Web Installer (Web Serial Flasher)          " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Serwer lokalny uruchomiony na porcie $Port" -ForegroundColor Green
Write-Host "Katalog: $webRoot" -ForegroundColor Gray
Write-Host "Otwieram przegladarke: http://localhost:$Port/" -ForegroundColor Yellow
Write-Host "Nacisnij Ctrl+C, aby zatrzymac serwer." -ForegroundColor DarkGray
Write-Host "----------------------------------------------------------"

Start-Process "http://localhost:$Port/"
& $python -m http.server $Port --directory $webRoot
