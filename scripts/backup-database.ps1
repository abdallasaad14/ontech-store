<#
.SYNOPSIS
  Takes a full backup of the OnTechStore database. Schedule it daily with Task Scheduler.
  SQL Server Express has no SQL Agent, so this script is the backup job.
#>
param(
    [string]$Server = ".\SQLEXPRESS",
    [string]$BackupDir = "C:\OnTechStore\Backups",
    [int]$KeepDays = 30
)
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
$file = Join-Path $BackupDir ("OnTechStore_{0:yyyyMMdd_HHmm}.bak" -f (Get-Date))
sqlcmd -S $Server -E -b -Q "BACKUP DATABASE [OnTechStore] TO DISK = N'$file' WITH INIT, COMPRESSION, CHECKSUM"
if ($LASTEXITCODE -ne 0) {
    # COMPRESSION isn't available on every Express edition; retry without it.
    sqlcmd -S $Server -E -b -Q "BACKUP DATABASE [OnTechStore] TO DISK = N'$file' WITH INIT, CHECKSUM"
    if ($LASTEXITCODE -ne 0) { throw "Backup failed" }
}
Get-ChildItem $BackupDir -Filter "OnTechStore_*.bak" | Where-Object LastWriteTime -lt (Get-Date).AddDays(-$KeepDays) | Remove-Item
Write-Host "Backup written to $file"
