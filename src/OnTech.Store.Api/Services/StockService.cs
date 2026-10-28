using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Data;
using OnTech.Store.Api.Infrastructure;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Services;

/// <summary>
/// The only place that changes item quantities: an order (stock out to a customer) or a stock-in voucher
/// (stock in from a supplier), plus cancelling either one. Quantities are never typed in directly. Each change is one database transaction, and every
/// decrease is a single conditional UPDATE ("... WHERE Quantity >= @n"), so two PCs ordering the same
/// item at the same moment can never take more than is in stock: the second one gets a clear 409.
/// </summary>
public sealed class StockService(StoreDbContext db)
{
    public async Task<Order> CreateOrderAsync(CreateOrderRequest req, int userId, CancellationToken ct)
    {
        var voucher = req.IssueVoucherNumber.Clean() ?? throw ApiException.BadRequest("Issue voucher number is required.");
        var receipt = req.ReceiptNumber.Clean();
        if (req.WithReceipt && receipt is null)
            throw ApiException.BadRequest("Receipt number is required when the order is made with a receipt.");
        if (!req.WithReceipt) receipt = null;

        var wanted = Combine(req.Lines);
        if (wanted.Count == 0) throw ApiException.BadRequest("An order needs at least one item.");

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == req.CustomerId, ct)
                       ?? throw ApiException.NotFound("Customer");
        if (!customer.IsActive) throw ApiException.BadRequest("This customer is disabled.");

        if (await db.Orders.AnyAsync(o => o.IssueVoucherNumber == voucher, ct))
            throw ApiException.Conflict($"Issue voucher number '{voucher}' is already used by another order.");
        if (receipt is not null && await db.Orders.AnyAsync(o => o.ReceiptNumber == receipt, ct))
            throw ApiException.Conflict($"Receipt number '{receipt}' is already used by another order.");

        var items = await LoadItemsAsync(wanted.Select(w => w.ItemId), "'{0}' is disabled and cannot be ordered.", ct);

        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var order = new Order
        {
            CustomerId = customer.Id,
            IssueVoucherNumber = voucher,
            WithReceipt = req.WithReceipt,
            ReceiptNumber = receipt,
            Notes = req.Notes.Clean(),
            Status = OrderStatus.Completed,
            CreatedByUserId = userId,
            CreatedAt = now,
            OrderNumber = "PENDING",
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
            throw ApiException.Conflict(receipt is not null && await db.Orders.AnyAsync(o => o.ReceiptNumber == receipt, ct)
                ? $"Receipt number '{receipt}' is already used by another order."
                : $"Issue voucher number '{voucher}' is already used by another order.");
        }
        order.OrderNumber = FormatOrderNumber(order.Id);

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
        var flipped = await db.Orders
            .Where(o => o.Id == orderId && o.Status == OrderStatus.Completed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Cancelled)
                .SetProperty(o => o.CancelledAt, now)
                .SetProperty(o => o.CancelledByUserId, userId)
                .SetProperty(o => o.CancelReason, reason.Trim()), ct);
        if (flipped == 0)
        {
            if (!await db.Orders.AnyAsync(o => o.Id == orderId, ct)) throw ApiException.NotFound("Order");
            throw ApiException.Conflict("This order is already cancelled.");
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

    public async Task<StockIn> CreateStockInAsync(CreateStockInRequest req, int userId, CancellationToken ct)
    {
        var voucher = req.VoucherNumber.Clean() ?? throw ApiException.BadRequest("Voucher number is required.");
        var wanted = Combine(req.Lines);
        if (wanted.Count == 0) throw ApiException.BadRequest("A stock-in voucher needs at least one item.");

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == req.SupplierId, ct) ?? throw ApiException.NotFound("Supplier");
        if (!supplier.IsActive) throw ApiException.BadRequest("This supplier is disabled.");
        if (await db.StockIns.AnyAsync(s => s.VoucherNumber == voucher, ct))
            throw ApiException.Conflict($"Voucher number '{voucher}' is already used by another stock-in.");

        var items = await LoadItemsAsync(wanted.Select(w => w.ItemId), "'{0}' is disabled.", ct);

        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var stockIn = new StockIn
        {
            SupplierId = supplier.Id,
            VoucherNumber = voucher,
            SupplierInvoiceNumber = req.SupplierInvoiceNumber.Clean(),
            Notes = req.Notes.Clean(),
            Status = OrderStatus.Completed,
            CreatedByUserId = userId,
            CreatedAt = now,
            StockInNumber = "PENDING",
            Lines = wanted.Select(w =>
            {
                var item = items[w.ItemId];
                return new StockInLine
                {
                    ItemId = item.Id, Quantity = w.Quantity, ItemName = item.Name, ItemCode = item.Code,
                    CategoryId = item.CategoryId, CategoryName = item.Category.Name,
                };
            }).ToList(),
        };
        db.StockIns.Add(stockIn);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw ApiException.Conflict($"Voucher number '{voucher}' is already used by another stock-in.");
        }
        stockIn.StockInNumber = FormatStockInNumber(stockIn.Id);

        foreach (var w in wanted.OrderBy(w => w.ItemId))
        {
            var after = await TryChangeQuantityAsync(w.ItemId, w.Quantity, now, ct) ?? throw ApiException.NotFound("Item");
            db.StockMovements.Add(new StockMovement
            {
                ItemId = w.ItemId, Type = StockMovementType.StockIn, Change = w.Quantity, QuantityAfter = after,
                StockInId = stockIn.Id, UserId = userId, CreatedAt = now,
            });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return stockIn;
    }

    /// <summary>
    /// Cancels a stock-in voucher entered by mistake and takes its quantities back out. Refused (409, nothing changes)
    /// if some of that stock has already gone out in orders.
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

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public static string FormatOrderNumber(int id) => $"ORD-{id:D6}";
    public static string FormatStockInNumber(int id) => $"IN-{id:D6}";

    /// <summary>The same item picked twice becomes one line.</summary>
    private static List<(int ItemId, int Quantity)> Combine(IEnumerable<OrderLineRequest> lines) =>
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
