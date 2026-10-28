# OnTech Store

Internal stock app for OnTech: categories and items with quantities, customers, and orders that take stock out of the store, with an optional receipt number. This repository holds the server (database + REST API) and the WPF desktop app that staff use on up to 3 PCs.

## Layout

| Path | What it is |
|---|---|
| `src/OnTech.Store.Api` | ASP.NET Core 8 Web API, EF Core, JWT login. Runs as a Windows service. |
| `src/OnTech.Store.Contracts` | DTOs and enums shared with the desktop app (`Permission`, `OrderQuery`, `CreateOrderRequest`, ...). |
| `src/OnTech.Store.Client` | Typed HTTP client for the API (`StoreApiClient`), used by the desktop app. Turns server errors into readable messages. |
| `src/OnTech.Store.Desktop` | WPF (.NET 8) desktop app, `OnTechStore.exe`. MVVM with CommunityToolkit.Mvvm. |
| `tests/OnTech.Store.Api.Tests` | End-to-end API tests (in-memory host + SQLite), including a 3-PC concurrency test, plus tests of the desktop app's API client against the real server. `FeatureCoverageTests` goes through every feature: the permission matrix, bad input, the opening data, the orders log and 3-PC races. |
| `tools/OnTech.Store.LoadTest` | Load test: several PCs using a running server at once, with timings per screen and a stock check at the end. |
| `db/OnTechStore.sql` | Idempotent SQL script of the schema, for review or manual setup. |
| `docs/swagger.json` | OpenAPI spec snapshot. The live, clickable version is at `/swagger` on the server. |
| `scripts/` | `install-service.ps1` (publish + install Windows service + firewall), `backup-database.ps1`. |

## Deployment (server PC)

Hardware: any normal office PC is enough for 3 clients. The API idles at roughly 140 MB RAM and stays around 300 MB with 3 PCs working (see *Load test* below); SQL Server Express is capped by Microsoft at 1.4 GB buffer memory and 10 GB per database, which is years of orders for a store this size.

1. Install the [.NET 8 ASP.NET Core Runtime (Hosting Bundle)](https://dotnet.microsoft.com/download/dotnet/8.0), **SQL Server Express** (instance name `SQLEXPRESS`), and `sqlcmd`.
2. Give the server PC a fixed IP address on the office network (or a DHCP reservation).
3. From an elevated PowerShell in the repo root (needs the .NET 8 SDK on the machine that publishes):
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\install-service.ps1
   ```
   This publishes to `C:\OnTechStore\Server`, generates a unique JWT signing key, installs the **OnTech Store Server** service (auto start, auto restart on crash), grants the service account access to SQL Express, and opens TCP 5080 on private/domain networks.
4. Open `http://localhost:5080/api/health`. On first start the server creates the database and the first admin:
   - Username `admin`, password `ChangeMe!123` (change in `appsettings.json` under `Seed` before first start, or change the password after first login; the account is flagged to change it).
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

## Desktop app (client PCs)

1. Install the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64) on each PC.
2. Build it once (on any machine with the .NET 8 SDK):
   ```powershell
   dotnet publish src\OnTech.Store.Desktop -c Release -r win-x64 --self-contained false -o publish\desktop
   ```
   Copy the `publish\desktop` folder to each PC (about 1 MB) and make a shortcut to `OnTechStore.exe`.
3. On first start, type the server PC's address on the sign-in screen (for example `192.168.1.10`; port 5080 is added for you), press **Test**, then sign in. The address and last username are remembered per PC in `%LocalAppData%\OnTechStore\settings.json`.
4. The app speaks English and Arabic. The **العربية / English** link on the sign-in screen and at the bottom of the sidebar switches language; Arabic turns the whole layout right-to-left. The choice is remembered per PC (`"Language": "en"` or `"ar"` in the same settings file).

Translations live in `src/OnTech.Store.Desktop/Services/Arabic.cs`, keyed by the English text. Screens use `{l:T 'English text'}` in XAML and `L.T("English text")` in code; anything without an Arabic entry shows in English. Server messages are translated there too (fixed ones by exact text, ones with names or numbers by pattern).

What each screen does:

| Screen | Who sees it | What it does |
|---|---|---|
| Sign in | everyone | Server address, username, password. Users flagged by an admin must pick a new password first. |
| Home | everyone | Greeting, the three big actions (New Order, Stock In, Add Customer), today's numbers, and the stock table with In stock / Low / Out of stock. Double-click an item to order it. |
| New Order | `CreateOrders` | Category → item (shows what's available) → quantity → customer, the **issue voucher number** (إذن صرف, required and unique), optional receipt number and notes. "Add another item" puts several items in one order. Shows how many are left after the order. If another PC took the stock first, the app says which item is short and refreshes the numbers. |
| Stock In | `AddStock` | Stock received from a supplier: category → item → quantity → supplier, the **receipt voucher number** (إذن إضافة, required and unique), optional supplier invoice number and notes. Several items per voucher. This and New Order are the only ways a quantity changes. |
| Customers | everyone (edit needs `ManageCustomers`) | Search by customer number or name, add, edit, disable. Company name is the only required field; new customers get the next free number. **Import from Excel** adds customers from an `.xlsx`/`.csv` sheet (see below). |
| Suppliers | everyone (edit needs `ManageSuppliers`) | Same as Customers (number, company name, phone, tax number, ...), without the import. |
| Orders Log | `ViewOrders` | Search (also by issue voucher), with/without receipt filter, paging, order details, cancel (needs `CancelOrders`). The full log with every filter and receipt PDFs is the next step. |
| Stock In Log | `ViewOrders` | Every stock-in, newest first: search by number, voucher, supplier invoice, supplier or item; details; cancel (needs `CancelOrders`, refused if that stock was already issued). |
| Items & Categories | `ManageCatalog` (admin menu) | Add/edit categories and items with a low-stock level, disable. New items start at 0; quantities can't be typed in here. |
| Users | `ManageUsers` (admin menu) | Add users with a username + first password, admin flag or individual permissions, reset passwords, disable. |

The menu only shows what the signed-in user is allowed to do, and the server checks every permission again, so a hidden button is never the only protection.

## Development

```bash
dotnet test                       # end-to-end API + client tests on SQLite
# same tests on a real SQL Server, through the migrations (database name is added per test run):
ONTECH_TEST_SQLSERVER="Server=localhost;User Id=sa;Password=...;TrustServerCertificate=True" dotnet test
cd src/OnTech.Store.Api
ASPNETCORE_ENVIRONMENT=Development dotnet run   # uses a local SQLite file, no SQL Server needed
# open http://localhost:5080/swagger
```

**Upgrading a SQLite database.** On startup the server adds any new tables and columns to an existing SQLite file. Before a change it first saves a full copy next to the file, named like `ontech-store-dev-backup-before-suppliers-20261028-153000.db`, and logs where it went. To go back, stop the server and put that copy in place of the `.db` file. SQL Server databases are upgraded by the EF migrations instead; take a backup with `scripts\backup-database.ps1` before updating.

### Load test

`tools/OnTech.Store.LoadTest` acts like several store PCs working at the same time against a running server, through the same API client as the desktop app: home page, stock list, New Order pickers, customer search by name and number, orders log, saving orders (some with receipts), stock-ins from a supplier and cancelling orders. At the end it prints the time each call took (average, p50, p95, p99, max), any errors, and checks that every unit taken, added or returned adds up on the server.

```bash
dotnet run -c Release --project tools/OnTech.Store.LoadTest -- --server 192.168.1.10 --user admin --password <password> --pcs 3 --seconds 60 --think-ms 300
```

It only orders and restocks a "Load test ..." category, items and customer that it makes for itself and disables at the end, but those orders stay in the orders log, so run it on a test copy of the server when you can. Exit code 0 means PASS.

Results on 2026-10-28 (4-core Linux container, SQLite, Release build, opening data loaded): 3 PCs with a short pause between clicks made ~10 calls/second, every screen answered in under 10 ms at the median and under 20 ms at p95 (Save order p95 16 ms), with no errors and an exact stock check. A stress run of 10 PCs with no pause at all made ~400 calls/second, still with no errors and an exact stock check; reads stayed under 25 ms at p95, saves queued for SQLite's single writer (p95 ≈ 300 ms). SQL Server locks per row, so writes there should do better.

The desktop app is built with Visual Studio 2022 or `dotnet build src/OnTech.Store.Desktop` and runs on Windows only (the project sets `EnableWindowsTargeting` so it also compiles on Linux CI). Run the API in Development mode, start the app, and use `localhost` as the server address.

Schema changes: edit `Data/Entities.cs` / `StoreDbContext.cs`, then
`dotnet ef migrations add <Name> -o Data/Migrations` from `src/OnTech.Store.Api`. The server applies pending migrations on startup.

## Data model

```
Users ─┐                          Categories 1─* Items ──* StockMovements
       │ creates                                  │            │
Customers 1─* Orders 1─* OrderLines *─────────────┤            │
                 └──────────────────────────────────────────────┤ (order movements)
Suppliers 1─* StockIns 1─* StockInLines *─────────┘            │
                 └──────────────────────────────────────────────┘ (stock-in movements)
```

- **Users**: name, username (unique, not case-sensitive, letters/digits/`.`/`-`/`_`), PBKDF2 password hash, `IsAdmin`, `Permissions` bit mask, `IsActive`, `MustChangePassword`.
- **Categories**: unique name, active flag. **Items**: category, name (unique per category), optional code/model, `Quantity` (DB check: never below 0), `LowStockThreshold`.
- **Customers**: `CustomerNumber` (unique, next free number when left empty), `CompanyName` (required, unique when added by hand; an import keeps two same-name rows that have different numbers), phone, tax number (unique when set), contact person, email, address, notes.
- **Orders**: `ORD-000123` number, customer, `IssueVoucherNumber` (إذن صرف, required on new orders, unique; empty on orders saved before it existed), `WithReceipt` + `ReceiptNumber` (unique when set), notes, status (Completed / Cancelled), who and when. **OrderLines** keep a snapshot of item and category names so old orders and receipts never change when an item is renamed.
- **Suppliers**: same fields as customers, with `SupplierNumber`.
- **StockIns**: `IN-000012` number, supplier, `VoucherNumber` (إذن إضافة, required, unique), optional supplier invoice number, notes, status, who and when. **StockInLines** keep the same snapshots as order lines.
- **StockMovements**: every quantity change (StockIn, StockInCancelled, Order, OrderCancelled; Initial for the opening data; Restock and Adjustment only in history from before stock-ins) with the change, the quantity after, the user and the order or stock-in. Stock can always be explained line by line.

Nothing that orders reference is hard-deleted: users, items and customers are disabled instead, so the log stays complete.

## Permissions

| Permission | Allows |
|---|---|
| `ViewStock` | Categories, items, quantities, stock history, dashboard |
| `ManageCustomers` | Add / edit / disable customers |
| `ManageSuppliers` | Add / edit / disable suppliers |
| `CreateOrders` | New Order |
| `AddStock` | Stock In from a supplier |
| `ViewOrders` | Orders log and stock-in log, search, filters |
| `CancelOrders` | Cancel an order (returns its stock) or a stock-in (takes its stock back out) |
| `ManageCatalog` | Add / edit categories and items |
| `ManageUsers` | Add users, set permissions, reset passwords |

`StandardUser` = ViewStock + ManageCustomers + ManageSuppliers + CreateOrders + AddStock + ViewOrders (the home page). When suppliers were added, everyone who had ManageCustomers was given ManageSuppliers. Admins have everything. Permissions are checked against the database on every request, so a change or a disabled account applies immediately on every PC.

## API overview

All endpoints except login and health need `Authorization: Bearer <token>`. Errors are RFC 7807 `ProblemDetails` with a readable `title` the app can show as-is. Enums are sent as strings.

| Method | Path | Permission | Purpose |
|---|---|---|---|
| POST | `/api/auth/login` | — | Username + password → token, expiry, user + permissions |
| GET | `/api/auth/me` | any | Current user |
| POST | `/api/auth/change-password` | any | Change own password |
| GET/POST | `/api/users` | ManageUsers | List / add user |
| GET/PUT/DELETE | `/api/users/{id}` | ManageUsers | Get / edit (permissions, admin, active) / disable |
| POST | `/api/users/{id}/reset-password` | ManageUsers | Set a new password |
| GET | `/api/categories` | ViewStock | With item count and total units |
| POST/PUT/DELETE | `/api/categories[/{id}]` | ManageCatalog | Delete removes an empty category, else disables it |
| GET | `/api/items?categoryId=&search=&inStockOnly=&lowStockOnly=` | ViewStock | Items with available quantity (New Order picker) |
| GET | `/api/items/{id}/card?from=&to=` | ViewStock | Item card: every movement in/out, oldest first, with customer or supplier, voucher, order/stock-in number and balance after each; with a period, the balance before it and the top 5 customers |
| POST/PUT/DELETE | `/api/items[/{id}]` | ManageCatalog | Add (starts at 0) / edit details / disable. Quantity is never set directly |
| GET | `/api/customers?search=` | any | Customer picker. A number matches that customer number exactly; text matches name, phone, tax number, contact |
| POST/PUT/DELETE | `/api/customers[/{id}]` | ManageCustomers | Add / edit / disable |
| POST | `/api/customers/import` | ManageCustomers | Bulk add `{ rows: [{ customerNumber, companyName, phoneNumber, taxNumber }] }`. Rows whose number already exists, or with no name, are skipped and listed in `messages`, so importing the same sheet twice is safe |
| GET | `/api/suppliers?search=` | any | Supplier picker, same matching as customers |
| POST/PUT/DELETE | `/api/suppliers[/{id}]` | ManageSuppliers | Add / edit / disable |
| POST | `/api/stock-ins` | AddStock | Stock in from a supplier `{ supplierId, voucherNumber, supplierInvoiceNumber?, notes?, lines }`, adds stock. `409` if the voucher number is used |
| GET | `/api/stock-ins?search=&from=&to=&supplierId=&itemId=&status=` | ViewOrders | Stock-in log, paged |
| GET | `/api/stock-ins/{id}` | ViewOrders | One stock-in with lines |
| POST | `/api/stock-ins/{id}/cancel` | CancelOrders | Cancel and take the stock back out; `409` with `shortages` if it was already issued |
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
  "issueVoucherNumber": "S-2051",
  "withReceipt": true,
  "receiptNumber": "R-10045",
  "notes": "Delivered by Ahmed",
  "lines": [ { "itemId": 7, "quantity": 2 }, { "itemId": 9, "quantity": 1 } ]
}
```

- `201` returns the full order (`orderNumber`, lines, customer, who created it).
- `400` if the issue voucher number is missing, `withReceipt` is true without a receipt number, or an item/customer is disabled.
- `409` if the issue voucher or receipt number is already used, or stock is short. A shortage response lists every short item, and **nothing is taken**:
  ```json
  { "status": 409, "title": "Not enough stock for one or more items. Nothing was taken.",
    "shortages": [ { "itemId": 9, "itemName": "Xpass", "requested": 3, "available": 2 } ] }
  ```

**Concurrency:** the order runs in one transaction, and each item is decremented with a single conditional `UPDATE Items SET Quantity = Quantity - @n WHERE Id = @id AND Quantity >= @n`, in item-id order. SQL Server row locks make the check and the decrement one step, so two PCs can never take the last unit twice, and the fixed order prevents deadlocks. A test fires 12 simultaneous one-unit orders from 3 clients at an item with 5 units and checks exactly 5 succeed.

### Orders log filters (`GET /api/orders`)

All optional, combined with AND: `search` (order no., issue voucher no., receipt no., customer name / tax no., item name / code, notes), `from`, `to` (ISO 8601, e.g. `2026-10-01T00:00:00+03:00`), `customerId`, `categoryId`, `itemId`, `createdByUserId`, `withReceipt` (true/false), `receiptNumber` (partial), `orderNumber` (partial), `issueVoucherNumber` (partial), `status` (`Completed`/`Cancelled`), `sort` (`NewestFirst`, `OldestFirst`, `CustomerName`, `ReceiptNumber`, `TotalQuantity`), `page`, `pageSize` (max 200).

Response: `{ items: OrderDto[], page, pageSize, totalCount, totalPages }`.

## Importing customers

On the Customers page, **Import from Excel** reads the first sheet (or a sheet named *Customers*) of an `.xlsx` or `.csv` file. With a header row, columns are found by name (Number / No / Code, Name / Company, Phone, Tax, in English or Arabic). Without a header, the column of whole numbers is the customer number and the column of text is the company name, which is how OnTech's old customer sheet is laid out. The app shows how many customers it found and asks before importing.

## Opening data

`src/OnTech.Store.Api/Data/Seed/opening-data.json` holds OnTech's stock (11 categories, 318 items with their balances) and 505 customers, taken from the old Excel sheets on 2026-10-28. The server loads it once on startup into any database (new or existing): categories and items that already exist by name, and customers whose number or name is already there (for example after Import from Excel), are left alone. Every item with a balance gets an *Initial* stock movement. The load is recorded in the `AppliedSeeds` table, so restarting the server, or later edits and orders, never bring it back. Cleanups made while converting: `!A1` link leftovers and extra spaces removed from names, the two "DURAGRD LAMINATE" rows in Data card merged, and "palstic box" (−2 in the sheet) set to 0.

## Codes

Codes are digits only. Categories get `01`, `02`, ... and items get their category's code followed by a three-digit number, e.g. `04001`, so every item code is unique across all categories. Leave the code box empty when adding a category or item to get the next one, or type your own digits; a code already in use is refused. An item keeps its code if it moves to another category. The opening data is coded in the sheet's order (Data card = 01 ... SUPREMA = 11), and any category or item still without a code gets one on startup. Older databases with letter codes (`C04`, `C04-001`) are switched to the digit codes (`04`, `04001`) on startup, including the codes saved on past orders. Item search matches codes from their start, so `04` lists the Fargo items and `04001` finds exactly one item.

