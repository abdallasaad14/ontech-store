# 01 · Architecture map

Repo `C:\Users\PC\source\ontech-store` @ `d8e7ba7`. Solution `OnTech.Store.sln`, 7 projects.

## Projects, references, packages

| Project | TFM | Output | References | NuGet (requested = resolved) |
|---|---|---|---|---|
| `src/OnTech.Store.Contracts` | net8.0 | DTOs, enums (`Permission`, `OrderStatus`, `StockMovementType`) | – | – |
| `src/OnTech.Store.Client` | net8.0 | `StoreApiClient` (HttpClient wrapper), `ApiError`, `CustomerSheet` (xlsx/csv import), `XlsxWriter` | Contracts | – |
| `src/OnTech.Store.Api` | net8.0 (`InvariantGlobalization=true`, workstation GC) | ASP.NET Core Web API, port 5080 | Contracts | JwtBearer 8.0.11, EFCore.Design/Sqlite/SqlServer 8.0.11, Hosting.WindowsServices 8.0.1, Identity.Core 8.0.11, Swashbuckle 6.6.2 |
| `src/OnTech.Store.Desktop` | net8.0-windows (WPF) | `OnTechStore.exe` | Client (→ Contracts) | CommunityToolkit.Mvvm 8.2.2 |
| `tests/OnTech.Store.Api.Tests` | net8.0 | xUnit, in-process `WebApplicationFactory` on SQLite (SQL Server if `ONTECH_TEST_SQLSERVER` set) | Api, Client | xunit 2.4.2, runner 2.4.5, Test.Sdk 17.6.0, Mvc.Testing 8.0.11, coverlet 6.0.0 |
| `tools/OnTech.Store.LoadTest` | net8.0 | simulates N PCs | Client | – |
| `tools/OnTech.Store.DemoData` | net8.0 | adds `DEMO-` orders/stock-ins to a SQLite copy | Api | – |

All app projects target .NET 8 consistently (no framework mismatch). There is no `global.json`; the PC has SDKs 9.0.203 and 10.0.300 only, so builds use the 10.0 SDK targeting net8.0. Runtime on the PC: 8.0.15 (ASP.NET Core + WindowsDesktop).

## Runtime topology

```
 up to 3 office PCs                                server PC (this PC)
┌─────────────────────────┐   HTTP/JSON + JWT   ┌──────────────────────────────┐      ┌───────────────────────────┐
│ OnTechStore.exe (WPF)   │ ──────────────────► │ OnTech.Store.Api (Kestrel)   │ ───► │ SQL Server .\SQLEXPRESS   │ (Production config)
│ %LocalAppData%\         │  http://<pc>:5080   │ 0.0.0.0:5080, Swagger on     │  EF  │   DB OnTechStore          │
│ OnTechStore\settings.json│  no TLS            │ Windows service (optional)   │ Core │ – or –                    │
└─────────────────────────┘                     └──────────────────────────────┘      │ SQLite ontech-store-dev.db│ (Development config = what runs today)
                                                                                       └───────────────────────────┘
```

The WPF app **never** opens a DB connection. Every read/write goes through the API.

## Connection strings and secrets (every place found)

| Where | Value | Notes |
|---|---|---|
| `src/OnTech.Store.Api/appsettings.json:4` | `Server=.\SQLEXPRESS;Database=OnTechStore;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False` | Windows auth, no password. `TrustServerCertificate=True` disables cert validation. |
| `src/OnTech.Store.Api/appsettings.Development.json:3` | `Data Source=ontech-store-dev.db` | Relative path → resolved against the **process working directory**, not the content root. |
| `src/OnTech.Store.Api/Data/DesignTimeFactory.cs:11` | hard-coded `Server=.\SQLEXPRESS;Database=OnTechStore;...` | design-time only (`dotnet ef`). |
| `tools/OnTech.Store.DemoData/DemoDataTool.cs:70` | `Data Source={output}` | tool only. |
| `tests/.../StoreApiFactory.cs:24,35,41` | env `ONTECH_TEST_SQLSERVER` or temp SQLite | tests only. |
| `src/OnTech.Store.Desktop/Services/AppSettings.cs:9` | default server URL `http://localhost:5080` | user can change it on the login screen. |
| **Secret** `appsettings.json:12` `Jwt:SigningKey` | `REPLACE-THIS-with-a-long-random-secret-of-at-least-32-chars` | passes the ≥32-char startup check, so an un-replaced key is accepted. `scripts/install-service.ps1:33` replaces it only when the script is used. |
| **Secret** `appsettings.Development.json:9` `Jwt:SigningKey` | `development-only-signing-key-0123456789abcdef` | committed to git **and used by the live server today** (it runs in Development). |
| **Credential** `appsettings.json:17`, `DatabaseSetup.cs:11` | seed admin `admin` / `ChangeMe!123` | account is created with `MustChangePassword=true` (enforced only by the desktop app, see report). |

No environment variables are read by the app itself except the standard `ASPNETCORE_*`/`DOTNET_*`.

## Screens → ViewModel → API calls

Navigation: `App` → `LoginWindow` (+ `ChangePasswordWindow` if required) → `MainWindow` (sidebar, `ShellViewModel`), pages are swapped via `DataTemplate`s in `MainWindow.xaml`. Pop-ups: `ItemCardWindow`, `VoucherWindow`, `PromptWindow`, `ChangePasswordWindow`, `MessageBox` via `Dialogs`.

