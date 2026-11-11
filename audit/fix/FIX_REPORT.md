# Audit fix report

Branch `fix/audit-main`, on top of `main` @ `d8e7ba7` (tag `pre-fix`). Work split in three branches (A: API config and auth, B: API data and migrations, C: WPF client), merged B → A → C with no conflicts, then verified (Phase 3, 2026-11-11). Per-issue detail, commits and regression tests: `HANDOFF.md`. Owner deploy steps: `DEPLOY_NOTES.md`.

## What was fixed

| Issue | Fix | Branch |
|---|---|---|
| H-1 | Server crashed at startup on SQL Server (`en-us is an invalid culture identifier`): `InvariantGlobalization` off | A |
| H-2, L-3 | Read endpoints use `ReadAccess` (any of several permissions); app menu shows only pages the user can open; no-permission users get 403 on customers/suppliers | B, C |
| H-3 | Arabic-Indic digits accepted in every number field (`Client/Numbers.cs`) | C |
| H-4 | JWT signing key out of git; `Jwt__SigningKey` required outside Development; old sample keys refused | A |
| M-1 | `Version` concurrency token on Customers/Suppliers/Items/Users; 409 `ConcurrencyConflict`; app reloads the record into the open form | B, C |
| M-2 | `MustChangePassword` enforced on the server; app opens the change-password window | A, C |
| M-3 | `MaxLength` on every text box = DTO limit; per-field validation text in English and Arabic | C |
| M-4 | Document numbers trimmed and upper-cased; voucher uniqueness ignores case on both DBs | B |
| M-5 | Desktop log file, 14 days | C |
| M-6, M-7 | EF Core / ASP.NET packages 8.0.33; SQLitePCLRaw 2.1.14 (advisory fixed) | A |
| M-8 | Numbering gate + 409 `Duplicate` instead of 500 on parallel number changes | B |
| M-9 | AutomationIds on every control | C |
| L-1 | Paging and offset limits → 400, not 500 | B |
| L-2 | Login lockout (5 wrong → 30 s, doubling to 300 s), 429 `TooManyAttempts` | A |
| L-5 | No default admin password in git: `Seed__AdminPassword` only when the DB has no admin | A |
| L-6 | FKs from `CreatedByUserId` to Users (SQL Server and new SQLite files; existing SQLite gets the index only) | B |
| L-8 | Stale list answers ignored in Home, Catalog, Orders | C |
| L-10 | Last build warning (CS1573) | B |
| Swagger | Only in Development | A |
| Login 409 | Sign-in writes `LastLoginAt` without the `Version` check (`ExecuteUpdate`), so an admin saving the same user at that moment no longer makes the login fail with 409. Test `Fix_Lead_LoginVersionTests` (failed 3/3 before, passes 5/5) | lead |
| Phase 3 | Stock movements and item card ordered by `Id`, not `CreatedAt` (fixes `Mixed_orders_restocks_and_cancels…`, flaky ~20% on SQLite) | lead |

## Not changed (owner decisions)

- **L-7**: an item keeps its code if it moves category. The owner said on 2026-11-08 that items never move between categories.
- **L-9**: the customer list is not paged; the data is small (≈500 customers) and New Order needs them all. The two L-9 audit tests still fail on purpose.

## Results (Phase 3, 2026-11-11, `fix/audit-main`)

| Suite | Before (`baseline.md`) | After |
|---|---|---|
| Build `OnTech.Store.sln` Debug and Release (in the worktree) | 1 warning | 0 errors, 0 warnings |
| `tests/OnTech.Store.Api.Tests`, SQLite | 87/87 | **166/166** (flaky test 10/10 in a row) |
| `tests/OnTech.Store.Api.Tests`, SQL Server | 87/87 | **166/166** |
| `tests/OnTech.Store.Desktop.Tests` | — | **150/150** |
| `audit/OnTech.Store.Tests` (5081 SQL Server Production, 5082 SQLite Development) | 63/85 | **84/85**: only L-9 `Customers_page_does_not_load_every_customer_at_once` fails (won't fix). `BindingTraceTests` run alone: 1/1 |
| `audit/OnTech.Store.UITests` (FlaUI) | 9 pass / 4 fail / 1 skip | **13 pass / 0 fail / 1 skip** (50k timing, skipped by the owner). One test (`New_order_invalid_inputs…`) hit a UIA COM error in the full run and passed on re-run |

Also checked: Release build run on port 5084 in Production on a copy of the SQLite file: `/api/health` 200, `/swagger` 404, admin login 200, `/api/items` without a token 401; stopped and the copy deleted. `dotnet list package --vulnerable --include-transitive`: no vulnerable packages in the shipped projects; the API test project pulls System.Net.Http 4.3.0 and System.Text.RegularExpressions 4.3.0 (High, test-only, transitive from old test packages, not shipped).

Raw output: `results/final_audit.txt`, `results/final_ui.txt`, `results/merged_*.txt`.

## Test changes made in Phase 3

Audit tests that encoded the old API shape (listed in `baseline.md`):
- Arabic-digit test calls `Numbers.TryParseInt` (the app's helper) instead of `int.TryParse`.
- Concurrency test sends the loaded `Version`, as the app does.
- `Swagger_is_reachable_without_signing_in` expects 404 on 5081 (Production), 200 on 5082 (Development).
- `Live_sqlite_copy_has_every_model_column` reads the 5082 copy of the live file after the new `UpgradeSqliteAsync` ran (the raw pre-fix backup has no `Version` columns until the server upgrades it).
- The audit test assembly runs one class at a time: the SQL Server trigger test (`TR_Audit_FailMovement`) made any stock-in saved by another class at the same moment fail.
- `Microsoft.Data.SqlClient` 5.1.6 → 5.1.9 in both audit test projects (EF Core SqlServer 8.0.33 needs ≥ 5.1.9).

UI test harness (no app change):
- The item card is an owned window, a child of the main window in UI Automation: `WaitWindow` looks one level down.
- Wait for typed text to arrive before reading the box length.
- The customer form checks the email before the number: the test checks the number with no email, then the email.
- Double-click save: the first click saves and clears the form, a later click shows "Choose an item": the test checks the order count (exactly 1), not the banner.
- New Order invalid input: ٥ is now accepted (H-3), so the test fills the voucher before asking for 99,999.

Test DB prep (test copies only): `MustChangePassword = 0` for `admin` on SQL Server `OnTechStore` (restored from `OnTechStore_before_audit.bak` and migrated to `AuditFixes` by the 5081 server) and on the 5082 SQLite copy.

## Left for the owner

- Review and merge `fix/audit-main` into `main` (PR).
- Then redeploy the live server in Production with a new key: `DEPLOY_NOTES.md`. Everyone signs in again once.
- Change-password still checks `Version` (it is an edit): if an admin edits that user at the same instant, it returns 409 and the user tries again.
