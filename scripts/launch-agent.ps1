$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$agentExe = Join-Path $projectRoot 'artifacts\icon-agent\CYD-DECK.exe'
if (-not (Test-Path $agentExe)) {
    $agentExe = Join-Path $projectRoot 'artifacts\icon-agent\StreamDeck.WindowsAgent.exe'
}
$agentDir = Join-Path $projectRoot 'artifacts\icon-agent'
if (-not (Test-Path $agentExe)) { throw "Agent executable missing at $agentExe" }

$taskName = "LaunchCYDDeckAgent"
$action = New-ScheduledTaskAction -Execute $agentExe -WorkingDirectory $agentDir
$user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Force | Out-Null
Start-ScheduledTask -TaskName $taskName
Start-Sleep -Seconds 3
Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
Write-Host "Agent launched via ScheduledTask."
