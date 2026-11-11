using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Data;
using OnTech.Store.Api.Infrastructure;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Services;

/// <summary>
/// The only place that changes item quantities: an order (stock out to a customer, as a sale or a demo) or a stock-in voucher
/// (stock in from a supplier, or a demo coming back from a customer), plus cancelling either one. Quantities are never typed in directly. Each change is one database transaction, and every
/// decrease is a single conditional UPDATE ("... WHERE Quantity >= @n"), so two PCs ordering the same
/// item at the same moment can never take more than is in stock: the second one gets a clear 409.
/// </summary>
public sealed class StockService(StoreDbContext db)
{
    public async Task<Order> CreateOrderAsync(CreateOrderRequest req, int userId, CancellationToken ct)
    {
        var voucher = req.IssueVoucherNumber.CleanDocNumber() ?? throw ApiException.BadRequest("Issue voucher number is required.");
        var receipt = req.ReceiptNumber.CleanDocNumber();
        if (req.WithReceipt && receipt is null)
            throw ApiException.BadRequest("Receipt number is required when the order is made with a receipt.");
        if (!req.WithReceipt) receipt = null;
        if (!Enum.IsDefined(req.Type)) throw ApiException.BadRequest("Unknown issue voucher type.");
        if (req.Type == IssueType.Demo && receipt is not null)
            throw ApiException.BadRequest("A demo can't have a receipt. Receipts are for sales.");

        var wanted = Combine(req.Lines);
        if (wanted.Count == 0) throw ApiException.BadRequest("An order needs at least one item.");

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == req.CustomerId, ct)
                       ?? throw ApiException.NotFound("Customer");
        if (!customer.IsActive) throw ApiException.BadRequest("This customer is disabled.");

        if (await db.Orders.AnyAsync(o => o.IssueVoucherNumber == voucher, ct))
            throw ApiException.Duplicate($"Issue voucher number '{voucher}' is already used by another order.");
        if (receipt is not null && await db.Orders.AnyAsync(o => o.ReceiptNumber == receipt, ct))
            throw ApiException.Duplicate($"Receipt number '{receipt}' is already used by another order.");

