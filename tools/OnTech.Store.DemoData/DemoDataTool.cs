using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Data;
using OnTech.Store.Api.Services;
using OnTech.Store.Contracts;

namespace OnTech.Store.DemoData;

/// <summary>
/// Makes a demo copy of a store database filled with made-up stock-ins and orders from 1 January to now,
/// so the app (item cards, logs, filters) can be tried on a year of history. The real database is only read, never changed.
///
///   dotnet run --project tools/OnTech.Store.DemoData -- --source src/OnTech.Store.Api/ontech-store-dev.db
///
/// Writes ontech-store-demo.db next to the source (or --out PATH), and the source is only read.
/// With --in-place it fills the source itself instead, after saving a backup next to it (<db>-backup-before-demo-<time>.db);
/// stop the server first. To undo, stop the server and copy the backup back over the database.
/// Everything made up is marked: voucher numbers start with DEMO-, the suppliers are named "(تجريبي)", and notes say so.
/// </summary>
public static class DemoDataTool
{
    public const string SeedName = "DemoData";
    private const string Note = "بيانات تجريبية";

    public static async Task<int> Main(string[] args)
    {
        string? Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
        var source = Arg("--source");
        var from = DateTime.TryParse(Arg("--from"), out var f) ? f.Date : new DateTime(DateTime.Today.Year, 1, 1);
        var seed = int.TryParse(Arg("--seed"), out var s) ? s : 2026;
        var overwrite = args.Contains("--overwrite");
        var inPlace = args.Contains("--in-place");

        if (source is null || !File.Exists(source))
        {
            Console.Error.WriteLine("Usage: OnTechStoreDemoData --source <store .db> [--out <demo .db> | --in-place] [--from 2026-01-01] [--seed 2026] [--overwrite]");
            Console.Error.WriteLine(source is null ? "--source is required." : $"Not found: {source}");
            return 2;
        }
        string output;
        if (inPlace)
        {
            output = Path.GetFullPath(source);
            var backup = Path.Combine(Path.GetDirectoryName(output)!, $"{Path.GetFileNameWithoutExtension(output)}-backup-before-demo-{DateTime.Now:yyyyMMdd-HHmmss}.db");
            CopyDatabase(output, backup);
            Console.WriteLine($"Backed up {output} -> {backup}");
        }
        else
        {
            output = Path.GetFullPath(Arg("--out") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source))!, "ontech-store-demo.db"));
            if (string.Equals(output, Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("--out must be a different file from --source (use --in-place to fill the source itself).");
                return 2;
            }
            if (File.Exists(output))
            {
                if (!overwrite)
                {
                    Console.Error.WriteLine($"{output} already exists. Add --overwrite to replace it (only the demo copy is replaced).");
                    return 2;
                }
                SqliteConnection.ClearAllPools();
                File.Delete(output);
            }
            CopyDatabase(source, output);
            Console.WriteLine($"Copied {source} -> {output}");
        }

        var options = new DbContextOptionsBuilder<StoreDbContext>().UseSqlite($"Data Source={output}").Options;
        await using var db = new StoreDbContext(options);
        var result = await new Generator(db, new Random(seed), from, DateTime.Now).RunAsync();
        Console.WriteLine(result);
        Console.WriteLine(inPlace ? "Done. Start the server again." : "Done. Run the server on the demo copy to try it.");
        return 0;
    }

    /// <summary>A consistent copy, even while the real server has the file open. The source is opened read only.</summary>
    public static void CopyDatabase(string source, string output)
    {
        using var src = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        using var dst = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = output, Pooling = false }.ToString());
        src.Open();
        dst.Open();
        src.BackupDatabase(dst);
    }

    /// <summary>Fills an open demo database. Stock never goes below zero, and every item ends with at least what it started with.</summary>
    public sealed class Generator(StoreDbContext db, Random rng, DateTime fromLocal, DateTime untilLocal)
    {
        private readonly Dictionary<int, int> _balance = new();
        private readonly Dictionary<int, int> _opening = new();
        private List<User> _users = new();
        private List<Item> _items = new();
        private List<Item> _busy = new();
        private List<Customer> _customers = new();
        private List<Supplier> _suppliers = new();
        private int _issue, _receipt, _voucher, _invoice;
        private int _orders, _stockIns, _cancelledOrders, _cancelledStockIns;

        public async Task<string> RunAsync()
        {
            if (await db.AppliedSeeds.AnyAsync(a => a.Name == SeedName))
                throw new InvalidOperationException("This database already has demo data.");

            _users = await db.Users.Where(u => u.IsActive).OrderBy(u => u.Id).ToListAsync();
            _items = await db.Items.Include(i => i.Category).Where(i => i.IsActive && i.Category.IsActive).OrderBy(i => i.Id).ToListAsync();
            _customers = await db.Customers.Where(c => c.IsActive).OrderBy(c => c.Id).ToListAsync();
            if (_users.Count == 0 || _items.Count == 0) throw new InvalidOperationException("The database needs at least one user and one item.");
            if (_customers.Count == 0) _customers = await AddDemoCustomersAsync();

            // The opening balances count from the first day of the demo period.
            var start = ToUtc(fromLocal.Date.AddHours(8));
            await db.StockMovements.Where(m => m.Type == StockMovementType.Initial).ExecuteUpdateAsync(u => u.SetProperty(m => m.CreatedAt, start));

            // Made-up history stops before anything really saved after the opening data, so those stay valid on top of it.
            var firstReal = await db.StockMovements.Where(m => m.Type != StockMovementType.Initial).MinAsync(m => (DateTime?)m.CreatedAt);
            var until = ToUtc(untilLocal);
            if (firstReal is { } fr && fr < until) until = fr.AddMinutes(-1);

            foreach (var i in _items) _balance[i.Id] = _opening[i.Id] = i.Quantity;
            _busy = _items.OrderBy(_ => rng.Next()).Take(Math.Min(80, _items.Count)).ToList();
            _customers = _customers.OrderBy(_ => rng.Next()).Take(Math.Min(150, _customers.Count)).ToList();
            _suppliers = await AddDemoSuppliersAsync();

            var pendingCancels = new List<(DateTime DueLocalDay, Order Order)>();
            for (var day = fromLocal.Date; ToUtc(day.AddHours(8)) < until; day = day.AddDays(1))
            {
                // Friday off, Saturday quiet.
                var (stockIns, orders) = day.DayOfWeek switch
                {
                    DayOfWeek.Friday => (0, rng.Next(0, 2)),
                    DayOfWeek.Saturday => (rng.Next(0, 2), rng.Next(1, 5)),
                    _ => (rng.Next(0, 4), rng.Next(4, 13)),
                };
                var kinds = Enumerable.Repeat(true, stockIns).Concat(Enumerable.Repeat(false, orders)).OrderBy(_ => rng.Next()).ToList();
                // Morning deliveries first, so the day's orders have stock.
                kinds = kinds.OrderBy(k => k ? rng.Next(0, 3) : rng.Next(1, 6)).ToList();
                var slot = TimeSpan.FromMinutes(Math.Max(30, 510 / Math.Max(1, kinds.Count)));
                var time = day.AddHours(8.5);
                foreach (var isStockIn in kinds)
                {
                    time = time.Add(slot).AddMinutes(rng.Next(-5, 6));
                    if (ToUtc(time) >= until) break;
                    if (isStockIn) AddStockIn(time);
                    else if (AddOrder(time) is { } order && rng.NextDouble() < 0.03) pendingCancels.Add((day.AddDays(rng.Next(0, 3)), order));
                }

                foreach (var due in pendingCancels.Where(p => p.DueLocalDay <= day).ToList())
                {
                    var at = day.AddHours(17.5);
                    if (ToUtc(at) < until) CancelOrder(due.Order, at);
                    pendingCancels.Remove(due);
                }

                // Every item ends the period with at least its opening stock, so real movements saved later stay valid.
                if (day.Day == 1 || ToUtc(day.AddDays(1).AddHours(8)) >= until) await SaveAsync();
            }

            var topUp = _items.Where(i => _balance[i.Id] < _opening[i.Id]).ToList();
            if (topUp.Count > 0) AddStockIn(ToLocal(until.AddMinutes(-30)), topUp.Select(i => (i, _opening[i.Id] - _balance[i.Id])).ToList());

            db.AppliedSeeds.Add(new AppliedSeed { Name = SeedName, AppliedAt = DateTime.UtcNow });
            await SaveAsync();
            await RecomputeBalancesAsync();
            return $"Added {_stockIns} stock-ins ({_cancelledStockIns} cancelled) and {_orders} orders ({_cancelledOrders} cancelled), " +
                   $"{fromLocal:d MMM yyyy} to {ToLocal(until):d MMM yyyy HH:mm}, from {_suppliers.Count} demo suppliers to {_customers.Count} customers.";
        }

        private User AnyUser() => _users[rng.Next(_users.Count)];

        private Item PickItem() => rng.NextDouble() < 0.1 ? _items[rng.Next(_items.Count)] : _busy[(int)(Math.Pow(rng.NextDouble(), 1.6) * _busy.Count)];

        // A few customers buy often, most now and then.
        private Customer PickCustomer() => _customers[(int)(Math.Pow(rng.NextDouble(), 2.2) * _customers.Count)];

        private void AddStockIn(DateTime local, List<(Item Item, int Quantity)>? lines = null)
        {
            lines ??= Enumerable.Range(0, rng.Next(1, 5)).Select(_ => PickItem()).Distinct()
                .Select(i => (i, rng.Next(1, 11) * 5)).ToList();
            var user = AnyUser();
            var at = ToUtc(local);
            var stockIn = new StockIn
            {
                StockInNumber = "PENDING", Supplier = _suppliers[(int)(Math.Pow(rng.NextDouble(), 1.4) * _suppliers.Count)],
                VoucherNumber = $"DEMO-A-{++_voucher:D5}", SupplierInvoiceNumber = rng.NextDouble() < 0.7 ? $"DEMO-INV-{++_invoice:D5}" : null,
                Notes = Note, Status = OrderStatus.Completed, CreatedByUser = user, CreatedAt = at,
            };
            foreach (var (item, qty) in lines)
            {
                stockIn.Lines.Add(new StockInLine { Item = item, Quantity = qty, ItemName = item.Name, ItemCode = item.Code, CategoryId = item.CategoryId, CategoryName = item.Category.Name });
                _balance[item.Id] += qty;
                db.StockMovements.Add(new StockMovement { Item = item, Type = StockMovementType.StockIn, Change = qty, StockIn = stockIn, User = user, CreatedAt = at });
            }
            db.StockIns.Add(stockIn);
            _stockIns++;

            // Now and then a voucher was typed by mistake and cancelled straight away.
            if (rng.NextDouble() < 0.02)
            {
                var cancelAt = at.AddMinutes(20);
                stockIn.Status = OrderStatus.Cancelled;
                stockIn.CancelledAt = cancelAt;
                stockIn.CancelledByUser = user;
                stockIn.CancelReason = "تجربة: إذن مكتوب بالغلط";
                foreach (var line in stockIn.Lines)
                {
                    _balance[line.Item.Id] -= line.Quantity;
                    db.StockMovements.Add(new StockMovement
                    {
                        Item = line.Item, Type = StockMovementType.StockInCancelled, Change = -line.Quantity, StockIn = stockIn, User = user,
                        Note = stockIn.CancelReason, CreatedAt = cancelAt,
                    });
                }
                _cancelledStockIns++;
            }
        }

        private Order? AddOrder(DateTime local)
        {
            var wanted = Enumerable.Range(0, rng.Next(1, 4)).Select(_ => PickItem()).Distinct().ToList();
            var lines = new List<(Item Item, int Quantity)>();
            foreach (var item in wanted)
            {
                var qty = Math.Min(_balance[item.Id], rng.NextDouble() < 0.85 ? rng.Next(1, 6) : rng.Next(6, 21));
                if (qty > 0) lines.Add((item, qty));
            }
            if (lines.Count == 0) return null;

            var user = AnyUser();
            var at = ToUtc(local);
            var withReceipt = rng.NextDouble() < 0.6;
            var order = new Order
            {
                OrderNumber = "PENDING", Customer = PickCustomer(), IssueVoucherNumber = $"DEMO-S-{++_issue:D5}",
                WithReceipt = withReceipt, ReceiptNumber = withReceipt ? $"DEMO-R-{++_receipt:D5}" : null,
                Notes = Note, Status = OrderStatus.Completed, CreatedByUser = user, CreatedAt = at,
            };
            foreach (var (item, qty) in lines)
            {
                order.Lines.Add(new OrderLine { Item = item, Quantity = qty, ItemName = item.Name, ItemCode = item.Code, CategoryId = item.CategoryId, CategoryName = item.Category.Name });
                _balance[item.Id] -= qty;
                db.StockMovements.Add(new StockMovement { Item = item, Type = StockMovementType.Order, Change = -qty, Order = order, User = user, CreatedAt = at });
            }
            db.Orders.Add(order);
            _orders++;
            return order;
        }

        private void CancelOrder(Order order, DateTime local)
        {
            var user = AnyUser();
            var at = ToUtc(local);
            order.Status = OrderStatus.Cancelled;
            order.CancelledAt = at;
            order.CancelledByUser = user;
            order.CancelReason = "تجربة: العميل لغى الطلب";
            foreach (var line in order.Lines)
            {
                _balance[line.Item.Id] += line.Quantity;
                db.StockMovements.Add(new StockMovement
                {
                    Item = line.Item, Type = StockMovementType.OrderCancelled, Change = line.Quantity, Order = order, User = user,
                    Note = order.CancelReason, CreatedAt = at,
                });
            }
            _cancelledOrders++;
        }

        private async Task SaveAsync()
        {
            await db.SaveChangesAsync();
            foreach (var o in db.ChangeTracker.Entries<Order>().Select(e => e.Entity).Where(o => o.OrderNumber == "PENDING")) o.OrderNumber = StockService.FormatOrderNumber(o.Id);
            foreach (var s in db.ChangeTracker.Entries<StockIn>().Select(e => e.Entity).Where(s => s.StockInNumber == "PENDING")) s.StockInNumber = StockService.FormatStockInNumber(s.Id);
            await db.SaveChangesAsync();
        }

        /// <summary>Walks every item's movements in time order and writes the balance after each one, and the item's quantity.</summary>
        private async Task RecomputeBalancesAsync()
        {
            db.ChangeTracker.Clear();
            var moves = await db.StockMovements.OrderBy(m => m.ItemId).ThenBy(m => m.CreatedAt).ThenBy(m => m.Id).ToListAsync();
            foreach (var group in moves.GroupBy(m => m.ItemId))
            {
                var running = 0;
                foreach (var m in group)
                {
                    running += m.Change;
                    if (running < 0) throw new InvalidOperationException($"Item {m.ItemId} would go below zero at movement {m.Id}.");
                    m.QuantityAfter = running;
                }
                var item = await db.Items.FindAsync(group.Key);
                if (item is not null && item.Quantity != running)
                {
                    item.Quantity = running;
                    item.UpdatedAt = DateTime.UtcNow;
                }
            }
            await db.SaveChangesAsync();
        }

        private async Task<List<Supplier>> AddDemoSuppliersAsync()
        {
            string[] names =
            [
                "الشركة المصرية للتوريدات (تجريبي)", "النيل للأنظمة الأمنية (تجريبي)", "دلتا كارد سيستمز (تجريبي)", "الأهرام للتكنولوجيا (تجريبي)",
                "Global ID Supplies (demo)", "Smart Access Trading (demo)", "كايرو برينت (تجريبي)", "الإسكندرية للإلكترونيات (تجريبي)",
            ];
            var next = (await db.Suppliers.MaxAsync(x => x.SupplierNumber) ?? 0) + 1;
            var list = names.Select(n => new Supplier
            {
                SupplierNumber = next++, CompanyName = n, Notes = Note, CreatedAt = ToUtc(fromLocal), CreatedByUserId = _users[0].Id,
            }).ToList();
            db.Suppliers.AddRange(list);
            await db.SaveChangesAsync();
            return list;
        }

        private async Task<List<Customer>> AddDemoCustomersAsync()
        {
            var list = Enumerable.Range(1, 40).Select(n => new Customer
            {
                CustomerNumber = 9000 + n, CompanyName = $"عميل تجريبي {n}", Notes = Note, CreatedAt = ToUtc(fromLocal), CreatedByUserId = _users[0].Id,
            }).ToList();
            db.Customers.AddRange(list);
            await db.SaveChangesAsync();
            return list;
        }

        private static DateTime ToUtc(DateTime local) => DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
        private static DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
    }
}
