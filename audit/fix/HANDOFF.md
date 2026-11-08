# Handoff between agents

Each agent writes ONLY in its own section: requests for files it does not own, decisions the others must know, and its final summary.
Format for a request: `- [ ] <file> — <what to change> — <why / issue id>`.

## Agent A (API config, auth) — branch `fix/a-api-config`

### Requests

### Notes for B / C

### Summary

## Agent B (API controllers, data, migrations) — branch `fix/b-api-data`

### Requests

- [ ] `src/OnTech.Store.Api/Program.cs` — OPTIONAL: `builder.Services.AddAnyPermissionPolicies();` right after `AddStoreAuth(...)` — H-2. `[HasAnyPermission]` works without it (it passes its requirement through .NET 8 `IAuthorizationRequirementData`, and the requirement handles itself). The line only makes named `[Authorize(Policy = "any:<int>")]` policies resolvable; nothing uses that today.
- [ ] `docs/STATUS.md` (lead) — add migration `AuditFixes` to the list; Customers/Suppliers/Items/Users carry `Version`; read policy per `ReadAccess`; doc numbers stored upper-case.
- [ ] `src/OnTech.Store.Api/Controllers/AuthController.cs` (A) — note only: `User.Version` is a concurrency token, so login (`LastLoginAt`) and change-password `SaveChanges` include `WHERE Version = @v`. If an admin edits that user in the same instant, that save throws `DbUpdateConcurrencyException`; the global handler now turns it into 409 ConcurrencyConflict, not 500. If you want change-password to count as an edit, add `user.Version++` there. Contract §4 does not require it.
- Edited existing tests (approved by lead): `FeatureCoverageTests.GuardedEndpoints` rows `GET /api/items` and `GET /api/categories` changed from `Permission.ViewStock` to `ReadAccess.Catalog` (H-2 commit). `StoreApiTests.Issue_voucher_number_is_required_unique_and_searchable` now expects `voucher.ToUpperInvariant()` (M-4 commit). No customers/suppliers GET rows existed.
- Tooling: `dotnet ef database update` cannot connect to SQL Server from this project (the API's runtime config is globalization-invariant: "en-us is an invalid culture identifier"). Use `dotnet ef migrations script` + `sqlcmd -I` (needs `-I` for the filtered indexes), or let the app's `MigrateAsync` run. `install-service.ps1` / prod use `MigrateAsync`, so this does not matter at runtime.

### Final DTO / error changes for C

- `Version` (int) is now filled in `CustomerDto`, `SupplierDto`, `ItemDto`, `UserDto`. Send it back in `SaveCustomerRequest.Version`, `SaveSupplierRequest.Version`, `UpdateItemRequest.Version`, `UpdateUserRequest.Version`. Null = no check. Disable (DELETE) and user reset-password also bump it, so reload after those.
- New `code` values on 409s:
  - `ConcurrencyConflict`, title exactly `This record was changed on another device. It has been reloaded; check it and save again.` (**new title, needs Arabic**).
  - `Duplicate` on every "already used / already exists / already taken" 409: customer/supplier number, name, tax number; category code and name; item code and name; username; issue voucher; receipt; stock-in voucher. Titles unchanged.
- **New English titles for `Arabic.cs`:**
  1. `This record was changed on another device. It has been reloaded; check it and save again.` (exact entry)
  2. `This number or code is already used. Reload and try again.` (exact entry; global safety net for unique-index violations no controller caught)
- Document numbers come back trimmed and upper-case (`ab-1` is saved and returned as `AB-1`): `IssueVoucherNumber`, `OrderNumber`, `ReceiptNumber`, `StockIn.VoucherNumber`, `SupplierInvoiceNumber`. Server messages quote the upper-case value, e.g. `Issue voucher number 'AB-1' is already used by another order.` (existing Patterns still match).
- Read access (H-2/L-3): `GET api/categories[/{id}]`, `api/items[/{id}]` need any of `ReadAccess.Catalog`; `api/customers[/{id}]` any of `ReadAccess.Customers`; `api/suppliers[/{id}]` any of `ReadAccess.Suppliers`; dashboard, item card and stock-movements stay ViewStock. `Permission.None` gets 403 on customers/suppliers.
- Paging (L-1): new `OnTech.Store.Contracts.Paging` (`MaxPage = 1_000_000`, `MaxPageSize = 200`, `Skip(page, size)`). `OrderQuery`, `StockInQuery`, `StockMovementQuery`: `Page` 1..1,000,000, `PageSize` 1..200, otherwise 400. `GET api/dashboard/summary?utcOffsetMinutes=` is -840..840, otherwise 400 (the client sends the PC offset, always inside that range).
- No new query parameters.

### Decisions for the owner

- **L-7 (item moved to another category keeps its code): not changed.** The code is a snapshot on every past order line and stock-in line (`OrderLines.ItemCode`, `StockInLines.ItemCode`). It is printed on the item card and receipts, and searches match it from its start (`01…`). Re-coding on a category move would make the same item show under two codes: the old code on paper vouchers and history lines, the new code on screen. `CatalogCodes` documents "an item keeps its code" as the rule. Recommendation: keep it, and have the Items page show a hint when the category changes ("the code stays 01007; type a new code to change it"). The code box already lets the user type a new code (it is checked for uniqueness). If the owner wants automatic re-coding, it is a few lines in `ItemsController.Update`, plus a decision on whether history keeps the old code.
- M-4 existing data: **0** values collide when upper-cased, and **0** need upper-casing, on the live copy (`Orders` 1,689 rows: IssueVoucherNumber / OrderNumber / ReceiptNumber; `StockIns` 339 rows: VoucherNumber / SupplierInvoiceNumber) and on SQL Server `OnTechStore` (read-only check). The upgrade step and the migration still upper-case any lower-case rows they find. If a SQLite file ever has values that differ only in case, that index is left case-sensitive and a warning is logged ("…values that differ only in letter case…"). The data is never changed in that case.
- L-6 orphans: **0** `Customers`/`Suppliers` rows whose `CreatedByUserId` is not a user, on the live copy and on SQL Server `OnTechStore`. So the FK is added on SQL Server (migration) and in new SQLite files. **Existing SQLite files (the live DB) do not get the FK**: SQLite can't add an FK to a table without rebuilding it, and live tables are not rebuilt. They get only the `IX_*_CreatedByUserId` index. The API always writes the signed-in user's id, so nothing breaks. The FK arrives when the owner moves to SQL Server or a new SQLite file.

### Migration status (`20261108114808_AuditFixes`, dotnet-ef 8.0.29, snapshot updated)

Contents: `Version int NOT NULL DEFAULT 0` on Users/Items/Customers/Suppliers; `IX_Customers_CreatedByUserId`, `IX_Suppliers_CreatedByUserId` + FKs to Users (RESTRICT); upper-casing of existing doc numbers (binary compare). No collation changes: the NOCASE column collation is SQLite-only model config (`if (Database.IsSqlite())`), so the SQL Server model and snapshot don't see it.

| Check | Result |
|---|---|
| (a) fresh SQL Server `OnTechStore_B_Fresh`, full script 0 → AuditFixes | OK: last migration AuditFixes, 4 Version columns, 2 FKs, collation still `SQL_Latin1_General_CP1_CI_AS` |
| (b) `OnTechStore_B_Upgrade`: script 0 → SuppliersAndStockIn, rows inserted (user, customer, supplier, order `iv-1`/`rc-1`, stock-in `in-1`/`inv-1`), then SuppliersAndStockIn → AuditFixes | OK: data kept, `IV-1/IV-1/RC-1`, `IN-1/INV-1`, Customer.Version 0, 2 FKs |
| (c) fresh SQLite through EnsureCreated | OK (test suite): the 3 unique voucher indexes are NOCASE, FKs present (`Fix_B_DocNumberTests`, `Fix_B_ForeignKeyTests`) |
| (d) copy of the live backup (`audit\fix\scratch\live-upgrade.db`), built API in Development on 5083 | OK: 1st start backed up to `live-upgrade-backup-before-audit-fixes-20261108-135155.db` and ran 17 steps; 2nd start ran no steps and wrote no backup; `GET /api/health` 200; Version on 4 tables, 3 NOCASE indexes, 2 CreatedByUserId indexes, 1,689 orders / 339 stock-ins kept, `integrity_check` ok. API stopped. |
| Clean-up | `OnTechStore_B_Fresh` and `OnTechStore_B_Upgrade` dropped; no `OnTechStore_B_*` / `OnTechStoreTest_*` DBs left; nothing listening on 5083. |

Note: the first 5083 attempt didn't bind, because `appsettings.json` sets `Urls` to `0.0.0.0:5080` and 5080 is in use. It exited without touching 5080; the retry used env `Urls=http://localhost:5083`. Use `Urls`, not `ASPNETCORE_URLS`, for side servers.

### Summary

Build `dotnet build OnTech.Store.sln -c Debug`: **0 errors, 0 warnings**. Tests `tests/OnTech.Store.Api.Tests`: **131/131 on SQLite, 131/131 on SQL Server** (87 existing + 44 new Fix_B).

| Issue | Commit | Regression test | SQLite | SQL Server |
|---|---|---|---|---|
| H-2 + L-3 read policy | `633b0de` | `Fix_B_ReadPolicyTests` (failed 11/14 before) | pass | pass |
| M-1 concurrency | `aa2bcf4` | `Fix_B_ConcurrencyTests` (stale edit, disable/reset bump, stock movement no bump, 6-PC race) | pass | pass |
| M-8 numbering gate + 409 Duplicate | `df21843` | `Fix_B_DuplicateTests` (failed 4/5 before; parallel number changes, codes) | pass | pass |
| M-4 doc numbers | `92be4b5` | `Fix_B_DocNumberTests`, `Fix_B_SqliteUpgradeTests` (pre-fix file upgrade, collision left alone) | pass | pass (SQLite-only tests no-op) |
| L-1 paging | `fc60f84` | `Fix_B_PagingTests` (page=2147483647, 1,000,001, pageSize 201, offset ±841) | pass | pass |
| L-10 doc tag | `aa6cc90` | `Fix_B_DocCommentTests` + 0-warning build | pass | pass |
| L-6 FKs | `702911f` | `Fix_B_ForeignKeyTests` | pass (new files) | pass |
| Migration AuditFixes | `bbe9ebb` | whole suite on SQL Server + checks (a)–(d) above | — | pass |
| L-7 | not changed | — | see Decisions for the owner | |

Commits between the M-1 commit and `bbe9ebb` have model changes but no migration yet, so they pass on SQLite only. Test the branch tip on SQL Server.

## Agent C (WPF client) — branch `fix/c-wpf`

### Requests

### Summary
