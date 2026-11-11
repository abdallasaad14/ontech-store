# Fix contracts (shared decisions for agents A, B, C)

Branch `fix/audit-main`, tagged `pre-fix` at `d8e7ba7`. Issue IDs, file:line and repro steps: `audit/AUDIT_REPORT.md`.
The shared C# part of this contract is already committed on `fix/audit-main` (Phase 0), so every branch compiles against it:

- `src/OnTech.Store.Contracts/ApiContract.cs`: `ErrorCodes` and the `ReadAccess` table.
- `Version` properties on `CustomerDto`, `SupplierDto`, `ItemDto`, `UserDto` and on `SaveCustomerRequest`, `SaveSupplierRequest`, `UpdateItemRequest`, `UpdateUserRequest`.
- `tests/OnTech.Store.Desktop.Tests` (empty WPF unit-test project, already in the solution).

## 1. Hard rules (everyone)

- Never touch port **5080**, `src/OnTech.Store.Api/ontech-store-dev.db` in the main checkout, the `bin\Release` folder of the main checkout, or the Windows service. Never run `scripts/install-service.ps1`. Never restart anything you did not start.
- Never build in `C:\Users\PC\source\ontech-store` (the main checkout). Work only in your own worktree.
- Ports you may use: A = 5084, B = 5083, C = 5085. 5081/5082 are the lead's audit servers: read-only for you, do not stop or restart them.
- Test databases only: the in-process test host (temp SQLite / throw-away `OnTechStoreTest_*` DBs on `.\SQLEXPRESS`), or your own copy of `C:\Users\PC\source\ontech-store\audit\backups\ontech-store-dev_before_audit.db` copied into your worktree's `audit\fix\scratch\` (git-ignored). Never write to `OnTechStore` on `.\SQLEXPRESS` (that is 5081's DB).
- One issue per commit: `fix(H-3): Arabic digits in quantity fields`. Every fix gets an xUnit regression test that fails before and passes after.
- Only Agent B adds EF migrations or touches `Data/Migrations/` and the model snapshot.
- Do not upgrade to .NET 10. Microsoft.* packages: latest 8.0.x patch only.
- A file you do not own: write the request in your section of `audit/fix/HANDOFF.md` and carry on. The lead applies it at merge time.
- Desktop rules in `CLAUDE.md` apply: no hard-coded UI text, Arabic for every new `L.T`/`{l:T}` text and every new server message in `Services/Arabic.cs`.
- Before you finish: `dotnet build OnTech.Store.sln -c Debug` with 0 errors, and `tests/OnTech.Store.Api.Tests` green on SQLite **and** on SQL Server (`$env:ONTECH_TEST_SQLSERVER='Server=.\SQLEXPRESS;Trusted_Connection=True;TrustServerCertificate=True'`).

## 2. File ownership

| Area | Owner |
|---|---|
| `src/OnTech.Store.Api/OnTech.Store.Api.csproj`, `Program.cs`, `appsettings*.json`, `Properties/launchSettings.json`, `Auth/**`, `Controllers/AuthController.cs`, `scripts/install-service.ps1` (edit only, never run) | **A** |
| In `Data/DatabaseSetup.cs`: only `SeedOptions` and `CreateFirstAdminAsync` | **A** |
| `Controllers/*` except `AuthController.cs`, `Services/**`, `Infrastructure/**`, `Data/**` (except the two members above), `Data/Migrations/**`, `src/OnTech.Store.Contracts/**` | **B** |
| `src/OnTech.Store.Desktop/**`, `src/OnTech.Store.Client/**`, `tests/OnTech.Store.Desktop.Tests/**`, `audit/OnTech.Store.UITests/**` | **C** |
| `tests/OnTech.Store.Api.Tests/StoreApiFactory.cs` | **A** (B and C ask through HANDOFF) |
| New regression test files in `tests/OnTech.Store.Api.Tests/`: `Fix_A_*.cs` | **A** |
| New regression test files in `tests/OnTech.Store.Api.Tests/`: `Fix_B_*.cs` | **B** |
| `audit/OnTech.Store.Tests/**`, `audit/fix/*.md` other than your HANDOFF section, `OnTech.Store.sln`, `docs/STATUS.md`, `tools/**` | **Lead** |

## 3. Error contract

