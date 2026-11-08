# Handoff between agents

Each agent writes ONLY in its own section: requests for files it does not own, decisions the others must know, and its final summary.
Format for a request: `- [ ] <file> — <what to change> — <why / issue id>`.

## Agent A (API config, auth) — branch `fix/a-api-config`

### Requests

- [ ] `src/OnTech.Store.Api/Controllers/DashboardController.cs` (B): add `/// <param name="ct">Cancels the request.</param>` to `Summary` (line 18). This is the only build warning left (CS1573). — L-10
- [ ] `src/OnTech.Store.Contracts/ApiContract.cs` (B): add `public const string TooManyAttempts = "TooManyAttempts";` to `ErrorCodes` (429, too many wrong passwords). The server uses the same literal in `Auth/LoginThrottle.Code`; once the constant exists, the lead can point `LoginThrottle.Code` at it. — L-2
- [ ] `tests/OnTech.Store.Api.Tests/*.csproj` and the other test projects (lead): align `Microsoft.AspNetCore.Mvc.Testing` 8.0.11 → **8.0.33** to match the API. — M-6
- [ ] `tools/OnTech.Store.DemoData` (lead): nothing to change. It got SQLitePCLRaw 2.1.6 through its `ProjectReference` to the API, so the API's new direct reference lifts it to 2.1.14 too; `dotnet list tools/OnTech.Store.DemoData package --vulnerable --include-transitive` reports no vulnerable packages. — M-7
- [ ] `tests/OnTech.Store.Api.Tests/StoreApiTests.cs` (lead, optional): `Standard_user_permissions_are_enforced_and_disabling_is_immediate` creates a user with the default `MustChangePassword = true` and uses it right away. It still passes because `StoreApiFactory.LoginAsync` now clears the flag of the user it signs in (documented on the method). If you prefer the test to say it explicitly, set `MustChangePassword = false` there. Same for `Dashboard_and_swagger_respond`: it passes because the factory sets `Swagger:Enabled=true`; to test the Testing default instead, expect 404 there. — M-2, Swagger
- [ ] `audit/OnTech.Store.Tests` and the 5081 DB (lead, Phase 3): the audit tests that sign in as admin on a test DB whose admin still has `MustChangePassword = 1` now get 403 `MustChangePassword`. Clear the flag on the test DBs. `Swagger_is_reachable_without_signing_in` must expect 404 outside Development. Audit servers started from these builds need `Jwt__SigningKey` (≥ 32 bytes, not a leaked key) unless they run in Development. — M-2, H-4, Swagger
- [ ] `docs/STATUS.md` (lead): the "Sign in" row says seed admin `admin` / `ChangeMe!123` from `appsettings.json`. Now: first admin password from `Seed__AdminPassword` (env or user-secrets), required only when the DB has no admin; server-side MustChangePassword; lockout after 5 wrong passwords; key from `Jwt__SigningKey`; Swagger only in Development. "How it runs on the owner's PC": see `audit/fix/DEPLOY_NOTES.md`. — H-4, L-5, M-2, L-2

### Notes for B / C

- **For C, Arabic needed in `Services/Arabic.cs`** (new server titles the user can see):
  - Exact: `You must change your password before you continue.` (403, code `MustChangePassword`). The client should react on the code: open the change-password window.
  - Pattern: `Too many wrong passwords. Wait {0} seconds and try again.` (429, code `TooManyAttempts`, `{0}` is a whole number of seconds, e.g. `Wait 30 seconds`). The response also has a `Retry-After: <seconds>` header. 429 is new to the client: `DefaultMessage` has no text for it, but the title is always set, so it shows.
  - Startup errors (missing `Jwt__SigningKey`, missing `Seed__AdminPassword`) are not user-facing; no Arabic needed.
