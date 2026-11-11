using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Auth;
using OnTech.Store.Api.Data;
using OnTech.Store.Api.Infrastructure;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Controllers;

/// <summary>Demos that are still out: what the demo return screen lists for a customer.</summary>
[ApiController]
[Route("api/demos")]
public class DemosController(StoreDbContext db) : ControllerBase
{
    /// <summary>
    /// Demo lines that are still (partly) with the customer, oldest first. Cancelled demos and lines that came back in full are left out.
    /// Readable by whoever records demo returns (AddStock) or reads the logs (ViewOrders).
    /// </summary>
    [HttpGet("open")]
    [HasAnyPermission(Permission.AddStock | Permission.ViewOrders)]
    public async Task<IReadOnlyList<OpenDemoLineDto>> Open([FromQuery] OpenDemoQuery f, CancellationToken ct)
    {
        var q = db.OrderLines.AsNoTracking()
            .Where(l => l.Order.Type == IssueType.Demo && l.Order.Status == OrderStatus.Completed && l.ReturnedQuantity < l.Quantity);
        if (f.CustomerId is { } cid) q = q.Where(l => l.Order.CustomerId == cid);
        if (f.ItemId is { } iid) q = q.Where(l => l.ItemId == iid);

        var rows = await q
            .Select(l => new
            {
                l.Id, l.OrderId, Voucher = l.Order.IssueVoucherNumber ?? l.Order.OrderNumber, l.Order.CreatedAt,
                l.Order.CustomerId, l.Order.Customer.CustomerNumber, l.Order.Customer.CompanyName,
                l.ItemId, l.ItemName, l.ItemCode, l.CategoryName, l.Quantity, l.ReturnedQuantity,
            })
            .ToListAsync(ct);

        // Only what is out right now, so a small list.
        return rows.OrderBy(r => r.CreatedAt).ThenBy(r => r.OrderId).ThenBy(r => r.Id)
            .Select(r => new OpenDemoLineDto
            {
                OrderLineId = r.Id, OrderId = r.OrderId, IssueVoucherNumber = r.Voucher, CreatedAt = r.CreatedAt.ToUtcOffset(),
                CustomerId = r.CustomerId, CustomerNumber = r.CustomerNumber, CustomerName = r.CompanyName,
                ItemId = r.ItemId, ItemName = r.ItemName, ItemCode = r.ItemCode, CategoryName = r.CategoryName,
                Quantity = r.Quantity, ReturnedQuantity = r.ReturnedQuantity, OpenQuantity = r.Quantity - r.ReturnedQuantity,
            })
            .ToList();
    }
}
