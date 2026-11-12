<#
.SYNOPSIS
  Builds the "move to a new server" package on the CURRENT server PC (run from the repository root).
.DESCRIPTION
  powershell -ExecutionPolicy Bypass -File tools\move-package\prepare-package.ps1 [-Package C:\OnTechStore-Migration] [-IncludeSigningKey]

  The package gets:
    server\   the server, self-contained (the new PC needs no .NET install), set up for SQL Server
    tool\     OnTechStoreMoveToSqlServer.exe: copies the SQLite data into SQL Server, checks counts, takes backups
    install-on-new-server.ps1, README-AR.md
    secrets\jwt-key.txt   only with -IncludeSigningKey: the live server's sign-in key, so nobody has to sign in again after the move.
                          It is a secret: never put it in git, delete it once the new server is installed.
    data\     empty. On the move day, copy the live ontech-store-dev.db here (README-AR.md, step 2).
  Nothing here touches the live server or its database.
#>
param([string]$Package = "C:\OnTechStore-Migration", [switch]$IncludeSigningKey)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$repo = Resolve-Path (Join-Path $here "..\..")

New-Item -ItemType Directory -Force "$Package\data", "$Package\secrets" | Out-Null
dotnet publish "$repo\src\OnTech.Store.Api" -c Release -r win-x64 --self-contained true -o "$Package\server"
if ($LASTEXITCODE -ne 0) { throw "publishing the server failed" }
dotnet publish "$repo\tools\OnTech.Store.MoveToSqlServer" -c Release -r win-x64 --self-contained true -o "$Package\tool"
if ($LASTEXITCODE -ne 0) { throw "publishing the move tool failed" }
Copy-Item "$here\install-on-new-server.ps1", "$here\README-AR.md" $Package -Force

# The live server reads its key from this PC's environment (C:\OnTechStore\start-server.ps1 uses the user variable).
$key = [Environment]::GetEnvironmentVariable('Jwt__SigningKey', 'User')
if (-not $key) { $key = [Environment]::GetEnvironmentVariable('Jwt__SigningKey', 'Machine') }
if ($key -and $IncludeSigningKey) {
    Set-Content "$Package\secrets\jwt-key.txt" $key -NoNewline -Encoding ASCII
    Write-Host "Copied the live sign-in key to secrets\jwt-key.txt (keep the package private)."
} elseif ($key) {
    Write-Host "The live sign-in key was NOT copied. Run again with -IncludeSigningKey to keep everyone signed in after the move."
} else {
    Write-Host "No Jwt__SigningKey on this PC: the new server makes a new key and everyone signs in once."
}
Write-Host "Package ready: $Package"
