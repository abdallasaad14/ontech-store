# Project status: what is built and where it lives

Read this first. It lists every feature on `main`, its state, and the files that hold it, so you only open the code you need. The README has the full detail (setup, API table, data model, permissions); this file is the map.

**Keep it current.** Any PR that adds or changes a feature updates this file in the same PR: the feature's row, its files, and the "Last updated" line.

Last updated: 2026-10-28, main after PR #7.

## The app in one paragraph

OnTech's internal stock app. A small ASP.NET Core server (one office PC) keeps categories, items and quantities, customers, suppliers and users. Up to 3 PCs run the WPF desktop app. Stock only changes through two vouchers: **New Order** (إذن صرف, takes stock out to a customer) and **Stock In** (إذن إضافة, adds stock from a supplier). Every change is a `StockMovement` row. The desktop app works in English and Arabic (RTL).

## Projects

| Project | What it is |
|---|---|
| `src/OnTech.Store.Api` | Server: ASP.NET Core 8 Web API, EF Core, JWT. Port 5080, Swagger at `/swagger`. SQL Server in production, SQLite in Development and tests. |
| `src/OnTech.Store.Contracts` | DTOs and enums shared by server and app (`Permission`, `OrderStatus`, `StockMovementType`, `*Dtos.cs`). |
| `src/OnTech.Store.Client` | `StoreApiClient` (typed HTTP client, one method per endpoint), `ApiError` (server error to readable text), `CustomerSheet` (reads .xlsx/.csv for import). |
| `src/OnTech.Store.Desktop` | WPF app, exe `OnTechStore`. MVVM with CommunityToolkit.Mvvm. |
| `tests/OnTech.Store.Api.Tests` | xUnit tests: end-to-end API on SQLite (`StoreApiTests`, `FeatureCoverageTests`), client against real server (`StoreApiClientTests`), `CustomerSheetTests`. Host is `StoreApiFactory`. |
| `tools/OnTech.Store.LoadTest` | Simulates N PCs against a running server; timings per screen plus a stock check. |
| `scripts/` | `install-service.ps1` (publish + Windows service + firewall), `backup-database.ps1` (SQL Server only). |
| `db/OnTechStore.sql`, `docs/swagger.json` | Schema script and OpenAPI snapshot, for reference. |

## How a feature is wired (follow this path)

- **Server:** `Controllers/<X>Controller.cs` → `Services/StockService.cs` for anything that moves stock → `Data/Entities.cs` + `Data/StoreDbContext.cs` (keys, indexes, checks). DTO mapping in `Services/Mapping.cs`. Errors are thrown as `Infrastructure/ApiException.cs` and come out as ProblemDetails.
- **Contracts:** request/response shapes in `src/OnTech.Store.Contracts/<Area>Dtos.cs`.
- **Client:** a method in `src/OnTech.Store.Client/StoreApiClient.cs`.
- **Desktop page:** `ViewModels/<X>ViewModel.cs` + `Views/<X>View.xaml`. The `Page` enum, the menu and the page factory are in `ViewModels/ShellViewModel.cs`; the ViewModel→View `DataTemplate` is in `MainWindow.xaml`. Pages derive from `ViewModelBase` (`RunAsync`, error/success banners via `Views/MessageBanners.xaml`).
- **Text:** `{l:T '...'}` / `L.T("...")` (`Services/L.cs`) with Arabic in `Services/Arabic.cs` (exact entries, plus `Patterns` for server messages with names or numbers). See `CLAUDE.md`.
- **Schema change:** add an EF migration under `Data/Migrations` (SQL Server) **and** the same tables/columns in `DatabaseSetup.UpgradeSqliteAsync` (SQLite files are made with `EnsureCreated` and upgraded by hand; back up first with `BackupSqliteAsync`).

## Features

State: ✅ done and merged · 🟡 basic version, more planned · ⏳ not started.

