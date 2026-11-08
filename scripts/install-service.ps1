<#
.SYNOPSIS
  Publishes the OnTech Store server and installs it as a Windows service on the server PC.
.DESCRIPTION
  Run from an elevated PowerShell in the repository root:
    powershell -ExecutionPolicy Bypass -File scripts\install-service.ps1
  Re-running it upgrades the service in place (appsettings.json in the install folder and the service's key are kept).

  The service runs in Production. Its secrets are not in any appsettings file; they are environment variables of the service
  (registry: HKLM\SYSTEM\CurrentControlSet\Services\<ServiceName>\Environment):
    ASPNETCORE_ENVIRONMENT = Production
    Jwt__SigningKey        = the machine variable Jwt__SigningKey if one is set, else the service's existing key, else a new random key
    Seed__AdminPassword    = only when -FirstAdminPassword is given (a new database with no admin needs it once)
  See audit/fix/DEPLOY_NOTES.md.
#>
param(
    [string]$InstallDir = "C:\OnTechStore\Server",
    [string]$ServiceName = "OnTechStoreServer",
    [int]$Port = 5080,
    # Temporary password for the first admin. Only needed when the database has no admin yet; change it at first sign-in.
    [string]$FirstAdminPassword = ""
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
    $json = Get-Content "$InstallDir\appsettings.json" -Raw | ConvertFrom-Json
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

# Service environment: Production mode and the signing key (never written to appsettings.json).
$regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$envList = @((Get-ItemProperty -Path $regPath -Name Environment -ErrorAction SilentlyContinue).Environment) | Where-Object { $_ }
$current = @{}
foreach ($line in $envList) { $i = $line.IndexOf('='); if ($i -gt 0) { $current[$line.Substring(0, $i)] = $line.Substring($i + 1) } }

$key = [Environment]::GetEnvironmentVariable('Jwt__SigningKey', 'Machine')
if (-not $key) { $key = $current['Jwt__SigningKey'] }
if (-not $key) {
    $bytes = New-Object byte[] 48
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $key = [Convert]::ToBase64String($bytes)
    Write-Host "Generated a new signing key for the service. Users sign in again once."
}
$current['Jwt__SigningKey'] = $key
$current['ASPNETCORE_ENVIRONMENT'] = 'Production'
if ($FirstAdminPassword) { $current['Seed__AdminPassword'] = $FirstAdminPassword } else { $current.Remove('Seed__AdminPassword') }
$lines = [string[]]($current.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" })
New-ItemProperty -Path $regPath -Name Environment -PropertyType MultiString -Value $lines -Force | Out-Null

if (-not (Get-NetFirewallRule -DisplayName "OnTech Store Server" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "OnTech Store Server" -Direction Inbound -Protocol TCP -LocalPort $Port -Profile Private,Domain -Action Allow | Out-Null
}

Start-Service $ServiceName
Write-Host "Installed. Check http://localhost:$Port/api/health (Swagger is off in Production; set Swagger__Enabled=true to turn it on)."
if ($FirstAdminPassword) { Write-Host "After the first admin signs in, re-run this script without -FirstAdminPassword to remove it from the service." }
