param(
    [string]$ProjectId = "curly-dream-74227927",
    [string]$DatabaseName = "tennis_intelligence",
    [string]$RoleName = "tennis_owner"
)

$ErrorActionPreference = "Stop"

$rawLines = npx --yes neonctl@latest connection-string `
    --project-id $ProjectId `
    --database-name $DatabaseName `
    --role-name $RoleName `
    --no-color
if ($LASTEXITCODE -ne 0) {
    throw "Neon CLI could not retrieve the database connection."
}

$connectionString = ($rawLines -join "").Trim()
if (-not $connectionString.StartsWith("postgresql://", [StringComparison]::OrdinalIgnoreCase)) {
    throw "Neon CLI returned an invalid PostgreSQL connection URL."
}

$credentialDirectory = Join-Path $env:APPDATA "TennisTracker"
$credentialPath = Join-Path $credentialDirectory "neon-connection.txt"
New-Item -ItemType Directory -Path $credentialDirectory -Force | Out-Null

$secureConnection = ConvertTo-SecureString $connectionString -AsPlainText -Force
$secureConnection | ConvertFrom-SecureString | Set-Content -Path $credentialPath -Encoding utf8

& "$PSScriptRoot\Backup-Neon.ps1"
if ($LASTEXITCODE -ne 0) {
    throw "The initial Neon backup failed."
}

& "$PSScriptRoot\Register-NeonBackup.ps1"
if ($LASTEXITCODE -ne 0) {
    throw "The daily Neon backup task could not be registered."
}

Write-Host "Neon credentials stored securely for the current Windows user." -ForegroundColor Green
