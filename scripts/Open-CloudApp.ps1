param(
    [string]$ServerUrl = "https://tennis-intelligence.onrender.com"
)

$ErrorActionPreference = "Stop"

$passwordPath = Join-Path $env:APPDATA "TennisTracker\app-password.txt"
if (-not (Test-Path $passwordPath)) {
    throw "The cloud application password is not configured locally."
}

$securePassword = Get-Content $passwordPath | ConvertTo-SecureString
$password = [System.Net.NetworkCredential]::new("", $securePassword).Password
if ([string]::IsNullOrWhiteSpace($password)) {
    throw "The stored cloud application password could not be decrypted."
}

Set-Clipboard $password
Start-Process $ServerUrl
Write-Host "The application password was copied to the clipboard." -ForegroundColor Green