All errors stay **ProblemDetails**, the shape the client already reads: `{ "status": 409, "title": "<readable English message>", "code": "<ErrorCodes.X>" }`.
`title` remains the message shown to the user (the client translates it through `Arabic.cs`). `code` is the new machine-readable extension, from `OnTech.Store.Contracts.ErrorCodes`. It goes through `ApiException(..., extensions: { ["code"] = ... })`.

| Situation | Status | `code` | `title` (exact English, Arabic goes in `Arabic.cs`) |
|---|---|---|---|
| Edit saved with a `Version` that is not the current one | 409 | `ConcurrencyConflict` | `This record was changed on another device. It has been reloaded; check it and save again.` |
| Unique number / code / voucher / username already used (including the race on the unique index) | 409 | `Duplicate` | the existing specific message, e.g. `Customer number 12 is already used.` |
| Signed-in user with `MustChangePassword` calls anything except `GET api/auth/me` and `POST api/auth/change-password` | 403 | `MustChangePassword` | `You must change your password before you continue.` |
| Missing permission (unchanged) | 403 | none | unchanged |
| Too many wrong passwords (L-2) | 429 | `TooManyAttempts` (string literal, A may add it to `ErrorCodes` through HANDOFF) | `Too many wrong passwords. Wait {0} seconds and try again.` |

The client (C) exposes the code as `ApiError.Code` (string?, read from the `code` extension) and reacts on the code, never on the title.

## 4. Concurrency (M-1)

- Entities `Customer`, `Supplier`, `Item`, `User` get `public int Version { get; set; }`, configured `.IsConcurrencyToken()`, default 0 (existing rows start at 0). It is the same plain INTEGER column on SQLite and SQL Server, so one mechanism works for both. No `rowversion`, no triggers.
- Every edit made through the API (PUT, DELETE=disable, and for users also reset-password) does `Version++` in the same SaveChanges. Stock movements (`ExecuteUpdate` on `Items.Quantity` in `StockService`) do **not** change it.
- DTOs return `Version`. Update requests carry `int? Version`. When it is non-null and differs from the stored value, or EF raises `DbUpdateConcurrencyException`, the server returns 409 `ConcurrencyConflict` and changes nothing. `null` = no check (old callers and the existing tests keep working). The desktop app always sends it.
- The client, on 409 `ConcurrencyConflict`: shows the message (Arabic in Arabic mode), reloads that record into the form, and keeps the dialog/page open.

## 5. Read-policy table (H-2, L-3)

The table lives in code as `OnTech.Store.Contracts.ReadAccess`. A caller may read when they hold **any** bit. Admins hold every bit.

| Endpoint(s) | Allowed with any of | Used by |
|---|---|---|
| `GET api/categories`, `api/categories/{id}`, `api/items`, `api/items/{id}` | `ReadAccess.Catalog` = ViewStock, CreateOrders, AddStock, ManageCatalog | Home (stock list), New Order, Stock In, Items & Categories |
| `GET api/customers`, `api/customers/{id}` | `ReadAccess.Customers` = ViewStock, ManageCustomers, CreateOrders | Customers page, New Order customer picker |
| `GET api/suppliers`, `api/suppliers/{id}` | `ReadAccess.Suppliers` = ViewStock, ManageSuppliers, AddStock | Suppliers page, Stock In supplier picker |
| `GET api/dashboard/summary`, `api/items/{id}/card`, `api/stock-movements` | `ReadAccess.StockFigures` = ViewStock | Home figures, item card |
| Orders, stock-ins, users, auth endpoints | unchanged | |

- Server (B): a `[HasAnyPermission(ReadAccess.X)]` attribute (policy `any:<int>`), next to `HasPermission` in `Auth/Permissions.cs`. `Auth/` belongs to A, so B adds the attribute + handler in a **new file** `Auth/AnyPermission.cs`; A does not create that file. The policy provider in `Auth/Permissions.cs` has to learn the `any:` prefix: B writes that one-line request in HANDOFF, or registers its own `IAuthorizationPolicyProvider` decorator inside `AnyPermission.cs`. Prefer the decorator so nothing waits on A.
- A user with `Permission.None` gets 403 on customers and suppliers.
- Client (C): the menu shows a page only when the user can read everything that page loads:
  - Home: always. Load the dashboard figures and stock list only with `ViewStock`. Without it, Home shows only the actions the user may do.
  - New Order: `CreateOrders` (implies catalog and customers).
  - Stock In: `AddStock` (implies catalog and suppliers).
  - Customers: `ReadAccess.Customers`. Suppliers: `ReadAccess.Suppliers`. Add/edit buttons stay behind `ManageCustomers` / `ManageSuppliers`.
  - Orders Log, Stock In Log: `ViewOrders` (unchanged). Items & Categories: `ManageCatalog`. Users: `ManageUsers`.
  - The item card opens only with `ViewStock`.

