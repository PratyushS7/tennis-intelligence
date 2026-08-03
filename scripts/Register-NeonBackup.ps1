param(
    [string]$TaskName = "TennisTracker Neon Backup"
)

$ErrorActionPreference = "Stop"

$shell = Get-Command "pwsh" -ErrorAction SilentlyContinue
if (-not $shell) {
    $shell = Get-Command "powershell" -ErrorAction Stop
}

$backupScript = Join-Path $PSScriptRoot "Backup-Neon.ps1"
$action = New-ScheduledTaskAction `
    -Execute $shell.Source `
    -Argument "-NoLogo -NoProfile -NonInteractive -File `"$backupScript`""
$triggers = @(
    New-ScheduledTaskTrigger -Daily -At 2am
    New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
)
$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Hours 1)
$principal = New-ScheduledTaskPrincipal `
    -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) `
    -LogonType Interactive `
    -RunLevel Limited

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger $triggers `
    -Settings $settings `
    -Principal $principal `
    -Description "Creates a daily PostgreSQL backup for TennisTracker." `
    -Force | Out-Null

Write-Host "Daily backup task registered for 2:00 AM." -ForegroundColor Green
