# OnTech Store: audit report

2026-11-08 · repo `C:\Users\PC\source\ontech-store` @ `d8e7ba7` · no application code was changed.

**How severity was ranked.** The owner confirmed that the app runs only on the office network and handles small amounts of data. Security issues that need an outside attacker, and anything that only appears with tens of thousands of rows, were moved down. Bugs that affect daily use were moved up.

## 1. Executive summary

The app is in **good shape for daily use.** The stock logic, which matters most, held up under every test:
- 20 PCs ordering at the same moment never oversold.
- A failure forced in the middle of an order rolled back completely.
- Five PCs cancelling the same order at once returned the stock only once.
- The schema matches the code exactly.
- WPF reported no binding errors in either English or Arabic.

The problems are mostly in **permissions, Arabic input, error messages, and the not-yet-used SQL Server path.**

| Severity | Count |
|---|---|
| Critical | 0 |
| High | 4 |
| Medium | 9 |
| Low | 10 |

**Top 5 to fix first**
1. **H-1** The server crashes on startup with the production (SQL Server) config. The move to SQL Server or a Windows service is blocked.
2. **H-2** Menu pages open and then fail with "You don't have permission". This happens for any user who lacks `ViewStock`.
3. **H-3** On an Arabic keyboard, Arabic digits (٥) are refused in quantity and number fields.
4. **H-4** The live server runs in **Development** mode, with a signing key committed to git. Anyone on the LAN who has the repo can make an admin token.
5. **M-1** When two PCs edit the same customer, supplier, item or user, the second save silently overwrites the first.

## 2. Issues