| Feature | State | PR | Server | Desktop | Notes |
|---|---|---|---|---|---|
| Sign in with username + password, forced password change | ✅ | #1, #2 | `AuthController` (`/api/auth/login`, `me`, `change-password`), `Auth/TokenService.cs`, `Auth/AuthSetup.cs`, `Services/PasswordService.cs` (PBKDF2) | `Views/LoginWindow`, `LoginViewModel`, `Views/ChangePasswordWindow`, `ChangePasswordViewModel`, `Services/Session.cs`, `Services/AppSettings.cs` (server URL, last username, language in `%LocalAppData%\OnTechStore\settings.json`) | Seed admin `admin` / `ChangeMe!123` from `appsettings.json` `Seed`, made by `DatabaseSetup.CreateFirstAdminAsync`. Token lasts 12 h. |
| Users and permissions | ✅ | #1, #2 | `UsersController` (`/api/users`, reset-password), `Auth/Permissions.cs` (checked against the DB on every request) | `UsersViewModel` / `UsersView` (admin menu) | `Permission` flags enum in Contracts: ViewStock 1, ManageCustomers 2, CreateOrders 4, AddStock 8, ViewOrders 16, CancelOrders 32, ManageCatalog 64, ManageUsers 128, ManageSuppliers 256. `StandardUser` preset. Users are disabled, never deleted. |
| Categories and items (catalog) | ✅ | #1, #2 | `CategoriesController`, `ItemsController` (`?categoryId=&search=&inStockOnly=&lowStockOnly=`) | `CatalogViewModel` / `CatalogView` (admin menu) | New items start at 0; quantity can never be typed. Low-stock level per item. |
| Digit-only codes | ✅ | #6 | `Services/CatalogCodes.cs` (next code, validation, converts old `C01`/`C01-001`, backfill on startup) | code boxes in `CatalogView` | Category `01`..`11`; item = category code + 3 digits (`04001`), unique across all categories. Search matches from the start of the code. |
| Customers | ✅ | #1, #2, #3 | `CustomersController` (search by number or name; `POST /api/customers/import`) | `CustomersViewModel` / `CustomersView`, import via `Client/CustomerSheet.cs` | `CustomerNumber` unique, auto next. Import skips existing numbers, so re-importing is safe. |
| Suppliers | ✅ | #7 | `SuppliersController` | `SuppliersViewModel` / `SuppliersView` | Same shape as customers (`SupplierNumber`), no import. |
| New Order (issue voucher, إذن صرف) | ✅ | #1, #2, #7 | `OrdersController` `POST /api/orders` → `StockService.CreateOrderAsync` | `NewOrderViewModel` / `NewOrderView` | Several items per order. `IssueVoucherNumber` required and unique; receipt number optional, unique when set. Stock taken with a conditional `UPDATE ... WHERE Quantity >= n` in item-id order, so 3 PCs can't oversell; a shortage returns 409 and takes nothing. Order numbers `ORD-000123`. |
| Stock In (receipt voucher, إذن إضافة) | ✅ | #7 | `StockInsController` `POST /api/stock-ins` → `StockService.CreateStockInAsync` | `StockInViewModel` / `StockInView` | Replaced the old Add Quantity screen. Supplier + unique `VoucherNumber` + optional supplier invoice. Numbers `IN-000012`. |
| Orders log | 🟡 | #1, #2, #7 | `GET /api/orders` (many filters, paging), `/{id}`, `by-receipt/{n}`, `/{id}/cancel` → `StockService.CancelOrderAsync` | `OrdersViewModel` / `OrdersView` | Has search, receipt filter, paging, details, cancel. Plan step 3: full log with every filter in the UI and receipt PDFs. |
| Stock In log | ✅ | #7 | `GET /api/stock-ins`, `/{id}`, `/{id}/cancel` → `StockService.CancelStockInAsync` (409 if that stock was already issued) | `StockInsViewModel` / `StockInsView` | |
| Home page / dashboard | ✅ | #1, #2 | `DashboardController` `GET /api/dashboard/summary` | `HomeViewModel` / `HomeView`, `Views/StockPill` (In stock / Low / Out) | Three big actions: New Order, Stock In, Add Customer. Double-click an item to order it. |
| Stock history (movements) | ✅ server only | #1 | `StockMovementsController` `GET /api/stock-movements` | none yet | Types in `StockMovementType`: Initial, Restock and Adjustment (history only), Order, OrderCancelled, StockIn, StockInCancelled. |
| Opening data (OnTech's stock and customers) | ✅ | #3 | `Data/OpeningData.cs` + `Data/Seed/opening-data.json` (11 categories, 318 items, 505 customers) | | Applied once on startup, recorded in the `AppliedSeeds` table. |
| Arabic and right-to-left layout | ✅ | #5 | Server messages are English; the app translates them | `Services/L.cs`, `Services/Arabic.cs`, `Services/Dialogs.cs`, `Converters.cs` (`Translate`) | Language switch on the sign-in screen and in the sidebar. Rules in `CLAUDE.md`. |
| Design ("Clean sidebar") | ✅ | #2 | | `Themes/Theme.xaml`, `MainWindow.xaml` | Brand blue `#055A9A`, gray `#A6A5AA`, Segoe UI. |
| Tests and load test | ✅ | #4 | `tests/OnTech.Store.Api.Tests`, `tools/OnTech.Store.LoadTest` | | Baseline: 3 PCs on SQLite, p95 under 20 ms, no errors. Server uses workstation GC. |
| Safe SQLite upgrades | ✅ | #7 | `DatabaseSetup.UpgradeSqliteAsync`, `BackupSqliteAsync` (`VACUUM INTO <db>-backup-before-<change>-<time>.db`) | | |
| Packaging, installer, backups | 🟡 | #1 | `scripts/install-service.ps1`, `scripts/backup-database.ps1` | publish command in README | Plan step 4. Scripts exist for SQL Server; the live install still runs SQLite (see below). |
| Receipt PDFs | ⏳ | | | | Plan step 3. |

## Startup order (server)

`Program.cs` → `DatabaseSetup.InitializeDatabaseAsync`: migrate (SQL Server) or `EnsureCreated` + `UpgradeSqliteAsync` (SQLite) → create the first admin if none → `OpeningData.ApplyAsync` → `CatalogCodes.MakeNumericAsync` → `CatalogCodes.BackfillAsync`.

## Database tables

`Users`, `Categories`, `Items` (`Quantity` has a DB check `>= 0`), `Customers`, `Suppliers`, `Orders`, `OrderLines`, `StockIns`, `StockInLines` (lines keep a snapshot of item and category names), `StockMovements`, `AppliedSeeds`. Rows that history points to are disabled, never deleted. Migrations: `InitialCreate`, `UsernameLogin`, `CustomerNumber`, `OpeningData`, `CategoryCode`, `UniqueNumericItemCodes`, `SuppliersAndStockIn`.

## Rules decided with the owner

- Stock changes only through a voucher tied to a customer or supplier. Never add a screen or endpoint that types a quantity directly; ask first if a stock-count correction is needed.
- Every desktop change works in English and Arabic (RTL). See `CLAUDE.md`.
- The server must stay light (normal office PC, up to 3 client PCs).

## How it runs on the owner's PC today

- Repo at `C:\Users\PC\source\ontech-store`. The server is not a Windows service yet: it runs hidden with `dotnet run --project src/OnTech.Store.Api -c Release` on SQLite, DB file `src\OnTech.Store.Api\ontech-store-dev.db`, log `src\OnTech.Store.Api\server.log`.
- Back up by copying the `.db` while the server is stopped. `backup-database.ps1` only applies to SQL Server.
- Desktop exe: `src\OnTech.Store.Desktop\bin\Release\net8.0-windows\OnTechStore.exe`.

## Building in a Linux container

- Install the .NET 8 SDK from the packages.microsoft.com Ubuntu repo, then `dotnet test` runs everything on SQLite.
- The desktop project needs the WindowsDesktop SDK, which the Ubuntu package lacks: copy `Sdks/Microsoft.NET.Sdk.WindowsDesktop` from the jammy `dotnet-sdk-8.0` .deb on packages.microsoft.com. `EnableWindowsTargeting` is already set. It builds but can't run on Linux.
- SQL Server can't be installed in the container, so SQL Server paths are only tested on Windows.
