# OnTech Store

Internal stock app for OnTech: categories and items with quantities, customers, and orders that take stock out of the store, with an optional receipt number. This repository holds the server (database + REST API). The WPF desktop app will be added under `src/` and reuses `OnTech.Store.Contracts` for all request/response types.

## Layout

| Path | What it is |
|---|---|
| `src/OnTech.Store.Api` | ASP.NET Core 8 Web API, EF Core, JWT login. Runs as a Windows service. |
| `src/OnTech.Store.Contracts` | DTOs and enums shared with the desktop app (`Permission`, `OrderQuery`, `CreateOrderRequest`, ...). |
| `tests/OnTech.Store.Api.Tests` | End-to-end API tests (in-memory host + SQLite), including a 3-PC concurrency test. |
| `db/OnTechStore.sql` | Idempotent SQL script of the schema, for review or manual setup. |
| `docs/swagger.json` | OpenAPI spec snapshot. The live, clickable version is at `/swagger` on the server. |
| `scripts/` | `install-service.ps1` (publish + install Windows service + firewall), `backup-database.ps1`. |

## Deployment (server PC)

Hardware: any normal office PC is enough for 3 clients. The API idles at roughly 60–100 MB RAM; SQL Server Express is capped by Microsoft at 1.4 GB buffer memory and 10 GB per database, which is years of orders for a store this size.