| ID | Sev | Layer | Title | File:line | How to reproduce | Root cause | Suggested fix | Effort |
|---|---|---|---|---|---|---|---|---|
| H-1 | High | Config | API crashes at startup on SQL Server | `src/OnTech.Store.Api/OnTech.Store.Api.csproj:8` | Run the API with `ASPNETCORE_ENVIRONMENT=Production` → `CultureNotFoundException: en-us is an invalid culture identifier` (`evidence/api_sqlserver_stderr.log`) | `InvariantGlobalization=true` breaks Microsoft.Data.SqlClient. The tests run with their own runtimeconfig, so they never see this | Remove `InvariantGlobalization`, or set it to false. Add one start-up smoke test of the published API on SQL Server | S |
| H-2 | High | Backend/ViewModel | Pages shown in the menu fail with 403 | `ViewModels/ShellViewModel.cs:22-28`, `CategoriesController.cs:16`, `ItemsController.cs:17` | Create a user with only CreateOrders (or AddStock / ManageCatalog). Open New Order → "You don't have permission" (`evidence/tests/permissions.log`) | The menu checks one permission, but the page also calls `GET api/categories` and `api/items`, which need `ViewStock` | Grant ViewStock automatically with those permissions, or let those GETs accept any of them | S |
| H-3 | High | ViewModel | Arabic-Indic digits refused | `NewOrderViewModel.cs:82`, `StockInViewModel.cs:57`, `CustomersViewModel.cs:85`, `SuppliersViewModel.cs:84`, `CatalogViewModel.cs:197` | Arabic keyboard, type ٥ as the quantity → "Enter a quantity of 1 or more" (UI test `New_order_invalid_inputs_show_messages`, `evidence/tests/viewmodels.log`) | `int.TryParse` in .NET does not accept native digits | Convert ٠-٩ and ۰-۹ to 0-9 before parsing (one helper) | S |
| H-4 | High | Config/Security | Live server uses Development config and a key that is in git | `appsettings.Development.json:9`, `appsettings.json:12`, `docs/STATUS.md` (server started with `dotnet run`) | Token signed with `development-only-signing-key-…` and `sub=1` → `GET api/users` returns 200 on both servers (`evidence/tests/security.log`) | The signing key is in source control. Development is the live mode | Run the live server in Production with a random key (`install-service.ps1` already does this) | S |
| M-1 | Medium | Backend/DB | Lost updates when two PCs edit the same record | `CustomersController.cs:57-66` (same pattern in Suppliers, Items, Categories, Users) | PC1 saves a phone number, then PC2 saves notes from a stale form → the phone becomes NULL, no warning (`concurrency.log`) | No concurrency token | Add a `rowversion` / `UpdatedAt` check and return 409 "changed on another PC" | M |
| M-2 | Medium | Backend | `MustChangePassword` is only enforced by the desktop app | `AuthController.cs`, `AuthSetup.cs:39-56` | A user flagged MustChangePassword calls `GET api/items` with the old password → 200 | The flag is not checked on the server | Refuse every endpoint except `me` and `change-password` while the flag is set | S |
| M-3 | Medium | WPF | Server validation messages are English and technical, even in Arabic | `Services/Arabic.cs` (no DataAnnotations patterns), all forms | Customers → invalid email → "The Email field is not a valid e-mail address." in English (UI test screenshot `Customer_with_invalid_email…png`). Too long: "The field CompanyName must be a string … maximum length of '200'" | DataAnnotation messages are not translated or mapped to fields | Add `MaxLength` to the TextBoxes (only 1 of 45 has one) and Arabic `Patterns` for "maximum length", "not a valid e-mail", "is required" | S |
| M-4 | Medium | DB/Backend | Voucher uniqueness depends on letter case on SQLite only | `StockService.cs:31` + SQLite collation | `AB-1`, then `ab-1` → SQLite accepts both, SQL Server returns 409 (`case-sensitivity.log`) | SQLite compares case-sensitively; SQL Server is `CI_AS` | Normalise to upper case on save, or use `COLLATE NOCASE` on SQLite | S |
| M-5 | Medium | Desktop | The desktop app writes no log | `App.xaml.cs:107-112` | Any unexpected error shows a MessageBox and is lost. No `TaskScheduler.UnobservedTaskException` handler | – | Log to `%LocalAppData%\OnTechStore\logs` and add the unobserved-task handler | S |
| M-6 | Medium | Platform | .NET 8 support ends 10 Nov 2026. The runtime on the PC is 8.0.15 | all csproj | `dotnet --list-runtimes` | – | Install the last 8.0.x patch now, then plan .NET 10 (LTS) | M |
| M-7 | Medium | Packages | `SQLitePCLRaw.lib.e_sqlite3 2.1.6` has a High advisory (GHSA-2m69-gcr7-jv3q). This is the live DB engine | Api, DemoData (transitive) | `dotnet list package --vulnerable` (`evidence/packages_vulnerable.txt`) | old transitive | Reference `SQLitePCLRaw.bundle_e_sqlite3` ≥ the patched version directly | S |
| M-8 | Medium | Backend | 500 instead of 409 when two PCs set the same customer or supplier number | `CustomersController.cs:57` (Update is not behind `NumberingGate`), `ApiException.cs:27` | Two parallel PUTs with the same number → `ok,500` (`concurrency.log`) | The unique index violation is not mapped to 409 | Catch `DbUpdateException` in Update, or map unique violations to 409 globally | S |
| M-9 | Medium | Accessibility/Test | No `AutomationId` on any control. TextBoxes have no label link | all `Views/*.xaml` (0 of about 176 controls; only 17 `x:Name`) | FlaUI must find fields by position. Screen readers read empty names | – | Add `AutomationProperties.AutomationId` and `LabeledBy` to inputs and buttons. Name the menu items (they read as `NavItem { … }`) | M |
| L-1 | Low | Backend | `page=2147483647` → 500 on SQL Server, wrong rows on SQLite | `OrdersController.cs:47`, `StockInsController.cs:48`, `StockMovementsController.cs:29` | `GET api/orders?page=2147483647` (`paging-*.log`) | int overflow in `(Page-1)*PageSize` | Use a long, or cap Page | S |
| L-2 | Low | Security | No lockout or delay after wrong passwords | `AuthController.cs:19-34` | 50 wrong logins → all 401, then the right one → 200 | – | Simple per-user delay or lockout | S |
| L-3 | Low | Security | Users with no permissions can read all customers and suppliers | `CustomersController.cs:17,33`, `SuppliersController.cs:17,32` | Permission.None user → 200 | No `[HasPermission]` on the GETs | Require ViewStock, ManageCustomers or CreateOrders | S |
| L-4 | Low | Security | Plain HTTP, `TrustServerCertificate=True`, Swagger open without sign-in | `appsettings.json` | anonymous `GET /swagger/v1/swagger.json` → 200 | by design for LAN | Acceptable for local use. Revisit if the app is ever exposed outside the office | – |
| L-5 | Low | Security | Seed admin `admin` / `ChangeMe!123` in config | `appsettings.json:17` | – | flagged MustChangePassword, see M-2 | Fix M-2 | – |
| L-6 | Low | DB | `Customers.CreatedByUserId` and `Suppliers.CreatedByUserId` have no FK | `StoreDbContext.cs:61,105` | `SchemaTests.User_id_columns_have_foreign_keys` | – | Add FKs in a migration | S |
| L-7 | Low | Backend | Moving an item to another category keeps the old category's code prefix | `ItemsController.cs:156-158` | Edit an item and change its category | by design (`CatalogCodes` comment), but confusing | Document it, or offer to re-code | S |
| L-8 | Low | ViewModel | Overlapping reloads can show stale lists (Home, Catalog categories, Orders) | `HomeViewModel.cs:61-73`, `CatalogViewModel.cs:59-66`, `OrdersViewModel.cs:29-45` | Change filters quickly | No version guard (NewOrder and StockIn already have one) | Same `_version` pattern | S |
| L-9 | Low | Backend | Customers list and item card are not paged | `CustomersController.cs:18`, `ItemsController.cs:72` | 50k customers → 12.8 MB, about 0.4–0.9 s (`performance.log`) | Unbounded queries | Not urgent at the owner's data size | M |
| L-10 | Low | Build | CS1573 doc warning. Analyzers report culture-dependent `ToString()` (CA1305 ×7) | `DashboardController.cs:18` | `evidence/build_analyzers.txt` | – | Cosmetic | S |

