# Audit progress

Started 2026-11-08 on DESKTOP-VIF0NUS. Repo `C:\Users\PC\source\ontech-store` @ `d8e7ba7` (main).

## Placeholders resolved

| Prompt placeholder | Value found |
|---|---|
| .NET version | .NET 8 (`net8.0` API/Client/Contracts, `net8.0-windows` WPF). Runtimes on PC: 8.0.15. SDKs: 9.0.203, 10.0.300 (no 8.x SDK, no global.json) |
| Pattern | MVVM (CommunityToolkit.Mvvm 8.2.2), thin code-behind |
| SQL instance / DB | Production config: `.\SQLEXPRESS` / `OnTechStore`. **Live install actually runs SQLite** `src\OnTech.Store.Api\ontech-store-dev.db` (Development env) |
| Data access | EF Core 8.0.11 (SqlServer + Sqlite providers), LINQ + `ExecuteUpdate`; no stored procedures, no raw SQL except SQLite upgrade DDL |
| Backend | `OnTech.Store.Api`, ASP.NET Core 8, `http://<server>:5080`, JWT bearer. WPF never touches the DB |
| Connection string | `Server=.\SQLEXPRESS;Database=OnTechStore;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False` (prod) / `Data Source=ontech-store-dev.db` (dev) |

## Test environments used

| Port | What | DB |
|---|---|---|
| 5080 | The owner's live server (left untouched by the audit) | SQLite `ontech-store-dev.db` |
| 5081 | Audit copy of the Debug build, Production config | SQL Server `.\SQLEXPRESS\OnTechStore` (created by the app's migrations during Phase 0) |
| 5082 | Audit copy, Development config | SQLite copy under `audit\build\api-sqlite\` |

## Phase 0: Safety backup ✅

- SQLite (live data, 318 items / 505 customers / 1689 orders / 4070 movements), online backup with integrity check `ok`:
  - `audit\backups\ontech-store-dev_before_audit.db`
  - `D:\Backups\ontech-store-dev_before_audit_20261108.db` (same SHA-256 `41BE4F42…0640`)
- SQL Server: `OnTechStore` did **not exist** on `.\SQLEXPRESS` (or the default instance). Created by the app's own migrations (7 applied), then
  `BACKUP DATABASE [OnTechStore] TO DISK = 'C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\OnTechStore_before_audit.bak' WITH INIT, COPY_ONLY, CHECKSUM` + `RESTORE VERIFYONLY` → valid.
- Restore script: `audit\restore.sql` (SQL Server part + PowerShell lines for the SQLite file).

## Checklist

- [x] Phase 0 backup + restore script
- [x] Phase 1 discovery → `01_architecture_map.md`
- [x] Phase 2 build & static analysis (0 errors, 1 warning; vulnerable SQLitePCLRaw)
- [x] Phase 3 DB connection, schema, queries, transactions, concurrency
- [x] Phase 4 API integration
- [x] Phase 5 WPF static review + binding trace (0 errors)
- [x] Phase 6 integration + ViewModel + FlaUI tests, logs (50k UI test skipped at owner request: local use, small data)
- [x] `AUDIT_REPORT.md`
- SQL Server DB restored from the Phase 0 backup at 10:32 UTC before the final UI run.

