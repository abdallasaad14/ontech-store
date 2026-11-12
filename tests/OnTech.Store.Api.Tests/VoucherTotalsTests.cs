using OnTech.Store.Contracts;
using Xunit;

namespace OnTech.Store.Api.Tests;

/// <summary>Owner's request: all issue vouchers of one customer, or receipt vouchers of one supplier, with their totals.</summary>
public class VoucherTotalsTests : IClassFixture<StoreApiFactory>
{
    private readonly StoreApiFactory _f;
    public VoucherTotalsTests(StoreApiFactory f) => _f = f;

    private static async Task<OrderDto> IssueAsync(HttpClient admin, int customerId, int itemId, int qty) =>
        await (await admin.Post("/api/orders", new CreateOrderRequest
        {
            IssueVoucherNumber = TestStock.Voucher(), CustomerId = customerId, Lines = { new OrderLineRequest { ItemId = itemId, Quantity = qty } },
        })).Read<OrderDto>();

    [Fact]
    public async Task Orders_log_totals_one_customer_over_every_page_without_cancelled()
    {
        var admin = await _f.LoginAsync();
        var (_, item, customer, _) = await FixB.SeedAsync(admin);
        var (_, _, other, _) = await FixB.SeedAsync(admin);
        await IssueAsync(admin, customer.Id, item.Id, 2);
        await IssueAsync(admin, customer.Id, item.Id, 3);
        var cancelled = await IssueAsync(admin, customer.Id, item.Id, 4);
        (await admin.Post($"/api/orders/{cancelled.Id}/cancel", new CancelOrderRequest { Reason = "test" })).EnsureSuccessStatusCode();
        await IssueAsync(admin, other.Id, item.Id, 5);

        // Page size 1: the totals still cover the whole list.
        var page = await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}&pageSize=1");
        Assert.Single(page.Items);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Totals!.Vouchers);
        Assert.Equal(5, page.Totals.Quantity);

        var none = await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}&status=Cancelled&type=Demo");
        Assert.Equal(0, none.Totals!.Vouchers);
        Assert.Equal(0, none.Totals.Quantity);
    }

    [Fact]
    public async Task Stock_in_log_totals_one_supplier_without_cancelled()
    {
        var admin = await _f.LoginAsync();
        var (_, item, _, supplier) = await FixB.SeedAsync(admin);
        var (_, _, _, other) = await FixB.SeedAsync(admin);
        await admin.StockIn(item.Id, 5, supplier);
        var lines = await (await admin.Post("/api/stock-ins", new CreateStockInRequest
        {
            SupplierId = supplier.Id, VoucherNumber = TestStock.Voucher(),
            Lines = { new StockInLineRequest { ItemId = item.Id, Quantity = 1 }, new StockInLineRequest { ItemId = item.Id, Quantity = 6 } },
        })).Read<StockInDto>();
        var cancelled = await admin.StockIn(item.Id, 2, supplier);
        (await admin.Post($"/api/stock-ins/{cancelled.Id}/cancel", new CancelStockInRequest { Reason = "test" })).EnsureSuccessStatusCode();
        await admin.StockIn(item.Id, 9, other);

        var page = await admin.GetAs<PagedResult<StockInDto>>($"/api/stock-ins?type=Purchase&supplierId={supplier.Id}&pageSize=1");
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Totals!.Vouchers);
        Assert.Equal(12, page.Totals.Quantity);
        Assert.Equal(7, lines.TotalQuantity);
    }
}
