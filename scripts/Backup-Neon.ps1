param(
    [string]$BackupDirectory
)

$ErrorActionPreference = "Stop"

$credentialPath = Join-Path $env:APPDATA "TennisTracker\neon-connection.txt"
if (-not (Test-Path $credentialPath)) {
    throw "Neon credentials are not configured. Run Configure-NeonBackup.ps1 first."
}

$secureConnection = Get-Content $credentialPath | ConvertTo-SecureString
$credential = [System.Net.NetworkCredential]::new("", $secureConnection)
$uri = [Uri]$credential.Password
$separator = $uri.UserInfo.IndexOf(':')
if ($separator -le 0) {
    throw "The stored Neon connection URL is invalid."
}

$username = [Uri]::UnescapeDataString($uri.UserInfo.Substring(0, $separator))
$password = [Uri]::UnescapeDataString($uri.UserInfo.Substring($separator + 1))
$database = [Uri]::UnescapeDataString($uri.AbsolutePath.Trim('/'))
$port = if ($uri.IsDefaultPort) { 5432 } else { $uri.Port }
if ($uri.Host.Contains("-pooler.", [StringComparison]::OrdinalIgnoreCase)) {
    throw "Backups require Neon's direct connection, not a pooled connection."
}

if (-not $BackupDirectory) {
    $root = if ($env:OneDrive) {
        $env:OneDrive
    }
    else {
        [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)
    }
    $BackupDirectory = Join-Path $root "TennisTrackerBackups"
}
New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null

$pgDumpCommand = Get-Command "pg_dump" -ErrorAction SilentlyContinue
$pgDumpPath = if ($pgDumpCommand) { $pgDumpCommand.Source } else { $null }
if (-not $pgDumpPath) {
    $programFilesRoots = @($env:ProgramW6432, $env:ProgramFiles) |
        Where-Object { $_ } |
        Select-Object -Unique
    $pgDumpPath = $programFilesRoots |
        ForEach-Object {
            Get-ChildItem (Join-Path $_ "PostgreSQL\*\bin\pg_dump.exe") -ErrorAction SilentlyContinue
        } |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $pgDumpPath) {
    throw "pg_dump is unavailable. Install PostgreSQL command-line tools."
}

$backupPath = Join-Path $BackupDirectory "tennis-intelligence-$(Get-Date -Format 'yyyyMMdd-HHmmss').dump"
$partialPath = "$backupPath.partial"
$previousPassword = $env:PGPASSWORD
$previousSslMode = $env:PGSSLMODE
$completed = $false
try {
    $env:PGPASSWORD = $password
    $env:PGSSLMODE = "require"
    & $pgDumpPath `
        --host $uri.Host `
        --port $port `
        --username $username `
        --dbname $database `
        --format custom `
        --no-owner `
        --no-privileges `
        --file $partialPath
    if ($LASTEXITCODE -ne 0) {
        throw "pg_dump failed with exit code $LASTEXITCODE."
    }
    if ((Get-Item $partialPath).Length -eq 0) {
        throw "pg_dump created an empty backup."
    }

    Move-Item -LiteralPath $partialPath -Destination $backupPath
    $completed = $true
}
finally {
    $env:PGPASSWORD = $previousPassword
    $env:PGSSLMODE = $previousSslMode
    if (-not $completed -and (Test-Path $partialPath)) {
        Remove-Item -LiteralPath $partialPath -Force
    }
}

Get-ChildItem $BackupDirectory -Filter "tennis-intelligence-*.dump" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -Skip 30 |
    Remove-Item -Force

Write-Host "Backup created: $backupPath" -ForegroundColor Green