## 6. Document numbers (M-4)

- Issue voucher (`Orders.IssueVoucherNumber`/`OrderNumber`), receipt number (`Orders.ReceiptNumber`), stock-in voucher (`StockIns.VoucherNumber`), supplier invoice number: on save, **trim and upper-case (invariant)**, on both providers. Duplicate checks compare the normalized value.
- Unique index must be case-insensitive on SQLite too: `COLLATE NOCASE` on the SQLite indexes (through `UpgradeSqliteAsync` for existing files and the model for new ones), SQL Server is already `CI_AS`.
- Existing rows: before upper-casing or re-indexing, B checks the live copy (`audit\backups\ontech-store-dev_before_audit.db`) for values that collide when upper-cased. If there are none, existing rows are upper-cased in the upgrade step. If there are, leave existing rows as they are, report the collisions in HANDOFF, and the lead asks the owner.
- Order lookups by number (`by-receipt/{n}`, search) also compare normalized.

## 7. Database changes (B only)

- The live database is **SQLite** and is created by `EnsureCreated` + `DatabaseSetup.UpgradeSqliteAsync` (no migrations on SQLite). SQL Server uses migrations.
- So every schema change needs **both**: one new EF migration (SQL Server) named `AuditFixes`, and idempotent `UpgradeSqliteAsync` steps (check `pragma_table_info` / `pragma_index_list` first, wrap in a transaction, call `BackupSqliteAsync` first).
- Changes: `Version` columns (4 tables), FKs `Customers.CreatedByUserId` and `Suppliers.CreatedByUserId` → `Users.Id` (`ON DELETE RESTRICT`; SQLite cannot add an FK to an existing table, so on SQLite the FK applies only to new files; document it), case-insensitive voucher indexes.
- Verify on: a fresh SQLite file, your copy of the live SQLite backup, a throw-away SQL Server DB (`OnTechStore_B_*`), and an upgrade from the `pre-fix` schema on SQL Server.

## 8. Paging and input limits (L-1)

`page` ≥ 1 and ≤ 1,000,000, `pageSize` 1..200, computed with `long` or checked before `(page-1)*pageSize`. Out-of-range → 400 (model validation), never 500, on both providers. `utcOffsetMinutes` limited to −840..840.

## 9. Auth and config (A)

- JWT key: read from `Jwt:SigningKey` (env `Jwt__SigningKey` or user-secrets). Removed from every git-tracked `appsettings*.json`. Production with a missing key or a key shorter than 32 bytes fails at startup with a clear message. Development with a missing key generates a random key for that process and logs a warning (tokens then stop working after a restart).
- Seed admin: no password in config. When the database has no admin, `Seed:AdminPassword` must come from env or user-secrets, else startup fails with a clear message. The seeded admin always has `MustChangePassword = true`.
- Swagger only in Development (`Swagger:Enabled` can still force it).
- Lockout (L-2): per username, in memory: after 5 wrong passwords in a row, refuse for 30 s (429, section 3), doubling up to 5 min. A right password resets the counter.

## 10. Client logging (M-5) and UI ids (M-9)

- Log folder `%LOCALAPPDATA%\OnTechStore\logs`, rolling daily, 14 files kept.
- AutomationId naming: `<Screen>_<Control>`, e.g. `NewOrder_SaveButton`, `Login_UsernameBox`, `Shell_Nav_Customers`.

## 11. Not in scope

- M-6: .NET 10 upgrade. Only 8.0.x patch bumps.
- L-9: paging the customer list. The owner's data is small (≈500 customers) and the New Order picker needs them all; left as is unless the owner asks.
- L-4: plain HTTP on the office LAN, accepted.
