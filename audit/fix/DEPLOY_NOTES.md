# Deploy notes: run the live server in Production with a new signing key

For the owner. Nothing here has been done by the agents. Do it once, after the fix branches are merged.

**What changed (H-4, L-5, Swagger).**
- `appsettings*.json` in git no longer hold a JWT signing key, and `appsettings.json` no longer holds an admin password.
- The server reads the key from `Jwt:SigningKey`: the environment variable `Jwt__SigningKey` (two underscores), or user-secrets.
- In any mode other than Development, the server refuses to start when the key is missing, shorter than 32 bytes, or one of the two old sample keys that were in git.
- In Development with no key, the server makes a random key at each start and logs a warning. Everyone is then signed out each time the server restarts. That is why the live server must run in **Production**.
- Swagger (`/swagger`) is now only on in Development. To keep it in Production, set `Swagger__Enabled=true`.

**Everyone is signed out once.** Changing the key makes every token issued with the old key invalid. Each PC shows the sign-in screen at its next request; users sign in again. Their passwords do not change.

## 1. Back up the database first

The live database today is SQLite: `C:\Users\PC\source\ontech-store\src\OnTech.Store.Api\ontech-store-dev.db`.

1. Stop the server (see step 4 for how it runs today).
2. Copy the file while the server is stopped:
   ```powershell
   $db = 'C:\Users\PC\source\ontech-store\src\OnTech.Store.Api\ontech-store-dev.db'
   New-Item -ItemType Directory -Force C:\OnTechStore\Backups | Out-Null
   Copy-Item $db ("C:\OnTechStore\Backups\ontech-store-dev_{0:yyyyMMdd_HHmm}.db" -f (Get-Date))
   ```
   If you already moved to SQL Server, run `powershell -ExecutionPolicy Bypass -File scripts\backup-database.ps1` instead.

## 2. Make a new random key

In PowerShell:
```powershell
$bytes = New-Object byte[] 48
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$key = [Convert]::ToBase64String($bytes)   # 64 characters
```
Keep `$key` secret. Do not put it in any file inside the repository.

## 3. Store the key and the settings as machine environment variables

From an **elevated** PowerShell (Run as administrator), in the same window as step 2:
```powershell
[Environment]::SetEnvironmentVariable('Jwt__SigningKey', $key, 'Machine')
[Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Production', 'Machine')

# Database. Pick ONE of the two blocks.

# a) Stay on SQLite (today's live data). Use the full path: in Production the relative path from
#    appsettings.Development.json is not used any more.
[Environment]::SetEnvironmentVariable('Database__Provider', 'Sqlite', 'Machine')
[Environment]::SetEnvironmentVariable('ConnectionStrings__Store', 'Data Source=C:\Users\PC\source\ontech-store\src\OnTech.Store.Api\ontech-store-dev.db', 'Machine')

# b) SQL Server (appsettings.json already defaults to it; set these only to be explicit or to change the server/database).
# [Environment]::SetEnvironmentVariable('Database__Provider', 'SqlServer', 'Machine')
# [Environment]::SetEnvironmentVariable('ConnectionStrings__Store', 'Server=.\SQLEXPRESS;Database=OnTechStore;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False', 'Machine')
```

Only when the database has **no admin yet** (a brand-new SQL Server database), also set a first admin password. The server refuses to start without it in that case. The admin is asked to change it at first sign-in. Remove it again once the admin exists:
```powershell
[Environment]::SetEnvironmentVariable('Seed__AdminPassword', '<a temporary password>', 'Machine')
# after the first start:
[Environment]::SetEnvironmentVariable('Seed__AdminPassword', $null, 'Machine')
```

Machine variables reach new processes only. Close and reopen PowerShell (and any terminal that starts the server) after setting them. A Windows service picks them up at its next start.

Note: machine-wide `ASPNETCORE_ENVIRONMENT=Production` also applies to `dotnet run` of the API from Visual Studio or a terminal on this PC. A developer on this PC who needs Development uses the `http` launch profile (which sets Development) and `dotnet user-secrets set Jwt:SigningKey <secret> --project src/OnTech.Store.Api`.

## 4. Stop and start the server

**As it runs today (hidden `dotnet run`).**
1. Stop it: end the `dotnet` / `OnTech.Store.Api` process that listens on port 5080 (Task Manager, or `Get-NetTCPConnection -LocalPort 5080 -State Listen | % { Stop-Process -Id $_.OwningProcess }`).
2. Start it in Production. `--no-launch-profile` stops `dotnet run` from applying the Development profile in `launchSettings.json`:
   ```powershell
   cd C:\Users\PC\source\ontech-store
   dotnet run --project src/OnTech.Store.Api -c Release --no-launch-profile --urls http://0.0.0.0:5080 *> src\OnTech.Store.Api\server.log
   ```
   (Start it hidden the same way as before; only `--no-launch-profile --urls ...` is new.)

**Or as a Windows service** (recommended, survives reboots). From an elevated PowerShell in the repository root:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\install-service.ps1
```
The script publishes to `C:\OnTechStore\Server`, sets `Jwt__SigningKey` (a new random key, unless one is already set on the machine) and `ASPNETCORE_ENVIRONMENT=Production` on the service only, and starts it. To use the key from step 3 instead, run it after step 3; it uses an existing machine key, and on a re-run it keeps the service's key. For a brand-new database with no admin, add `-FirstAdminPassword '<temporary password>'` once, then re-run without it after the first sign-in. The service's variables live in `HKLM:\SYSTEM\CurrentControlSet\Services\OnTechStoreServer\Environment`. Stop/start later with `Stop-Service OnTechStoreServer` / `Start-Service OnTechStoreServer`. Do not run the service and the hidden `dotnet run` at the same time: both use port 5080.

## 5. Check

```powershell
Invoke-WebRequest http://localhost:5080/api/health -UseBasicParsing   # 200, {"status":"ok",...}
```
Then sign in from one PC. If the server does not start, the first line of the error says which setting is missing (`Jwt__SigningKey`, `Seed__AdminPassword`, or the connection string).

## Roll back

Stop the server, copy the backup from step 1 back over the `.db` file, remove the machine variables (`[Environment]::SetEnvironmentVariable('<name>', $null, 'Machine')`) and start the old way. Before rolling back, check the error message: it names the setting that is missing, which is usually the quicker fix.
