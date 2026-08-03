param(
    [string]$ServerUrl = "https://tennis-intelligence.onrender.com",
    [string]$ProjectPath = "$PSScriptRoot\..\TennisIntelligence.csproj"
)

$ErrorActionPreference = "Stop"

$connectorKeyPath = Join-Path $env:APPDATA "TennisTracker\connector-key.txt"
if (-not (Test-Path $connectorKeyPath)) {
    throw "The connector key is not configured locally."
}
$databasePath = Join-Path $env:APPDATA "TennisTracker\neon-connection.txt"
if (-not (Test-Path $databasePath)) {
    throw "The Neon connection is not configured locally."
}

$secureKey = Get-Content $connectorKeyPath | ConvertTo-SecureString
$connectorKey = [System.Net.NetworkCredential]::new("", $secureKey).Password
if ([string]::IsNullOrWhiteSpace($connectorKey)) {
    throw "The stored connector key could not be decrypted."
}
$secureDatabase = Get-Content $databasePath | ConvertTo-SecureString
$databaseUrl = [System.Net.NetworkCredential]::new("", $secureDatabase).Password
if ([string]::IsNullOrWhiteSpace($databaseUrl)) {
    throw "The stored Neon connection could not be decrypted."
}

$project = (Resolve-Path $ProjectPath).Path
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://127.0.0.1:5082"
$env:DATABASE_URL = $databaseUrl
$env:Connector__ApiKey = $connectorKey
$env:Connector__PairingServerUrl = $ServerUrl.TrimEnd('/')

$process = Start-Process `
    -FilePath "dotnet" `
    -ArgumentList @("run", "--project", $project, "--no-launch-profile") `
    -PassThru `
    -NoNewWindow

try {
    $ready = $false
    foreach ($attempt in 1..30) {
        Start-Sleep -Seconds 1
        if ($process.HasExited) {
            throw "The local pairing server exited before it became ready."
        }

        if (Test-NetConnection 127.0.0.1 -Port 5082 -InformationLevel Quiet -WarningAction SilentlyContinue) {
            $ready = $true
            break
        }
    }

    if (-not $ready) {
        throw "The local pairing server did not become ready."
    }

    Start-Process "http://127.0.0.1:5082/Imports"
    Write-Host "Pairing page opened. Scan the QR code, then close this window." -ForegroundColor Green
    Wait-Process -Id $process.Id
}
finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id
    }
}