1. Install the [.NET 8 ASP.NET Core Runtime (Hosting Bundle)](https://dotnet.microsoft.com/download/dotnet/8.0), **SQL Server Express** (instance name `SQLEXPRESS`), and `sqlcmd`.
2. Give the server PC a fixed IP address on the office network (or a DHCP reservation).
3. From an elevated PowerShell in the repo root (needs the .NET 8 SDK on the machine that publishes):
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\install-service.ps1
   ```
   This publishes to `C:\OnTechStore\Server`, generates a unique JWT signing key, installs the **OnTech Store Server** service (auto start, auto restart on crash), grants the service account access to SQL Express, and opens TCP 5080 on private/domain networks.
4. Open `http://localhost:5080/api/health`. On first start the server creates the database and the first admin:
   - Email `admin@ontech.local`, password `ChangeMe!123` (change in `appsettings.json` under `Seed` before first start, or change the password after first login; the account is flagged to change it).
5. Schedule `scripts\backup-database.ps1` daily in Task Scheduler (SQL Express has no SQL Agent). Copy the backup folder off the PC regularly.

Client PCs connect to `http://<server-ip>:5080`.

### Configuration (`appsettings.json` next to the exe)

| Key | Default | Notes |
|---|---|---|
| `Urls` | `http://0.0.0.0:5080` | Port the API listens on. |
| `ConnectionStrings:Store` | `Server=.\SQLEXPRESS;Database=OnTechStore;Trusted_Connection=True;...` | Use `User Id=...;Password=...` instead for SQL logins. |
| `Jwt:SigningKey` | sample value | Must be ≥ 32 chars. The install script replaces it. |
| `Jwt:TokenLifetimeHours` | `12` | One login per working day. |
| `Seed:*` | see file | Only used when no admin exists. |
| `Swagger:Enabled` | `true` | API docs at `/swagger`. |

## Development

```bash
dotnet test                       # 16 end-to-end tests on SQLite
# same tests on a real SQL Server, through the migrations (database name is added per test run):
ONTECH_TEST_SQLSERVER="Server=localhost;User Id=sa;Password=...;TrustServerCertificate=True" dotnet test
cd src/OnTech.Store.Api
ASPNETCORE_ENVIRONMENT=Development dotnet run   # uses a local SQLite file, no SQL Server needed
# open http://localhost:5080/swagger
```

Schema changes: edit `Data/Entities.cs` / `StoreDbContext.cs`, then
`dotnet ef migrations add <Name> -o Data/Migrations` from `src/OnTech.Store.Api`. The server applies pending migrations on startup.

## Data model

```
Users ─┐                          Categories 1─* Items ──* StockMovements
       │ creates                                  │            │
Customers 1─* Orders 1─* OrderLines *─────────────┘            │
                 └──────────────────────────────────────────────┘ (order movements)
```

- **Users**: name, email (unique, case-insensitive), PBKDF2 password hash, `IsAdmin`, `Permissions` bit mask, `IsActive`, `MustChangePassword`.
- **Categories**: unique name, active flag. **Items**: category, name (unique per category), optional code/model, `Quantity` (DB check: never below 0), `LowStockThreshold`.
- **Customers**: `CompanyName` (required, unique), phone, tax number (unique when set), contact person, email, address, notes.
- **Orders**: `ORD-000123` number, customer, `WithReceipt` + `ReceiptNumber` (unique when set), notes, status (Completed / Cancelled), who and when. **OrderLines** keep a snapshot of item and category names so old orders and receipts never change when an item is renamed.
- **StockMovements**: every quantity change (Initial, Restock, Order, OrderCancelled, Adjustment) with the change, the quantity after, the user and the order. Stock can always be explained line by line.

Nothing that orders reference is hard-deleted: users, items and customers are disabled instead, so the log stays complete.

## Permissions

| Permission | Allows |
|---|---|
| `ViewStock` | Categories, items, quantities, stock history, dashboard |
| `ManageCustomers` | Add / edit / disable customers |
| `CreateOrders` | New Order |
| `AddStock` | Add Quantity to existing items |
| `ViewOrders` | Orders log, search, filters |
| `CancelOrders` | Cancel an order (returns its stock) |
| `ManageCatalog` | Add / edit categories and items, stock-count adjustments |
| `ManageUsers` | Add users, set permissions, reset passwords |

`StandardUser` = ViewStock + ManageCustomers + CreateOrders + AddStock + ViewOrders (the home page). Admins have everything. Permissions are checked against the database on every request, so a change or a disabled account applies immediately on every PC.

## API overview

All endpoints except login and health need `Authorization: Bearer <token>`. Errors are RFC 7807 `ProblemDetails` with a readable `title` the app can show as-is. Enums are sent as strings.

| Method | Path | Permission | Purpose |
|---|---|---|---|
| POST | `/api/auth/login` | — | Email + password → token, expiry, user + permissions |
| GET | `/api/auth/me` | any | Current user |
| POST | `/api/auth/change-password` | any | Change own password |
| GET/POST | `/api/users` | ManageUsers | List / add user |
| GET/PUT/DELETE | `/api/users/{id}` | ManageUsers | Get / edit (permissions, admin, active) / disable |
| POST | `/api/users/{id}/reset-password` | ManageUsers | Set a new password |
| GET | `/api/categories` | ViewStock | With item count and total units |
| POST/PUT/DELETE | `/api/categories[/{id}]` | ManageCatalog | Delete removes an empty category, else disables it |
| GET | `/api/items?categoryId=&search=&inStockOnly=&lowStockOnly=` | ViewStock | Items with available quantity (New Order picker) |
| POST/PUT/DELETE | `/api/items[/{id}]` | ManageCatalog | Add (with initial quantity) / edit details / disable |
| POST | `/api/items/{id}/add-quantity` | AddStock | Add received stock |
| POST | `/api/items/{id}/adjust` | ManageCatalog | Set counted quantity, reason required |
| GET | `/api/customers?search=` | any | Customer picker |
| POST/PUT/DELETE | `/api/customers[/{id}]` | ManageCustomers | Add / edit / disable |
| POST | `/api/orders` | CreateOrders | Create order, deduct stock (see below) |
| GET | `/api/orders` | ViewOrders | Orders log (filters below), paged |
| GET | `/api/orders/{id}` | ViewOrders | One order with lines |
| GET | `/api/orders/by-receipt/{receiptNumber}` | ViewOrders | Exact receipt lookup |
| POST | `/api/orders/{id}/cancel` | CancelOrders | Cancel and return stock |
| GET | `/api/stock-movements?itemId=&categoryId=&type=&userId=&from=&to=` | ViewStock | Stock history, paged |
| GET | `/api/dashboard/summary?utcOffsetMinutes=180` | ViewStock | Home page numbers and low-stock list |
| GET | `/api/health` | — | Server + DB check (for the app's connection screen) |

### Creating an order

```json
POST /api/orders
{
  "customerId": 3,
  "withReceipt": true,
  "receiptNumber": "R-10045",
  "notes": "Delivered by Ahmed",
  "lines": [ { "itemId": 7, "quantity": 2 }, { "itemId": 9, "quantity": 1 } ]
}
```

- `201` returns the full order (`orderNumber`, lines, customer, who created it).
- `400` if `withReceipt` is true without a receipt number, or an item/customer is disabled.
- `409` if the receipt number is already used, or stock is short. A shortage response lists every short item, and **nothing is taken**:
  ```json
  { "status": 409, "title": "Not enough stock for one or more items. Nothing was taken.",
    "shortages": [ { "itemId": 9, "itemName": "Xpass", "requested": 3, "available": 2 } ] }
  ```

**Concurrency:** the order runs in one transaction, and each item is decremented with a single conditional `UPDATE Items SET Quantity = Quantity - @n WHERE Id = @id AND Quantity >= @n`, in item-id order. SQL Server row locks make the check and the decrement one step, so two PCs can never take the last unit twice, and the fixed order prevents deadlocks. A test fires 12 simultaneous one-unit orders from 3 clients at an item with 5 units and checks exactly 5 succeed.

### Orders log filters (`GET /api/orders`)

All optional, combined with AND: `search` (order no., receipt no., customer name / tax no., item name / code, notes), `from`, `to` (ISO 8601, e.g. `2026-10-01T00:00:00+03:00`), `customerId`, `categoryId`, `itemId`, `createdByUserId`, `withReceipt` (true/false), `receiptNumber` (partial), `orderNumber` (partial), `status` (`Completed`/`Cancelled`), `sort` (`NewestFirst`, `OldestFirst`, `CustomerName`, `ReceiptNumber`, `TotalQuantity`), `page`, `pageSize` (max 200).

Response: `{ items: OrderDto[], page, pageSize, totalCount, totalPages }`.
