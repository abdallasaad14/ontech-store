<#
.SYNOPSIS
  Publishes the OnTech Store server and installs it as a Windows service on the server PC.
.DESCRIPTION
  Run from an elevated PowerShell in the repository root:
    powershell -ExecutionPolicy Bypass -File scripts\install-service.ps1
  Re-running it upgrades the service in place (appsettings.json in the install folder is kept).
#>
param(
    [string]$InstallDir = "C:\OnTechStore\Server",
    [string]$ServiceName = "OnTechStoreServer",
    [int]$Port = 5080
)
$ErrorActionPreference = "Stop"

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -eq "Running") { Stop-Service $ServiceName; Start-Sleep 2 }

$keepSettings = Test-Path "$InstallDir\appsettings.json"
if ($keepSettings) { Copy-Item "$InstallDir\appsettings.json" "$env:TEMP\ontech-appsettings.json" -Force }

dotnet publish src\OnTech.Store.Api -c Release -r win-x64 --self-contained false -o $InstallDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

if ($keepSettings) {
    Copy-Item "$env:TEMP\ontech-appsettings.json" "$InstallDir\appsettings.json" -Force
} else {
    # First install: generate a unique signing key so tokens can't be forged with the sample key.
    $bytes = New-Object byte[] 48
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $key = [Convert]::ToBase64String($bytes)
    $json = Get-Content "$InstallDir\appsettings.json" -Raw | ConvertFrom-Json
    $json.Jwt.SigningKey = $key
    $json.Urls = "http://0.0.0.0:$Port"
    $json | ConvertTo-Json -Depth 10 | Set-Content "$InstallDir\appsettings.json" -Encoding UTF8
}

# The service runs as LocalSystem and signs in to SQL Server with Windows authentication.
# Give that account the right to create the database (it then owns it). Skipped if sqlcmd isn't installed.
if (Get-Command sqlcmd -ErrorAction SilentlyContinue) {
    $sql = "IF SUSER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE LOGIN [NT AUTHORITY\SYSTEM] FROM WINDOWS; ALTER SERVER ROLE [dbcreator] ADD MEMBER [NT AUTHORITY\SYSTEM];"
    sqlcmd -S ".\SQLEXPRESS" -E -b -Q $sql
}

if (-not $existing) {
    New-Service -Name $ServiceName -DisplayName "OnTech Store Server" `
        -Description "Stock, customers and orders API for the OnTech store desktop app." `
        -BinaryPathName "`"$InstallDir\OnTech.Store.Api.exe`"" -StartupType Automatic | Out-Null
    # Restart automatically if it ever crashes.
    sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
}

if (-not (Get-NetFirewallRule -DisplayName "OnTech Store Server" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "OnTech Store Server" -Direction Inbound -Protocol TCP -LocalPort $Port -Profile Private,Domain -Action Allow | Out-Null
}

Start-Service $ServiceName
Write-Host "Installed. Check http://localhost:$Port/api/health and http://localhost:$Port/swagger"
