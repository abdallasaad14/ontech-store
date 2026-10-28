using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Auth;
using OnTech.Store.Api.Data;
using OnTech.Store.Api.Infrastructure;
using OnTech.Store.Api.Services;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController(StoreDbContext db, StockService stock) : ControllerBase
{
    /// <summary>
    /// Creates an order and takes the quantities out of stock in one transaction.
    /// Returns 409 with a "shortages" list if any item doesn't have enough stock; in that case nothing is taken.
    /// </summary>
    [HttpPost]
    [HasPermission(Permission.CreateOrders)]
    [ProducesResponseType<OrderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> Create(CreateOrderRequest req, CancellationToken ct)
    {
        var order = await stock.CreateOrderAsync(req, User.GetUserId(), ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, await LoadDto(order.Id, ct));
    }

    /// <summary>The orders log, with search, filters, sorting and paging.</summary>
    [HttpGet]
    [HasPermission(Permission.ViewOrders)]
    public async Task<PagedResult<OrderDto>> Search([FromQuery] OrderQuery query, CancellationToken ct)
    {
        var q = Filter(db.Orders.AsNoTracking(), query);
        var total = await q.CountAsync(ct);

        q = query.Sort switch
        {
            OrderSort.OldestFirst => q.OrderBy(o => o.CreatedAt).ThenBy(o => o.Id),
            OrderSort.CustomerName => q.OrderBy(o => o.Customer.CompanyName).ThenByDescending(o => o.CreatedAt),
            OrderSort.ReceiptNumber => q.OrderBy(o => o.ReceiptNumber == null).ThenBy(o => o.ReceiptNumber).ThenByDescending(o => o.CreatedAt),
            OrderSort.TotalQuantity => q.OrderByDescending(o => o.Lines.Sum(l => l.Quantity)).ThenByDescending(o => o.CreatedAt),
            _ => q.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id),
        };

        var page = await q
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Include(o => o.Customer).Include(o => o.CreatedByUser).Include(o => o.CancelledByUser).Include(o => o.Lines)
            .AsSplitQuery()
            .ToListAsync(ct);

        return new PagedResult<OrderDto>
        {
            Items = page.Select(o => o.ToDto()).ToList(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = total,
        };
    }

    [HttpGet("{id:int}")]
    [HasPermission(Permission.ViewOrders)]
    public async Task<ActionResult<OrderDto>> Get(int id, CancellationToken ct) => await LoadDto(id, ct);

    /// <summary>Looks up an order by its receipt number (exact match).</summary>
    [HttpGet("by-receipt/{receiptNumber}")]
    [HasPermission(Permission.ViewOrders)]
    public async Task<ActionResult<OrderDto>> GetByReceipt(string receiptNumber, CancellationToken ct)
    {
        var id = await db.Orders.Where(o => o.ReceiptNumber == receiptNumber.Trim()).Select(o => (int?)o.Id).FirstOrDefaultAsync(ct)
                 ?? throw ApiException.NotFound("Order with that receipt number");
        return await LoadDto(id, ct);
    }

    /// <summary>Cancels an order and puts its quantities back in stock. The order stays in the log as Cancelled.</summary>
    [HttpPost("{id:int}/cancel")]
    [HasPermission(Permission.CancelOrders)]
    public async Task<ActionResult<OrderDto>> Cancel(int id, CancelOrderRequest req, CancellationToken ct)
    {
        await stock.CancelOrderAsync(id, req.Reason, User.GetUserId(), ct);
        return await LoadDto(id, ct);
    }

    private async Task<OrderDto> LoadDto(int id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Customer).Include(o => o.CreatedByUser).Include(o => o.CancelledByUser).Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw ApiException.NotFound("Order");
        return order.ToDto();
    }

    internal static IQueryable<Order> Filter(IQueryable<Order> q, OrderQuery f)
    {
        if (f.From is { } from) { var d = from.UtcDateTime; q = q.Where(o => o.CreatedAt >= d); }
        if (f.To is { } to) { var d = to.UtcDateTime; q = q.Where(o => o.CreatedAt <= d); }
        if (f.CustomerId is { } cid) q = q.Where(o => o.CustomerId == cid);
        if (f.CreatedByUserId is { } uid) q = q.Where(o => o.CreatedByUserId == uid);
        if (f.ItemId is { } iid) q = q.Where(o => o.Lines.Any(l => l.ItemId == iid));
        if (f.CategoryId is { } catId) q = q.Where(o => o.Lines.Any(l => l.CategoryId == catId));
        if (f.WithReceipt is { } wr) q = q.Where(o => o.WithReceipt == wr);
        if (f.Status is { } st) q = q.Where(o => o.Status == st);
        if (f.ReceiptNumber.Clean() is { } rn) q = q.Where(o => o.ReceiptNumber != null && o.ReceiptNumber.Contains(rn));
        if (f.OrderNumber.Clean() is { } on) q = q.Where(o => o.OrderNumber.Contains(on));
        if (f.IssueVoucherNumber.Clean() is { } iv) q = q.Where(o => o.IssueVoucherNumber != null && o.IssueVoucherNumber.Contains(iv));
        if (f.Search.Clean()?.ToLower() is { } s) // ToLower: SQLite compares case-sensitively; SQL Server doesn't.
            q = q.Where(o => o.OrderNumber.ToLower().Contains(s)
                             || (o.IssueVoucherNumber != null && o.IssueVoucherNumber.ToLower().Contains(s))
                             || (o.ReceiptNumber != null && o.ReceiptNumber.ToLower().Contains(s))
                             || o.Customer.CompanyName.ToLower().Contains(s)
                             || (o.Customer.TaxNumber != null && o.Customer.TaxNumber.ToLower().Contains(s))
                             || (o.Notes != null && o.Notes.ToLower().Contains(s))
                             || o.Lines.Any(l => l.ItemName.ToLower().Contains(s) || (l.ItemCode != null && l.ItemCode.StartsWith(s))));
        return q;
    }
}
