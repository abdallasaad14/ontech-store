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
public class ItemsController(StoreDbContext db) : ControllerBase
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
        if (search.Clean()?.ToLower() is { } s) // ToLower: SQLite compares case-sensitively; SQL Server doesn't.
            // A code matches from its start, so "01" lists category 01's items and "01002" finds exactly one item.
            q = q.Where(i => i.Name.ToLower().Contains(s) || (i.Code != null && i.Code.StartsWith(s)) || i.Category.Name.ToLower().Contains(s));
        var items = await q.OrderBy(i => i.Category.Name).ThenBy(i => i.Name).ToListAsync(ct);
        return items.Select(i => i.ToDto()).ToList();
    }

    [HttpGet("{id:int}")]
    [HasPermission(Permission.ViewStock)]
    public async Task<ActionResult<ItemDto>> Get(int id, CancellationToken ct) => (await Load(id, ct)).ToDto();

    /// <summary>The item card: every movement of this item, oldest first, with who supplied or took it and the balance after it.</summary>
    [HttpGet("{id:int}/card")]
    [HasPermission(Permission.ViewStock)]
    public async Task<ActionResult<ItemCardDto>> Card(int id, CancellationToken ct)
    {
        var item = await Load(id, ct);
        var rows = await db.StockMovements.AsNoTracking().Where(m => m.ItemId == id)
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .Select(m => new
            {
                m.Id, m.CreatedAt, m.Type, m.Change, m.QuantityAfter, m.OrderId, m.StockInId, UserName = m.User.FullName, m.Note,
                OrderNumber = m.Order != null ? m.Order.OrderNumber : null,
                IssueVoucher = m.Order != null ? m.Order.IssueVoucherNumber : null,
                Receipt = m.Order != null ? m.Order.ReceiptNumber : null,
                Customer = m.Order != null ? m.Order.Customer.CompanyName : null,
                StockInNumber = m.StockIn != null ? m.StockIn.StockInNumber : null,
                InVoucher = m.StockIn != null ? m.StockIn.VoucherNumber : null,
                SupplierInvoice = m.StockIn != null ? m.StockIn.SupplierInvoiceNumber : null,
                Supplier = m.StockIn != null ? m.StockIn.Supplier.CompanyName : null,
            })
            .ToListAsync(ct);

        var lines = rows.Select(m => new ItemCardLineDto
        {
            Id = m.Id, Date = m.CreatedAt.ToUtcOffset(), Type = m.Type,
            In = Math.Max(m.Change, 0), Out = Math.Max(-m.Change, 0), Balance = m.QuantityAfter,
            OrderId = m.OrderId, StockInId = m.StockInId,
            DocumentNumber = m.OrderNumber ?? m.StockInNumber,
            VoucherNumber = m.IssueVoucher ?? m.InVoucher,
            InvoiceNumber = m.Receipt ?? m.SupplierInvoice,
            PartyName = m.Customer ?? m.Supplier,
            UserName = m.UserName, Note = m.Note,
        }).ToList();

        return new ItemCardDto { Item = item.ToDto(), TotalIn = lines.Sum(l => l.In), TotalOut = lines.Sum(l => l.Out), Lines = lines };
    }

    [HttpPost]
    [HasPermission(Permission.ManageCatalog)]
    public async Task<ActionResult<ItemDto>> Create(CreateItemRequest req, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == req.CategoryId, ct) ?? throw ApiException.NotFound("Category");
        var name = req.Name.Trim();
        var now = DateTime.UtcNow;
        var item = await NumberingGate.RunAsync(async () =>
        {
            await EnsureNameFree(req.CategoryId, name, null, ct);
            if (req.Code.Clean() is { } code) await EnsureCodeFree(code, null, ct);
            var created = new Item
            {
                Category = category, Name = name, Code = req.Code.Clean() ?? await CatalogCodes.NextItemCodeAsync(db, category, ct), Description = req.Description.Clean(),
                LowStockThreshold = req.LowStockThreshold, CreatedAt = now, UpdatedAt = now,
            };
            db.Items.Add(created); // starts at 0; stock comes in only through a stock-in voucher
            await db.SaveChangesAsync(ct);
            return created;
        }, ct);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, item.ToDto());
    }

    /// <summary>Edits item details. Quantity is changed only by orders and stock-in vouchers.</summary>
    [HttpPut("{id:int}")]
    [HasPermission(Permission.ManageCatalog)]
    public async Task<ActionResult<ItemDto>> Update(int id, UpdateItemRequest req, CancellationToken ct)
    {
        var item = await Load(id, ct, tracking: true);
        if (item.CategoryId != req.CategoryId && !await db.Categories.AnyAsync(c => c.Id == req.CategoryId, ct))
            throw ApiException.NotFound("Category");
        var name = req.Name.Trim();
        await EnsureNameFree(req.CategoryId, name, id, ct);
        if (req.Code.Clean() is { } code && code != item.Code) await EnsureCodeFree(code, id, ct);
        item.CategoryId = req.CategoryId;
        item.Name = name;
        item.Code = req.Code.Clean() ?? item.Code; // Empty keeps the generated code.
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

    private async Task EnsureCodeFree(string code, int? exceptId, CancellationToken ct)
    {
        if (!CatalogCodes.IsValid(code)) throw ApiException.BadRequest("Codes can only contain digits.");
        if (await db.Items.AnyAsync(i => i.Code == code && i.Id != exceptId, ct))
            throw ApiException.Conflict($"Item code '{code}' is already used.");
    }
}
