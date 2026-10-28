using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Auth;
using OnTech.Store.Api.Data;
using OnTech.Store.Api.Infrastructure;
using OnTech.Store.Api.Services;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Controllers;

/// <summary>Stock received from suppliers (إذن إضافة). With orders, the only way quantities change.</summary>
[ApiController]
[Route("api/stock-ins")]
public class StockInsController(StoreDbContext db, StockService stock) : ControllerBase
{
    /// <summary>Records stock received from a supplier and adds the quantities, in one transaction.</summary>
    [HttpPost]
    [HasPermission(Permission.AddStock)]
    [ProducesResponseType<StockInDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<StockInDto>> Create(CreateStockInRequest req, CancellationToken ct)
    {
        var created = await stock.CreateStockInAsync(req, User.GetUserId(), ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, await LoadDto(created.Id, ct));
    }

    /// <summary>The stock-in log, newest first, with search, filters and paging.</summary>
    [HttpGet]
    [HasPermission(Permission.ViewOrders)]
    public async Task<PagedResult<StockInDto>> Search([FromQuery] StockInQuery f, CancellationToken ct)
    {
        var q = db.StockIns.AsNoTracking();
        if (f.From is { } from) { var d = from.UtcDateTime; q = q.Where(s => s.CreatedAt >= d); }
        if (f.To is { } to) { var d = to.UtcDateTime; q = q.Where(s => s.CreatedAt <= d); }
        if (f.SupplierId is { } sid) q = q.Where(s => s.SupplierId == sid);
        if (f.ItemId is { } iid) q = q.Where(s => s.Lines.Any(l => l.ItemId == iid));
        if (f.Status is { } st) q = q.Where(s => s.Status == st);
        if (f.Search.Clean()?.ToLower() is { } t) // ToLower: SQLite compares case-sensitively; SQL Server doesn't.
            q = q.Where(s => s.StockInNumber.ToLower().Contains(t)
                             || s.VoucherNumber.ToLower().Contains(t)
                             || (s.SupplierInvoiceNumber != null && s.SupplierInvoiceNumber.ToLower().Contains(t))
                             || s.Supplier.CompanyName.ToLower().Contains(t)
                             || (s.Supplier.TaxNumber != null && s.Supplier.TaxNumber.ToLower().Contains(t))
                             || (s.Notes != null && s.Notes.ToLower().Contains(t))
                             || s.Lines.Any(l => l.ItemName.ToLower().Contains(t) || (l.ItemCode != null && l.ItemCode.StartsWith(t))));

        var total = await q.CountAsync(ct);
        var page = await q.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Skip((f.Page - 1) * f.PageSize).Take(f.PageSize)
            .Include(s => s.Supplier).Include(s => s.CreatedByUser).Include(s => s.CancelledByUser).Include(s => s.Lines)
            .AsSplitQuery()
            .ToListAsync(ct);

        return new PagedResult<StockInDto>
        {
            Items = page.Select(s => s.ToDto()).ToList(),
            Page = f.Page,
            PageSize = f.PageSize,
            TotalCount = total,
        };
    }

    [HttpGet("{id:int}")]
    [HasPermission(Permission.ViewOrders)]
    public async Task<ActionResult<StockInDto>> Get(int id, CancellationToken ct) => await LoadDto(id, ct);

    /// <summary>Cancels a stock-in entered by mistake and takes its quantities back out. It stays in the log as Cancelled.</summary>
    [HttpPost("{id:int}/cancel")]
    [HasPermission(Permission.CancelOrders)]
    public async Task<ActionResult<StockInDto>> Cancel(int id, CancelStockInRequest req, CancellationToken ct)
    {
        await stock.CancelStockInAsync(id, req.Reason, User.GetUserId(), ct);
        return await LoadDto(id, ct);
    }

    private async Task<StockInDto> LoadDto(int id, CancellationToken ct)
    {
        var s = await db.StockIns.AsNoTracking()
            .Include(x => x.Supplier).Include(x => x.CreatedByUser).Include(x => x.CancelledByUser).Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiException.NotFound("Stock-in");
        return s.ToDto();
    }
}
