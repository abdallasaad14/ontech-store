using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Data;
using OnTech.Store.Api.Infrastructure;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Services;

/// <summary>
/// The only place that changes item quantities. Each change is one database transaction, and every
/// decrease is a single conditional UPDATE ("... WHERE Quantity >= @n"), so two PCs ordering the same
/// item at the same moment can never take more than is in stock: the second one gets a clear 409.
/// </summary>
public sealed class StockService(StoreDbContext db)
{
    public async Task<Order> CreateOrderAsync(CreateOrderRequest req, int userId, CancellationToken ct)
    {
        var receipt = req.ReceiptNumber.Clean();
        if (req.WithReceipt && receipt is null)
            throw ApiException.BadRequest("Receipt number is required when the order is made with a receipt.");
        if (!req.WithReceipt) receipt = null;

        // The same item picked twice becomes one line.
        var wanted = req.Lines
            .GroupBy(l => l.ItemId)
            .Select(g => (ItemId: g.Key, Quantity: g.Sum(l => l.Quantity)))
            .ToList();
        if (wanted.Count == 0) throw ApiException.BadRequest("An order needs at least one item.");

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == req.CustomerId, ct)
                       ?? throw ApiException.NotFound("Customer");
        if (!customer.IsActive) throw ApiException.BadRequest("This customer is disabled.");

        if (receipt is not null && await db.Orders.AnyAsync(o => o.ReceiptNumber == receipt, ct))
            throw ApiException.Conflict($"Receipt number '{receipt}' is already used by another order.");

        var ids = wanted.Select(w => w.ItemId).ToList();
        var items = await db.Items.Include(i => i.Category).Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        foreach (var w in wanted)
        {
            if (!items.TryGetValue(w.ItemId, out var item)) throw ApiException.NotFound($"Item #{w.ItemId}");
            if (!item.IsActive) throw ApiException.BadRequest($"'{item.Name}' is disabled and cannot be ordered.");
        }

        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var order = new Order
        {
            CustomerId = customer.Id,
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
        catch (DbUpdateException) when (receipt is not null)
        {
            throw ApiException.Conflict($"Receipt number '{receipt}' is already used by another order.");
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

    public async Task<int> AddQuantityAsync(int itemId, int quantity, string? note, int userId, CancellationToken ct)
    {
        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct) ?? throw ApiException.NotFound("Item");
        if (!item.IsActive) throw ApiException.BadRequest($"'{item.Name}' is disabled.");

        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var after = await TryChangeQuantityAsync(itemId, quantity, now, ct) ?? throw ApiException.NotFound("Item");
        db.StockMovements.Add(new StockMovement
        {
            ItemId = itemId, Type = StockMovementType.Restock, Change = quantity, QuantityAfter = after,
            UserId = userId, Note = note.Clean(), CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return after;
    }

    public async Task<int> AdjustQuantityAsync(int itemId, int newQuantity, string reason, int userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Lock the row with a no-op update before reading, so the difference we record is exact.
        var locked = await db.Items.Where(i => i.Id == itemId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.UpdatedAt, now), ct);
        if (locked == 0) throw ApiException.NotFound("Item");

        var current = await db.Items.Where(i => i.Id == itemId).Select(i => i.Quantity).FirstAsync(ct);
        var change = newQuantity - current;
        if (change != 0)
        {
            await db.Items.Where(i => i.Id == itemId).ExecuteUpdateAsync(s => s.SetProperty(i => i.Quantity, newQuantity), ct);
            db.StockMovements.Add(new StockMovement
            {
                ItemId = itemId, Type = StockMovementType.Adjustment, Change = change, QuantityAfter = newQuantity,
                UserId = userId, Note = reason.Trim(), CreatedAt = now,
            });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return newQuantity;
    }

    public void RecordInitialStock(Item item, int userId)
    {
        if (item.Quantity <= 0) return;
        db.StockMovements.Add(new StockMovement
        {
            Item = item, Type = StockMovementType.Initial, Change = item.Quantity, QuantityAfter = item.Quantity,
            UserId = userId, CreatedAt = item.CreatedAt,
        });
    }

    public static string FormatOrderNumber(int id) => $"ORD-{id:D6}";

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