| View | ViewModel | Menu gate (client) | API endpoints used (server permission) |
|---|---|---|---|
| `Views/LoginWindow` | `LoginViewModel` | – | `GET api/health` (anon), `POST api/auth/login` (anon) |
| `Views/ChangePasswordWindow` | `ChangePasswordViewModel` | – | `POST api/auth/change-password`, `GET api/auth/me` (auth) |
| `MainWindow` | `ShellViewModel` | – | – |
| `Views/HomeView` (+`StockPill`) | `HomeViewModel` | always | `GET api/categories` (ViewStock), `GET api/dashboard/summary` (ViewStock), `GET api/items` (ViewStock) |
| `Views/NewOrderView` | `NewOrderViewModel` | CreateOrders | `GET api/categories`, `GET api/items`, `GET api/items/{id}` (ViewStock), `GET api/customers` (auth only), `POST api/customers` (ManageCustomers), `POST api/orders` (CreateOrders) |
| `Views/StockInView` | `StockInViewModel` | AddStock | `GET api/categories`, `GET api/items` (ViewStock), `GET api/suppliers` (auth only), `POST api/suppliers` (ManageSuppliers), `POST api/stock-ins` (AddStock) |
| `Views/CustomersView` | `CustomersViewModel` | always | `GET/POST/PUT/DELETE api/customers`, `POST api/customers/import` (ManageCustomers for writes) |
| `Views/SuppliersView` | `SuppliersViewModel` | always | `GET/POST/PUT/DELETE api/suppliers` (ManageSuppliers for writes) |
| `Views/OrdersView` | `OrdersViewModel` | ViewOrders | `GET api/orders` (ViewOrders), `POST api/orders/{id}/cancel` (CancelOrders) |
| `Views/StockInsView` | `StockInsViewModel` | ViewOrders | `GET api/stock-ins` (ViewOrders), `POST api/stock-ins/{id}/cancel` (CancelOrders) |
| `Views/CatalogView` | `CatalogViewModel` | ManageCatalog | `GET api/categories`, `GET api/items` (ViewStock!), `POST/PUT/DELETE api/categories`, `POST/PUT/DELETE api/items` (ManageCatalog) |
| `Views/UsersView` | `UsersViewModel` | ManageUsers | `GET/POST/PUT/DELETE api/users`, `POST api/users/{id}/reset-password` (ManageUsers) |
| `Views/ItemCardWindow` | `ItemCardViewModel` | opened from Home/NewOrder/StockIn/Catalog | `GET api/items/{id}/card` (ViewStock); print via `Services/ItemCardPrinter` (FlowDocument), Excel via `Client/XlsxWriter` |
| `Views/VoucherWindow` | `VoucherViewModel` | from item card | `GET api/orders/{id}` / `GET api/stock-ins/{id}` (ViewOrders) |
| `Views/PromptWindow` | – (code-behind) | – | – |
| `Views/MessageBanners` | binds `ErrorMessage`/`SuccessMessage` of the page VM | – | – |

Endpoints exposed but **not called** by the desktop app: `GET api/stock-movements`, `GET api/orders/by-receipt/{n}`, `GET api/categories/{id}`, `GET api/customers/{id}`, `GET api/suppliers/{id}`, `GET api/users/{id}`, Swagger.

Desktop services: `Services/L.cs` + `Arabic.cs` (translation, `FlowDirection`), `Dialogs.cs`, `Session.cs`, `AppSettings.cs`, `ItemCards.cs`, `ItemCardPrinter.cs`; `Converters.cs`.

## Data flow

```
View (XAML, {Binding}, {l:T})
  └─► ViewModel ([ObservableProperty], [RelayCommand] async, RunAsync(): IsBusy + Error/Success banners)
        └─► Session.Api = StoreApiClient (one HttpClient per sign-in, 20 s timeout, Bearer token)
              └─► HTTP JSON ─► ASP.NET Core: JwtBearer → OnTokenValidated (re-reads User row every request)
                                 → HasPermission policy → Controller
                                     ├─ reads: EF LINQ (AsNoTracking) on StoreDbContext
                                     └─ stock changes: StockService (one DB transaction, conditional
                                        UPDATE Items SET Quantity = Quantity + @c WHERE Id=@id AND Quantity + @c >= 0,
                                        StockMovement row per change)
                                   → StoreDbContext (scoped per request) → SQL Server / SQLite
  ◄─ errors: ApiException → ProblemDetails {title, shortages} → ApiError → L.Message() (Arabic) → ErrorMessage banner
```

Server startup (`Program.cs` → `DatabaseSetup.InitializeDatabaseAsync`): SQL Server `Migrate()` / SQLite `EnsureCreated()` + hand-written `UpgradeSqliteAsync` → renumber orders by voucher → create first admin → apply opening data (once, `AppliedSeeds`) → normalize/backfill catalog codes.

## Database tables (from `StoreDbContext`)

`Users`, `Categories`, `Items` (CHECK `Quantity >= 0`, `LowStockThreshold >= 0`), `Customers`, `Suppliers`, `Orders`, `OrderLines` (CHECK `Quantity > 0`), `StockIns`, `StockInLines` (CHECK `Quantity > 0`), `StockMovements`, `AppliedSeeds`, `__EFMigrationsHistory` (SQL Server only). No views, no stored procedures, no triggers.
