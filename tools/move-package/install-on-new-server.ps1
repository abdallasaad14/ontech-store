<#
.SYNOPSIS
  Installs the OnTech Store server on a NEW PC with SQL Server, and moves the data from the old SQLite file.
.DESCRIPTION
  Run from an elevated PowerShell (Run as administrator), inside the package folder:
    powershell -ExecutionPolicy Bypass -File .\install-on-new-server.ps1
  Steps (README-AR.md explains each one):
    1. checks SQL Server Express is installed (instance .\SQLEXPRESS by default)
    2. copies the server to C:\OnTechStore\Server and the tool to C:\OnTechStore\Tool
    3. creates the database with the server's migrations and copies every row from data\ontech-store-dev.db,
       then checks that every table has the same number of rows (stops on any difference)
    4. lets the Windows service account (LocalSystem) own the database: Windows sign-in, no SQL password to keep
    5. installs the Windows service "OnTechStoreServer" (starts with Windows, restarts if it crashes),
       Production mode, the sign-in key from secrets\jwt-key.txt, the connection string
    6. opens TCP port 5080 in the firewall (private and domain networks)
    7. schedules a nightly SQL Server backup at 02:00 to C:\OnTechStore\Backups (newest 30 kept)
    8. starts the service and checks /api/health
  Use -SkipCopy to re-run the install without copying data again (the database must already hold the data).
#>
param(
    [string]$SqliteFile = (Join-Path $PSScriptRoot "data\ontech-store-dev.db"),
    [string]$SqlInstance = ".\SQLEXPRESS",
    [string]$Database = "OnTechStore",
    [string]$InstallDir = "C:\OnTechStore\Server",
    [string]$ToolDir = "C:\OnTechStore\Tool",
    [string]$BackupDir = "C:\OnTechStore\Backups",
    [string]$ServiceName = "OnTechStoreServer",
    [int]$Port = 5080,
    [switch]$SkipCopy
)
$ErrorActionPreference = "Stop"

function Step($text) { Write-Host ""; Write-Host "== $text" -ForegroundColor Cyan }

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this from PowerShell opened with 'Run as administrator'."
}

Step "1. SQL Server"
$instanceName = if ($SqlInstance -match '\\(.+)$') { $Matches[1] } else { "MSSQLSERVER" }
$sqlServiceName = if ($instanceName -eq "MSSQLSERVER") { "MSSQLSERVER" } else { "MSSQL`$$instanceName" }
$sqlService = Get-Service $sqlServiceName -ErrorAction SilentlyContinue
if (-not $sqlService) {
    throw "SQL Server instance '$SqlInstance' is not installed. Install SQL Server 2022 Express (Basic install) from https://www.microsoft.com/sql-server/sql-server-downloads , then run this script again."
}
if ($sqlService.Status -ne "Running") { Start-Service $sqlServiceName }
Set-Service $sqlServiceName -StartupType Automatic
$cs = "Server=$SqlInstance;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False"
Write-Host "OK: $sqlServiceName is running."

Step "2. Copy the server and the tool"
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -eq "Running") { Stop-Service $ServiceName; Start-Sleep 2 }
New-Item -ItemType Directory -Force $InstallDir, $ToolDir, $BackupDir | Out-Null
Copy-Item (Join-Path $PSScriptRoot "server\*") $InstallDir -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot "tool\*") $ToolDir -Recurse -Force
$tool = Join-Path $ToolDir "OnTechStoreMoveToSqlServer.exe"
Write-Host "OK: $InstallDir, $ToolDir"

if (-not $SkipCopy) {
    Step "3. Move the data from SQLite to SQL Server"
    if (-not (Test-Path $SqliteFile)) { throw "The old database file is missing: $SqliteFile (README-AR.md, step 2)." }
    & $tool copy --sqlite $SqliteFile --sqlserver $cs
    if ($LASTEXITCODE -ne 0) { throw "Moving the data failed (see above). Nothing was installed; the old server can keep running." }
}

Step "4. Database owner for the service"
& $tool grant --sqlserver $cs
if ($LASTEXITCODE -ne 0) { throw "Could not give the service account access to the database." }

Step "5. Windows service"
if (-not $existing) {
    New-Service -Name $ServiceName -DisplayName "OnTech Store Server" `
        -Description "Stock, customers and orders API for the OnTech store desktop app." `
        -BinaryPathName "`"$InstallDir\OnTech.Store.Api.exe`" --urls http://0.0.0.0:$Port" -StartupType Automatic | Out-Null
    sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
}
$keyFile = Join-Path $PSScriptRoot "secrets\jwt-key.txt"
if (Test-Path $keyFile) {
    $key = (Get-Content $keyFile -Raw).Trim()
    Write-Host "Using the old server's sign-in key: users stay signed in."
} else {
    $bytes = New-Object byte[] 48
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $key = [Convert]::ToBase64String($bytes)
    Write-Host "No secrets\jwt-key.txt: made a new sign-in key. Every user signs in once."
}
$envLines = [string[]]@(
    "ASPNETCORE_ENVIRONMENT=Production",
    "Database__Provider=SqlServer",
    "ConnectionStrings__Store=$cs",
    "Jwt__SigningKey=$key"
)
New-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" -Name Environment -PropertyType MultiString -Value $envLines -Force | Out-Null
Write-Host "OK: service $ServiceName (automatic start, restarts on failure)."

Step "6. Firewall"
if (-not (Get-NetFirewallRule -DisplayName "OnTech Store Server" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "OnTech Store Server" -Direction Inbound -Protocol TCP -LocalPort $Port -Profile Private,Domain -Action Allow | Out-Null
}
Write-Host "OK: TCP $Port open on private and domain networks."

Step "7. Nightly backup"
# SQL Server itself writes the .bak file, so its service account needs write access to the folder.
icacls $BackupDir /grant "NT SERVICE\$($sqlServiceName):(OI)(CI)M" | Out-Null
$action = New-ScheduledTaskAction -Execute $tool -Argument "backup --sqlserver `"$cs`" --folder `"$BackupDir`" --keep 30"
$trigger = New-ScheduledTaskTrigger -Daily -At 2am
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName "OnTech Store nightly backup" -Action $action -Trigger $trigger -Principal $principal `
    -Settings (New-ScheduledTaskSettingsSet -StartWhenAvailable) -Force | Out-Null
& $tool backup --sqlserver $cs --folder $BackupDir --keep 30
if ($LASTEXITCODE -ne 0) { throw "The first backup failed." }
Write-Host "OK: every night at 02:00 to $BackupDir (and one backup just now)."

Step "8. Start and check"
Start-Service $ServiceName
$ok = $false
for ($i = 0; $i -lt 30 -and -not $ok; $i++) {
    Start-Sleep 1
    try { $ok = (Invoke-WebRequest "http://localhost:$Port/api/health" -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200 } catch { }
}
if (-not $ok) { throw "The service did not answer on http://localhost:$Port/api/health. Look in Event Viewer > Windows Logs > Application." }
$ip = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254*' -and $_.PrefixOrigin -ne 'WellKnown' } | Select-Object -First 1).IPAddress
Write-Host ""
Write-Host "DONE. The server is running. On every PC, set the server to: http://$($ip):$Port" -ForegroundColor Green
if (Test-Path $keyFile) { Write-Host "Now delete $keyFile (it is a secret and is no longer needed)." -ForegroundColor Yellow }