- **For B:** a signed-in user's `MustChangePassword` flag is now enforced by `Auth/MustChangePassword.cs` (middleware after `UseAuthentication`). Endpoints that must stay open while flagged get `[AllowWhileMustChangePassword]`; only `GET api/auth/me` and `POST api/auth/change-password` have it. Nothing for B to do in controllers.
- **For B:** requests that match no endpoint now return 404 before authorization (they used to get the fallback policy's 401). Any route you add is an endpoint, so it is unaffected.
- **For B / C tests:** `StoreApiFactory` now has `EnvironmentName`, `Settings` (a null value removes a default setting) and `TestServices` (replace services, e.g. a fake `TimeProvider`). The test admin no longer has `MustChangePassword`. `LoginAsync` clears the flag of any user it signs in; to test the flag, sign in by hand with `CreateClient()` + `POST api/auth/login`. Login lockout is per factory (per process host): 5 wrong passwords for one username in one test class lock it for 30 s, so don't hammer `admin` with wrong passwords in a shared fixture.
- **Package versions chosen (M-6):** Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.EntityFrameworkCore.Design/.Sqlite/.SqlServer, Microsoft.Extensions.Identity.Core = **8.0.33**; Microsoft.Extensions.Hosting.WindowsServices = **8.0.1** (already the latest 8.0.x); SQLitePCLRaw.bundle_e_sqlite3 = **2.1.14** (GHSA-2m69-gcr7-jv3q covers ≤ 2.1.11; native SQLite now ≥ 3.50.2 instead of 3.41.2).
- **Owner redeploy:** `audit/fix/DEPLOY_NOTES.md`. After merge, the live server must not keep running as `dotnet run` in Development: without a key it makes a random one at each start (everyone signed out on every restart). The key change signs everyone out once.

### Summary

All on `fix/a-api-config`, based on `0865a55`. Build: `dotnet build OnTech.Store.sln -c Debug` 0 errors, 1 warning (the pre-existing CS1573, request above). `tests/OnTech.Store.Api.Tests`: **121/121 on SQLite, 121/121 on SQL Server** (87 old + 34 new `Fix_A_*`).

| Issue | Commit | Regression test | Result |
|---|---|---|---|
| H-1 InvariantGlobalization | `bb2fb06` | `Fix_A_GlobalizationTests` (reads the API's own runtimeconfig.json) | failed before, passes. Smoke: published API on 5084, Production, `.\SQLEXPRESS` DB `OnTechStore_A_Smoke`, `GET /api/health` → **200** (also re-run on the final build: 200, `/swagger` 404); process stopped, DB dropped |
| H-4 signing key out of git | `bbc9705`, `4422c30` (install-service.ps1, edited, not run) | `Fix_A_SigningKeyTests`: leaked keys → 401 (Development with no key, and Testing); Production without key / short key / leaked key does not start; no `SigningKey` in any appsettings | 6 of 9 failed before, all pass. Manual: Production without key exits with "Jwt:SigningKey is not set … Set the environment variable Jwt__SigningKey …". `audit/fix/DEPLOY_NOTES.md` written |
| M-2 MustChangePassword on the server | `9967f94` | `Fix_A_MustChangePasswordTests` (403 + code on GET/POST, me/change-password allowed, flag clears, admin reset flags again, wrong current password keeps it) | 4 failed before, pass |
| M-7 SQLitePCLRaw advisory | `3696367` | `Fix_A_SqliteVersionTests` (package ≥ 2.1.12, native SQLite ≥ 3.50.2) | failed before (3.41.2), passes. `dotnet list src/OnTech.Store.Api package --vulnerable --include-transitive`: no vulnerable packages |
| M-6 (partial) 8.0.x patch | `73f2b67` | `Fix_A_PackageVersionTests` (EF Core, Sqlite, SqlServer, JwtBearer, Identity.Core ≥ 8.0.33, major 8) | 5 failed before, pass |
| L-2 lockout | `1a04b20` | `Fix_A_LoginLockoutTests` (30 s after 5, same answer for right/wrong while locked, countdown, doubling 30→60→120→240→300→300, reset on success, per username incl. case/spaces, unknown usernames, 1000 parallel failures) | 4 HTTP tests failed before, pass |
| L-5 no default admin password | `2ee90ca`, `7142a3a` (factory drops a SQL DB left by a failed startup) | `Fix_A_SeedAdminTests` (no password → startup fails naming `Seed__AdminPassword`; seeded admin has MustChangePassword; no AdminPassword in appsettings or SeedOptions) | 2 failed before, pass |
| Swagger only in Development | `265d348` | `Fix_A_SwaggerTests` (Testing/Production → 404, Development → 200, Production + `Swagger:Enabled=true` → 200) | 2 failed before, pass |
| L-10 CS1573 | — | — | request to B above (not my file) |

Not done / notes: two throw-away DBs `OnTechStoreTest_d7ca0a03…` and `OnTechStoreTest_295fbeb7…` (created 13:53 and 13:57 on `.\SQLEXPRESS`) were left by my SQL Server runs before `7142a3a`; I did not drop them in case they belong to another agent's running tests. They are safe to drop.

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

- [ ] `docs/STATUS.md` — add the desktop changes below (menu rules, item card needs ViewStock, log folder, AutomationId naming, field validation messages, concurrency reload) — CLAUDE.md rule, lead owns the file.
- [ ] Server (B) — the client relies on CONTRACTS.md as written: `code` on ProblemDetails, `Version` on the 4 DTOs/requests, GET `api/customers/{id}`, `api/suppliers/{id}`, `api/users/{id}`, `api/items/{id}` (used to reload a record after a 409), and the ReadAccess policy on the GETs. Without B's ReadAccess change, users with only CreateOrders/AddStock/ManageCatalog still get 403 from the server on the catalog lists (the menu is right, the server is not yet).
- [ ] No sln / Contracts / API changes needed from C.
- Package bumps (#10): Desktop and Client reference no Microsoft.* packages (only CommunityToolkit.Mvvm 8.2.2), nothing to bump. Logging is hand-written (no Serilog), so no new packages.

### Summary

| Issue | Commit | Regression tests (tests/OnTech.Store.Desktop.Tests) |
|---|---|---|
| H-3 Arabic digits | `98b6748` | `NumbersTests` (٥, ۱۲, 1٢, spaces, RTL mark, invalid, decimals ٫ ٬, server address, NewOrder/StockIn quantity, customer/supplier number, low stock, import sheet) |
| M-1 Version + 409 reload | `e653a08` | `ConcurrencyTests` (ApiError.Code, customer/supplier/item/user send Version, reload into form, form stays open, Arabic message, no Version on create) |
| MustChangePassword | `1f4f840` | `MustChangePasswordTests` (page raises Session.PasswordChangeRequired, no 403 banner; plain 403 unchanged; gate opens once, continues/signs out) |
| H-2 menu by ReadAccess | `25d41d8` | `MenuPermissionTests` (each single permission: every menu page loads with no 403 against a fake server enforcing ReadAccess; expected menu per permission; admin; Home without ViewStock; item card needs ViewStock; edit buttons need Manage*) |
| L-8 stale lists | `b8bc5c8` | `StaleResponseTests` (Home, Catalog items, Catalog categories, Orders; late answers with and without cancellation) |
| M-3 MaxLength + validation text | `449eaf9` | `ValidationTests` (every TextBox/PasswordBox has MaxLength; limits equal the DTO [MaxLength]; DataAnnotations messages parsed per field; English and Arabic per-field text; email checked before sending; every label/template translated) |
| M-5 log file | `0398157` | `LogTests` (daily file, keep 14, unobserved-task/domain/dispatcher handlers log full exception and SetObserved, API errors logged, failed sign-in logged without password, unwritable folder does not throw) |
| M-9 AutomationIds | `4569086` | `AutomationIdTests` (every interactive control in every XAML has a `<Screen>_<Control>` id, unique per file; LabeledBy targets exist and every field label is linked; NavItem name) |
| Translations | `ea25bfe` | `TranslationTests` (every `L.T("…")` and `{l:T '…'}` literal has Arabic, no duplicate keys, placeholders match, new server messages translated) |
| UI test locators | `a0f22ba` | `audit/OnTech.Store.UITests` builds; locators use AutomationIds |

Results: `dotnet build OnTech.Store.sln -c Debug` 0 errors, the only warning is the existing CS1573 in `DashboardController.cs`. Desktop.Tests 150/150 passed. Api.Tests 87/87 passed (SQLite; C changed no server code). UI tests: built, **not run** (lead: 5081 stopped).

Notes for the lead / A / B:
- `ApiError` now has `Code` (ProblemDetails `code`) and `FieldErrors` (parsed `errors` dictionary). `OnTech.Store.Client.Numbers` is the one digit/number helper (Client, so the import sheet and server address use it too). Voucher, receipt and invoice numbers are sent with Western digits.
- Log: `%LOCALAPPDATA%\OnTechStore\logs\OnTechStore-yyyyMMdd.log`, newest 14 kept. Tests write to a temp folder (module initializer), not the user's folder. The user's settings.json was not touched.
- A cancelled list load ends silently; `IsBusy` stays on until the last overlapping call ends.
- AutomationIds: `Login_UsernameBox`, `Login_PasswordBox`, `Login_SignInButton`, `Shell_Nav_<Page>` (Page enum: Home, NewOrder, StockIn, Customers, Suppliers, Orders, StockIns, Catalog, Users), `NewOrder_CategoryCombo/ItemCombo/QuantityBox/CustomerSearchBox/CustomerCombo/IssueVoucherNumberBox/SaveButton`, `Customers_StartNewButton/CompanyNameBox/…/SaveButton`, `Users_Permission_<Permission>` (bound, one per check box), etc. The x:Name-based ids the old UI tests used (`PasswordBox`) are replaced by `Login_PasswordBox`.
- Not done: no run-time (STA window) test of the ids. A draft hung waiting for the dispatcher and was dropped; the static XAML tests cover the ids.

How it looks in Arabic (RTL):
- No layout changes: everything still mirrors through `L.Apply`. MaxLength, AutomationIds and LabeledBy are not visible.
- Errors appear in the existing red banner, in Arabic, one line per field with the Arabic form label, e.g. `اسم الشركة: الحد الأقصى 200 حرفًا.`, `البريد الإلكتروني: عنوان البريد الإلكتروني غير صحيح.`, `أدخل بريدًا إلكترونيًا صحيحًا، أو اترك الحقل فارغًا.`
- A concurrency conflict shows `تم تعديل هذا السجل من جهاز آخر، وتمت إعادة تحميله. راجِع البيانات ثم احفظ مرة أخرى.` and the form stays open with the saved values.
- MustChangePassword opens the existing (RTL) change-password window, not a banner. Lockout shows `تم إدخال كلمة مرور خاطئة عدة مرات. انتظر 30 ثانية ثم حاول مرة أخرى.`
- Arabic-keyboard digits (٥, ۱۲) work in every number field. The menu only lists pages the user can open, so it is shorter for limited users, and Home shows only their action cards.
- Screen readers get Arabic names for menu items (page title) and fields (their Arabic label).
