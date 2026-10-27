using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Auth;
using OnTech.Store.Api.Data;
using OnTech.Store.Api.Services;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[HasPermission(Permission.ViewStock)]
public class DashboardController(StoreDbContext db) : ControllerBase
{
    /// <summary>Numbers for the home page: stock totals, low-stock warnings and today's activity.</summary>
    /// <param name="utcOffsetMinutes">The PC's offset from UTC, so "today" means the local day (e.g. 180 for UTC+3).</param>
    [HttpGet("summary")]
    public async Task<DashboardSummaryDto> Summary([FromQuery] int utcOffsetMinutes = 0, CancellationToken ct = default)
    {
        var offset = TimeSpan.FromMinutes(Math.Clamp(utcOffsetMinutes, -14 * 60, 14 * 60));
        var localNow = DateTime.UtcNow + offset;
        var todayStart = localNow.Date - offset;
        var monthStart = new DateTime(localNow.Year, localNow.Month, 1) - offset;

        var active = db.Items.Where(i => i.IsActive && i.Category.IsActive);
        var lowStock = await active.Include(i => i.Category)
            .Where(i => i.LowStockThreshold > 0 && i.Quantity <= i.LowStockThreshold)
            .OrderBy(i => i.Quantity).ThenBy(i => i.Name).Take(20).ToListAsync(ct);
        var completed = db.Orders.Where(o => o.Status == OrderStatus.Completed);

        return new DashboardSummaryDto
        {
            CategoryCount = await db.Categories.CountAsync(c => c.IsActive, ct),
            ItemCount = await active.CountAsync(ct),
            TotalUnitsInStock = await active.SumAsync(i => (int?)i.Quantity, ct) ?? 0,
            LowStockItemCount = await active.CountAsync(i => i.LowStockThreshold > 0 && i.Quantity <= i.LowStockThreshold, ct),
            OutOfStockItemCount = await active.CountAsync(i => i.Quantity == 0, ct),
            CustomerCount = await db.Customers.CountAsync(c => c.IsActive, ct),
            OrdersToday = await completed.CountAsync(o => o.CreatedAt >= todayStart, ct),
            UnitsOutToday = await db.OrderLines.Where(l => l.Order.Status == OrderStatus.Completed && l.Order.CreatedAt >= todayStart)
                .SumAsync(l => (int?)l.Quantity, ct) ?? 0,
            OrdersThisMonth = await completed.CountAsync(o => o.CreatedAt >= monthStart, ct),
            LowStockItems = lowStock.Select(i => i.ToDto()).ToList(),
        };
    }
}
