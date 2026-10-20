[CmdletBinding()]
param(
    [string]$ExecutablePath,
    [string]$Arguments = "--usb-port COM5"
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (-not $ExecutablePath) {
    $installed = Join-Path $env:LOCALAPPDATA 'Programs\CYD-DECK\CYD-DECK.exe'
    if (Test-Path $installed) {
        $ExecutablePath = $installed
    } else {
        $candidate = Join-Path $projectRoot 'artifacts\dist\publish\CYD-DECK\CYD-DECK.exe'
        if (Test-Path $candidate) {
            $ExecutablePath = $candidate
        } else {
            $ExecutablePath = Join-Path $projectRoot 'artifacts\icon-agent\StreamDeck.WindowsAgent.exe'
        }
    }
}

if (-not (Test-Path $ExecutablePath)) {
    throw "Agent executable missing at $ExecutablePath"
}

$agentDir = Split-Path -Parent $ExecutablePath
$taskName = "LaunchCYDDeckAgent"
$action = if ($Arguments) {
    New-ScheduledTaskAction -Execute $ExecutablePath -Argument $Arguments -WorkingDirectory $agentDir
} else {
    New-ScheduledTaskAction -Execute $ExecutablePath -WorkingDirectory $agentDir
}

$user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Force | Out-Null
Start-ScheduledTask -TaskName $taskName
Start-Sleep -Seconds 3
Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
Write-Host "Agent launched via ScheduledTask: $ExecutablePath $Arguments"
