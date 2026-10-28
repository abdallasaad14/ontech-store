using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Auth;
using OnTech.Store.Api.Data;
using OnTech.Store.Api.Infrastructure;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Controllers;

/// <summary>The full history of stock in and out (initial stock, stock-in vouchers, orders, cancellations; older restocks and adjustments).</summary>
[ApiController]
[Route("api/stock-movements")]
[HasPermission(Permission.ViewStock)]
public class StockMovementsController(StoreDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<PagedResult<StockMovementDto>> Search([FromQuery] StockMovementQuery f, CancellationToken ct)
    {
        var q = db.StockMovements.AsNoTracking();
        if (f.ItemId is { } iid) q = q.Where(m => m.ItemId == iid);
        if (f.CategoryId is { } cid) q = q.Where(m => m.Item.CategoryId == cid);
        if (f.Type is { } t) q = q.Where(m => m.Type == t);
        if (f.UserId is { } uid) q = q.Where(m => m.UserId == uid);
        if (f.From is { } from) { var d = from.UtcDateTime; q = q.Where(m => m.CreatedAt >= d); }
        if (f.To is { } to) { var d = to.UtcDateTime; q = q.Where(m => m.CreatedAt <= d); }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Skip((f.Page - 1) * f.PageSize).Take(f.PageSize)
            .Select(m => new
            {
                m.Id, m.ItemId, ItemName = m.Item.Name, CategoryName = m.Item.Category.Name, m.Type, m.Change, m.QuantityAfter,
                m.OrderId, OrderNumber = m.Order != null ? m.Order.OrderNumber : null,
                m.StockInId, StockInNumber = m.StockIn != null ? m.StockIn.StockInNumber : null, m.UserId, UserName = m.User.FullName, m.Note, m.CreatedAt,
            })
            .ToListAsync(ct);

        return new PagedResult<StockMovementDto>
        {
            Items = rows.Select(m => new StockMovementDto
            {
                Id = m.Id, ItemId = m.ItemId, ItemName = m.ItemName, CategoryName = m.CategoryName, Type = m.Type, Change = m.Change,
                QuantityAfter = m.QuantityAfter, OrderId = m.OrderId, OrderNumber = m.OrderNumber,
                StockInId = m.StockInId, StockInNumber = m.StockInNumber, UserId = m.UserId,
                UserName = m.UserName, Note = m.Note, CreatedAt = m.CreatedAt.ToUtcOffset(),
            }).ToList(),
            Page = f.Page,
            PageSize = f.PageSize,
            TotalCount = total,
        };
    }
}