## 3. Findings by layer (evidence)

- **Config:** H-1 stack trace in `evidence/api_sqlserver_stderr.log`. All connection strings and secrets are listed in `01_architecture_map.md`. App connection logic tested: `.\SQLEXPRESS`, Windows auth, `Encrypt=True` with `TrustServerCertificate=True`, connect timeout 15 s, command timeout 30 s.
- **Backend:** endpoint checks in `evidence/tests/endpoints-*.log`, `validation-*.log`, `malformed-*.log` (no 500 on malformed bodies), `paging-*.log`, `security.log`, `permissions.log`.
- **DB:** the transaction with a forced failure (trigger on `StockMovements`) returned 500, and quantity, order and lines were all unchanged (`transactions.log`). Concurrency results are in `concurrency.log`.
- **WPF:** binding trace while every page is open, both languages: 0 errors. A deliberately wrong probe binding *was* captured, so the trace works (`evidence/binding_trace_*.txt`). Double-click and triple-click on Save created exactly 1 order.
- **Logs (6c):** the live `server.log` has no runtime errors, only build warnings. The audit server logs show only the expected failures from the tests (forced trigger, the 500s for M-8 and L-1).

## 4. DB vs code schema

- SQL Server (`OnTechStore`, created by the app's 7 migrations): **0 differences.** No pending migrations, model = snapshot, every column's nullability and length match.
- Live SQLite file: every model column is present, with no nullability mismatch.
- Missing FKs: L-6. Snapshot columns without FK by design: `OrderLines.CategoryId`, `StockInLines.CategoryId`.
- No stored procedures, views or triggers are used by the app.
- Collation difference: M-4.

## 5. Test results

| Suite | Result | Notes |
|---|---|---|
| Existing `tests/OnTech.Store.Api.Tests` (SQLite) | 87/87 passed, 25 s | `evidence/testresults/existing_sqlite.trx` |
| Existing suite on SQL Server | 87/87 passed, 20 s | does not catch H-1 (see root cause) |
| `audit/OnTech.Store.Tests` (integration + ViewModel + schema + binding trace) | 63 passed / 21 failed of 84 (first run); the double-save test was rewritten and now passes; binding trace 1/1 passed | every failure = an issue above (H-1…H-4, M-1, M-2, M-4, M-8, L-1, L-2, L-3, L-6, L-9) · `evidence/audit_tests_run1.txt` |
| `audit/OnTech.Store.UITests` (FlaUI, real exe) | 9 passed / 4 failed / 1 skipped, 2 m 30 s | see below · `evidence/ui_tests.log`, `screenshots/` |

UI tests, final run:
- **Passed:** sign-in errors (wrong password, empty fields, server down), open all 9 pages (each ready in 0.5–1.1 s), add + search customer, New Order invalid input (shows the ٥ problem), rapid double-click on Save → 1 order, Orders log search, Arabic sign-in.
- **Failed:**
  - `Customer_with_invalid_email…` failed on purpose: it captured the English validation text (M-3).
  - `New_order_saves_and_takes_stock`, `Stock_in_saves_and_adds_stock` and `Item_card_opens_and_exports` failed on test locators. Fields are found by position because there are no AutomationIds (M-9). The same flows are verified through the ViewModel and API tests.
- **Skipped:** the 50k-row screen timing, after the owner said data is small.

Screenshots: `audit/screenshots/<test>.png` (and `<test>_ok.png` / per-page shots). Failure logs: `audit/screenshots/<test>.log`.

## 6. Verified as working

- No overselling with 20 parallel orders.
- No partial orders on a shortage or a forced DB failure.
- Parallel cancels return stock once.
- A stock-in whose stock was already issued can't be cancelled.
- Stock-ins and orders running together keep the balance.
- Duplicate voucher numbers give 409, even in parallel.
- Item codes stay unique in parallel.
- Zero, negative and too-big quantities are refused with 400.
- Arabic, mixed and SQL-looking text is stored and read back exactly.
- A disabled user is signed out on the next call.
- The client shows readable messages when the server is down (about 2.4 s) or does not answer.
- Dates come back as UTC offsets.
- Schema and migrations are consistent.
- The Arabic RTL login and menus work.
- There are no `async void`, `.Result` or `.Wait()` calls on the UI thread in app code, and no SQL built by string concatenation.

## 7. Not tested, and why

- **Print / PDF and Excel export:** they open Windows print and save dialogs, which I didn't drive. The item card itself was checked.
- **A second physical PC over the LAN:** everything ran on this PC. Parallel HTTP clients simulated the other PCs.
- **50k-row UI freeze:** dropped at the owner's request. The API timings with 50k rows are in `evidence/tests/performance.log` (all under 2 s).
- **The live server on 5080:** deliberately left untouched. Tests used copies on 5081 (SQL Server) and 5082 (SQLite).
- **Windows-service install script:** not run, because it changes system services.

## Restore

The SQL Server DB was already restored once to `OnTechStore_before_audit.bak` before the final UI run. The SQLite copy on 5082 holds the test data; the live file was never touched. See `restore.sql`.
