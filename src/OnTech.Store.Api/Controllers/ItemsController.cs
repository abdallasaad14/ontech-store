using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Auth;
using OnTech.Store.Api.Data;
using OnTech.Store.Api.Infrastructure;
using OnTech.Store.Api.Services;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Controllers;

[ApiController]
[Route("api/items")]
public class ItemsController(StoreDbContext db, StockService stock) : ControllerBase
{
    /// <summary>Items with their available quantities. Filter by category for the New Order screen.</summary>
    [HttpGet]
    [HasPermission(Permission.ViewStock)]
    public async Task<IReadOnlyList<ItemDto>> List(
        [FromQuery] int? categoryId, [FromQuery] string? search,
        [FromQuery] bool includeInactive = false, [FromQuery] bool lowStockOnly = false, [FromQuery] bool inStockOnly = false,
        CancellationToken ct = default)
    {
        var q = db.Items.AsNoTracking().Include(i => i.Category).AsQueryable();
        if (categoryId is not null) q = q.Where(i => i.CategoryId == categoryId);
        if (!includeInactive) q = q.Where(i => i.IsActive && i.Category.IsActive);
        if (lowStockOnly) q = q.Where(i => i.LowStockThreshold > 0 && i.Quantity <= i.LowStockThreshold);
        if (inStockOnly) q = q.Where(i => i.Quantity > 0);
        if (search.Clean() is { } s)
            q = q.Where(i => i.Name.Contains(s) || (i.Code != null && i.Code.Contains(s)) || i.Category.Name.Contains(s));
        var items = await q.OrderBy(i => i.Category.Name).ThenBy(i => i.Name).ToListAsync(ct);
        return items.Select(i => i.ToDto()).ToList();
    }

    [HttpGet("{id:int}")]
    [HasPermission(Permission.ViewStock)]
    public async Task<ActionResult<ItemDto>> Get(int id, CancellationToken ct) => (await Load(id, ct)).ToDto();

    [HttpPost]
    [HasPermission(Permission.ManageCatalog)]
    public async Task<ActionResult<ItemDto>> Create(CreateItemRequest req, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == req.CategoryId, ct) ?? throw ApiException.NotFound("Category");
        var name = req.Name.Trim();
        await EnsureNameFree(req.CategoryId, name, null, ct);
        var now = DateTime.UtcNow;
        var item = new Item
        {
            Category = category, Name = name, Code = req.Code.Clean(), Description = req.Description.Clean(),
            Quantity = req.InitialQuantity, LowStockThreshold = req.LowStockThreshold, CreatedAt = now, UpdatedAt = now,
        };
        db.Items.Add(item);
        stock.RecordInitialStock(item, User.GetUserId());
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, item.ToDto());
    }

    /// <summary>Edits item details. Quantity is changed only through add-quantity, adjust and orders.</summary>
    [HttpPut("{id:int}")]
    [HasPermission(Permission.ManageCatalog)]
    public async Task<ActionResult<ItemDto>> Update(int id, UpdateItemRequest req, CancellationToken ct)
    {
        var item = await Load(id, ct, tracking: true);
        if (item.CategoryId != req.CategoryId && !await db.Categories.AnyAsync(c => c.Id == req.CategoryId, ct))
            throw ApiException.NotFound("Category");
        var name = req.Name.Trim();
        await EnsureNameFree(req.CategoryId, name, id, ct);
        item.CategoryId = req.CategoryId;
        item.Name = name;
        item.Code = req.Code.Clean();
        item.Description = req.Description.Clean();
        item.LowStockThreshold = req.LowStockThreshold;
        item.IsActive = req.IsActive;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (await Load(id, ct)).ToDto();
    }

    /// <summary>Disables the item so it can't be ordered; history is kept.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permission.ManageCatalog)]
    public async Task<IActionResult> Disable(int id, CancellationToken ct)
    {
        var item = await Load(id, ct, tracking: true);
        item.IsActive = false;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Adds received stock to an existing item (the "Add Quantity" home page action).</summary>
    [HttpPost("{id:int}/add-quantity")]
    [HasPermission(Permission.AddStock)]
    public async Task<ActionResult<ItemDto>> AddQuantity(int id, AddQuantityRequest req, CancellationToken ct)
    {
        await stock.AddQuantityAsync(id, req.Quantity, req.Note, User.GetUserId(), ct);
        return (await Load(id, ct)).ToDto();
    }

    /// <summary>Sets the quantity to a counted value (stock-take correction). The difference is logged.</summary>
    [HttpPost("{id:int}/adjust")]
    [HasPermission(Permission.ManageCatalog)]
    public async Task<ActionResult<ItemDto>> Adjust(int id, AdjustQuantityRequest req, CancellationToken ct)
    {
        await stock.AdjustQuantityAsync(id, req.NewQuantity, req.Reason, User.GetUserId(), ct);
        return (await Load(id, ct)).ToDto();
    }

    private async Task<Item> Load(int id, CancellationToken ct, bool tracking = false)
    {
        var q = tracking ? db.Items : db.Items.AsNoTracking();
        return await q.Include(i => i.Category).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw ApiException.NotFound("Item");
    }

    private async Task EnsureNameFree(int categoryId, string name, int? exceptId, CancellationToken ct)
    {
        var lower = name.ToLower();
        if (await db.Items.AnyAsync(i => i.CategoryId == categoryId && i.Name.ToLower() == lower && i.Id != exceptId, ct))
            throw ApiException.Conflict($"An item named '{name}' already exists in this category.");
    }
}