        var items = await LoadItemsAsync(wanted.Select(w => w.ItemId), "'{0}' is disabled and cannot be ordered.", ct);

        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var order = new Order
        {
            CustomerId = customer.Id,
            IssueVoucherNumber = voucher,
            Type = req.Type,
            WithReceipt = req.WithReceipt,
            ReceiptNumber = receipt,
            Notes = req.Notes.Clean(),
            Status = OrderStatus.Completed,
            CreatedByUserId = userId,
            CreatedAt = now,
            OrderNumber = voucher, // the issue voucher number is the order's number
            Lines = wanted.Select(w =>
            {
                var item = items[w.ItemId];
                return new OrderLine
                {
                    ItemId = item.Id,
                    Quantity = w.Quantity,
                    ItemName = item.Name,
                    ItemCode = item.Code,
                    CategoryId = item.CategoryId,
                    CategoryName = item.Category.Name,
                };
            }).ToList(),
        };
        db.Orders.Add(order);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another PC saved the same number between our check and now (unique index).
            throw ApiException.Duplicate(receipt is not null && await db.Orders.AnyAsync(o => o.ReceiptNumber == receipt, ct)
                ? $"Receipt number '{receipt}' is already used by another order."
                : $"Issue voucher number '{voucher}' is already used by another order.");
        }
        // Deduct in a fixed order (by item id) so concurrent orders never deadlock on each other.
        var shortages = new List<object>();
        foreach (var w in wanted.OrderBy(w => w.ItemId))
        {
            var after = await TryChangeQuantityAsync(w.ItemId, -w.Quantity, now, ct);
            if (after is null)
            {
                var available = await db.Items.Where(i => i.Id == w.ItemId).Select(i => i.Quantity).FirstAsync(ct);
                shortages.Add(new { itemId = w.ItemId, itemName = items[w.ItemId].Name, requested = w.Quantity, available });
                continue;
            }
            db.StockMovements.Add(new StockMovement
            {
                ItemId = w.ItemId, Type = StockMovementType.Order, Change = -w.Quantity, QuantityAfter = after.Value,
                OrderId = order.Id, UserId = userId, CreatedAt = now,
            });
        }

        if (shortages.Count > 0)
        {
            await tx.RollbackAsync(ct);
            throw ApiException.Conflict("Not enough stock for one or more items. Nothing was taken.",
                new Dictionary<string, object?> { ["shortages"] = shortages });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return order;
    }

    public async Task CancelOrderAsync(int orderId, string reason, int userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Flip the status atomically first, so an order can only be cancelled (and its stock returned) once.
        // A demo that already has something back can't be cancelled: its returns would point at a cancelled demo.
        var flipped = await db.Orders
            .Where(o => o.Id == orderId && o.Status == OrderStatus.Completed && !o.Lines.Any(l => l.ReturnedQuantity > 0))
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Cancelled)
                .SetProperty(o => o.CancelledAt, now)
                .SetProperty(o => o.CancelledByUserId, userId)
                .SetProperty(o => o.CancelReason, reason.Trim()), ct);
        if (flipped == 0)
        {
            var status = await db.Orders.Where(o => o.Id == orderId).Select(o => (OrderStatus?)o.Status).FirstOrDefaultAsync(ct)
                         ?? throw ApiException.NotFound("Order");
            if (status == OrderStatus.Cancelled) throw ApiException.Conflict("This order is already cancelled.");
            throw ApiException.Conflict(DemoHasReturnsMessage);
        }

        var lines = await db.OrderLines.Where(l => l.OrderId == orderId).OrderBy(l => l.ItemId).ToListAsync(ct);
        foreach (var line in lines)
        {
            var after = await TryChangeQuantityAsync(line.ItemId, line.Quantity, now, ct)
                        ?? throw new InvalidOperationException("Returning stock cannot fail.");
            db.StockMovements.Add(new StockMovement
            {
                ItemId = line.ItemId, Type = StockMovementType.OrderCancelled, Change = line.Quantity, QuantityAfter = after,
                OrderId = orderId, UserId = userId, Note = reason.Trim(), CreatedAt = now,
            });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public const string DemoHasReturnsMessage =
        "Some of this demo has already come back, so it can't be cancelled. Cancel its demo return vouchers first.";

    public async Task<StockIn> CreateStockInAsync(CreateStockInRequest req, int userId, CancellationToken ct)
    {
        var voucher = req.VoucherNumber.CleanDocNumber() ?? throw ApiException.BadRequest("Voucher number is required.");
        return req.Type switch
        {
            ReceiveType.Purchase => await CreatePurchaseAsync(req, voucher, userId, ct),
            ReceiveType.DemoReturn => await CreateDemoReturnAsync(req, voucher, userId, ct),
            _ => throw ApiException.BadRequest("Unknown receive voucher type."),
        };
    }

    private async Task<StockIn> CreatePurchaseAsync(CreateStockInRequest req, string voucher, int userId, CancellationToken ct)
    {
        if (req.CustomerId is not null) throw ApiException.BadRequest("A purchase comes from a supplier, not a customer.");
        if (req.Lines.Any(l => l.DemoOrderLineId is not null)) throw ApiException.BadRequest("Only a demo return points at demo lines.");
        var wanted = Combine(req.Lines.Select(l => (l.ItemId, l.Quantity)));
        if (wanted.Count == 0) throw ApiException.BadRequest("A stock-in voucher needs at least one item.");

        var supplierId = req.SupplierId ?? throw ApiException.BadRequest("Choose the supplier.");
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId, ct) ?? throw ApiException.NotFound("Supplier");
        if (!supplier.IsActive) throw ApiException.BadRequest("This supplier is disabled.");
        await EnsureVoucherFreeAsync(voucher, ct);

        var items = await LoadItemsAsync(wanted.Select(w => w.ItemId), "'{0}' is disabled.", ct);

        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var stockIn = NewStockIn(ReceiveType.Purchase, voucher, req, userId, now);
        stockIn.SupplierId = supplier.Id;
        stockIn.SupplierInvoiceNumber = req.SupplierInvoiceNumber.CleanDocNumber();
        stockIn.Lines = wanted.Select(w =>
        {
            var item = items[w.ItemId];
            return new StockInLine
            {
                ItemId = item.Id, Quantity = w.Quantity, ItemName = item.Name, ItemCode = item.Code,
                CategoryId = item.CategoryId, CategoryName = item.Category.Name,
            };
        }).ToList();
        await SaveNewStockInAsync(stockIn, voucher, ct);

        foreach (var w in wanted.OrderBy(w => w.ItemId))
            await AddStockAsync(stockIn, w.ItemId, w.Quantity, userId, now, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return stockIn;
    }

    /// <summary>
    /// A demo coming back. Every line points at a line of a demo of the same customer, and can bring back at most what is still
    /// out on that line. The "still out" check is one conditional UPDATE per line, so two PCs returning the same demo at the same
    /// moment can never bring back more than went out: the second one gets a 409 and nothing is saved.
    /// </summary>
    private async Task<StockIn> CreateDemoReturnAsync(CreateStockInRequest req, string voucher, int userId, CancellationToken ct)
    {
        if (req.SupplierId is not null) throw ApiException.BadRequest("A demo return comes from a customer, not a supplier.");
        var customerId = req.CustomerId ?? throw ApiException.BadRequest("Choose the customer the demo is coming back from.");
        if (req.Lines.Any(l => l.DemoOrderLineId is null))
            throw ApiException.BadRequest("Every line of a demo return must point at the demo line it comes back on.");

        // The same demo line picked twice becomes one line.
        var wanted = req.Lines.GroupBy(l => l.DemoOrderLineId!.Value)
            .Select(g => (DemoLineId: g.Key, ItemIds: g.Select(l => l.ItemId).Distinct().ToList(), Quantity: g.Sum(l => l.Quantity)))
            .ToList();
        if (wanted.Count == 0) throw ApiException.BadRequest("A stock-in voucher needs at least one item.");

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct) ?? throw ApiException.NotFound("Customer");
        var ids = wanted.Select(w => w.DemoLineId).ToList();
        var demoLines = await db.OrderLines.Include(l => l.Order).Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        foreach (var w in wanted)
        {
            if (!demoLines.TryGetValue(w.DemoLineId, out var line)) throw ApiException.NotFound($"Demo line #{w.DemoLineId}");
            if (line.Order.Type != IssueType.Demo) throw ApiException.BadRequest($"Issue voucher '{line.Order.IssueVoucherNumber}' is a sale, not a demo.");
            if (line.Order.Status != OrderStatus.Completed) throw ApiException.BadRequest($"Demo '{line.Order.IssueVoucherNumber}' is cancelled.");
            if (line.Order.CustomerId != customer.Id) throw ApiException.BadRequest($"Demo '{line.Order.IssueVoucherNumber}' went out to another customer.");
            if (w.ItemIds.Any(i => i != line.ItemId)) throw ApiException.BadRequest($"The item doesn't match the demo line for '{line.ItemName}'.");
        }
        await EnsureVoucherFreeAsync(voucher, ct);

        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var stockIn = NewStockIn(ReceiveType.DemoReturn, voucher, req, userId, now);
        stockIn.CustomerId = customer.Id;
        stockIn.Lines = wanted.Select(w =>
        {
            var line = demoLines[w.DemoLineId];
            // The demo line's snapshot: the item may have been renamed or disabled since; it still comes back.
            return new StockInLine
            {
                ItemId = line.ItemId, Quantity = w.Quantity, ItemName = line.ItemName, ItemCode = line.ItemCode,
                CategoryId = line.CategoryId, CategoryName = line.CategoryName, DemoOrderLineId = line.Id,
            };
        }).ToList();
        await SaveNewStockInAsync(stockIn, voucher, ct);

        var tooMuch = new List<object>();
        foreach (var w in wanted.OrderBy(w => w.DemoLineId))
        {
            var n = w.Quantity;
            var marked = await db.OrderLines
                .Where(l => l.Id == w.DemoLineId && l.ReturnedQuantity + n <= l.Quantity && l.Order.Status == OrderStatus.Completed)
                .ExecuteUpdateAsync(u => u.SetProperty(l => l.ReturnedQuantity, l => l.ReturnedQuantity + n), ct);
            if (marked == 0)
            {
                var line = await db.OrderLines.AsNoTracking().Where(l => l.Id == w.DemoLineId)
                    .Select(l => new { l.ItemName, Open = l.Quantity - l.ReturnedQuantity }).FirstAsync(ct);
                tooMuch.Add(new { demoOrderLineId = w.DemoLineId, itemName = line.ItemName, requested = n, open = line.Open });
            }
        }
        if (tooMuch.Count > 0)
        {
            await tx.RollbackAsync(ct);
            throw ApiException.Conflict("You are returning more than the customer still has of one or more items. Nothing was saved.",
                new Dictionary<string, object?> { ["overReturns"] = tooMuch });
        }

        foreach (var line in stockIn.Lines.OrderBy(l => l.ItemId))
            await AddStockAsync(stockIn, line.ItemId, line.Quantity, userId, now, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return stockIn;
    }

    private static StockIn NewStockIn(ReceiveType type, string voucher, CreateStockInRequest req, int userId, DateTime now) => new()
    {
        Type = type,
        VoucherNumber = voucher,
        Notes = req.Notes.Clean(),
        Status = OrderStatus.Completed,
        CreatedByUserId = userId,
        CreatedAt = now,
        StockInNumber = "PENDING",
    };

    private async Task EnsureVoucherFreeAsync(string voucher, CancellationToken ct)
    {
        if (await db.StockIns.AnyAsync(s => s.VoucherNumber == voucher, ct))
            throw ApiException.Duplicate($"Voucher number '{voucher}' is already used by another stock-in.");
    }

    private async Task SaveNewStockInAsync(StockIn stockIn, string voucher, CancellationToken ct)
    {
        db.StockIns.Add(stockIn);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw ApiException.Duplicate($"Voucher number '{voucher}' is already used by another stock-in.");
        }
        stockIn.StockInNumber = FormatStockInNumber(stockIn.Id);
    }

    private async Task AddStockAsync(StockIn stockIn, int itemId, int quantity, int userId, DateTime now, CancellationToken ct)
    {
        var after = await TryChangeQuantityAsync(itemId, quantity, now, ct) ?? throw ApiException.NotFound("Item");
        db.StockMovements.Add(new StockMovement
        {
            ItemId = itemId, Type = StockMovementType.StockIn, Change = quantity, QuantityAfter = after,
            StockInId = stockIn.Id, UserId = userId, CreatedAt = now,
        });
    }

    /// <summary>
    /// Cancels a stock-in voucher entered by mistake and takes its quantities back out. Refused (409, nothing changes)
    /// if some of that stock has already gone out in orders. Cancelling a demo return puts those units back on the demo.
    /// </summary>
    public async Task CancelStockInAsync(int stockInId, string reason, int userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var flipped = await db.StockIns
            .Where(s => s.Id == stockInId && s.Status == OrderStatus.Completed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, OrderStatus.Cancelled)
                .SetProperty(x => x.CancelledAt, now)
                .SetProperty(x => x.CancelledByUserId, userId)
                .SetProperty(x => x.CancelReason, reason.Trim()), ct);
        if (flipped == 0)
        {
            if (!await db.StockIns.AnyAsync(s => s.Id == stockInId, ct)) throw ApiException.NotFound("Stock-in");
            throw ApiException.Conflict("This stock-in is already cancelled.");
        }

        var lines = await db.StockInLines.Where(l => l.StockInId == stockInId).OrderBy(l => l.ItemId).ToListAsync(ct);
        var shortages = new List<object>();
        foreach (var line in lines)
        {
            var after = await TryChangeQuantityAsync(line.ItemId, -line.Quantity, now, ct);
            if (after is null)
            {
                var available = await db.Items.Where(i => i.Id == line.ItemId).Select(i => i.Quantity).FirstAsync(ct);
                shortages.Add(new { itemId = line.ItemId, itemName = line.ItemName, requested = line.Quantity, available });
                continue;
            }
            db.StockMovements.Add(new StockMovement
            {
                ItemId = line.ItemId, Type = StockMovementType.StockInCancelled, Change = -line.Quantity, QuantityAfter = after.Value,
                StockInId = stockInId, UserId = userId, Note = reason.Trim(), CreatedAt = now,
            });
        }

        if (shortages.Count > 0)
        {
            await tx.RollbackAsync(ct);
            throw ApiException.Conflict("Some of this stock has already been taken out, so the stock-in can't be cancelled.",
                new Dictionary<string, object?> { ["shortages"] = shortages });
        }

        // A cancelled demo return puts what it brought back on the customer again.
        foreach (var line in lines.Where(l => l.DemoOrderLineId is not null))
        {
            var n = line.Quantity;
            await db.OrderLines.Where(l => l.Id == line.DemoOrderLineId!.Value)
                .ExecuteUpdateAsync(u => u.SetProperty(l => l.ReturnedQuantity, l => l.ReturnedQuantity - n), ct);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public static string FormatStockInNumber(int id) => $"IN-{id:D6}";

    /// <summary>The same item picked twice becomes one line.</summary>
    private static List<(int ItemId, int Quantity)> Combine(IEnumerable<OrderLineRequest> lines) =>
        Combine(lines.Select(l => (l.ItemId, l.Quantity)));

    private static List<(int ItemId, int Quantity)> Combine(IEnumerable<(int ItemId, int Quantity)> lines) =>
        lines.GroupBy(l => l.ItemId).Select(g => (g.Key, g.Sum(l => l.Quantity))).ToList();

    private async Task<Dictionary<int, Item>> LoadItemsAsync(IEnumerable<int> itemIds, string disabledMessage, CancellationToken ct)
    {
        var ids = itemIds.ToList();
        var items = await db.Items.Include(i => i.Category).Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        foreach (var id in ids)
        {
            if (!items.TryGetValue(id, out var item)) throw ApiException.NotFound($"Item #{id}");
            if (!item.IsActive || !item.Category.IsActive) throw ApiException.BadRequest(string.Format(disabledMessage, item.Name));
        }
        return items;
    }

    /// <summary>Changes the quantity in one statement. Returns the new quantity, or null if stock would go below zero.</summary>
    private async Task<int?> TryChangeQuantityAsync(int itemId, int change, DateTime now, CancellationToken ct)
    {
        var rows = await db.Items
            .Where(i => i.Id == itemId && i.Quantity + change >= 0)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Quantity, i => i.Quantity + change)
                .SetProperty(i => i.UpdatedAt, now), ct);
        if (rows == 0) return null;
        return await db.Items.Where(i => i.Id == itemId).Select(i => i.Quantity).FirstAsync(ct);
    }
}
