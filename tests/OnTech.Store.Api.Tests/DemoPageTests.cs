using System.Net;
using OnTech.Store.Client;
using OnTech.Store.Contracts;
using Xunit;

namespace OnTech.Store.Api.Tests;

/// <summary>
/// What the separate Demo page reads: the logs split by type (one number book), the demo state filter,
/// search on what is still out, and the open-demo numbers on the home page.
/// </summary>
public class DemoPageTests : IClassFixture<StoreApiFactory>
{
    private readonly StoreApiFactory _f;
    public DemoPageTests(StoreApiFactory f) => _f = f;

    private static async Task<OrderDto> IssueAsync(HttpClient admin, IssueType type, int customerId, string voucher, params (int itemId, int qty)[] lines)
    {
        var req = new CreateOrderRequest { Type = type, IssueVoucherNumber = voucher, CustomerId = customerId };
        foreach (var (itemId, qty) in lines) req.Lines.Add(new OrderLineRequest { ItemId = itemId, Quantity = qty });
        return await (await admin.Post("/api/orders", req)).Read<OrderDto>();
    }

    private static async Task<StockInDto> ReturnAsync(HttpClient admin, int customerId, int itemId, int lineId, int qty, string? voucher = null) =>
        await (await admin.Post("/api/stock-ins", new CreateStockInRequest
        {
            Type = ReceiveType.DemoReturn, CustomerId = customerId, VoucherNumber = voucher ?? TestStock.Voucher(),
            Lines = { new StockInLineRequest { ItemId = itemId, Quantity = qty, DemoOrderLineId = lineId } },
        })).Read<StockInDto>();

    private static async Task<List<int>> OrderIdsAsync(HttpClient admin, string query) =>
        (await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?{query}&pageSize=200")).Items.Select(o => o.Id).ToList();

    [Fact]
    public async Task Sale_and_purchase_logs_leave_demos_out_but_share_the_number_books()
    {
        var admin = await _f.LoginAsync();
        var (_, item, customer, supplier) = await FixB.SeedAsync(admin);
        var sale = await IssueAsync(admin, IssueType.Sale, customer.Id, TestStock.Voucher(), (item.Id, 1));
        var demo = await IssueAsync(admin, IssueType.Demo, customer.Id, TestStock.Voucher(), (item.Id, 2));

        var sales = await OrderIdsAsync(admin, $"type=Sale&customerId={customer.Id}");
        Assert.Contains(sale.Id, sales);
        Assert.DoesNotContain(demo.Id, sales);
        Assert.Equal([demo.Id], await OrderIdsAsync(admin, $"type=Demo&customerId={customer.Id}"));

        // One issue book: a sale can't reuse a demo's number, and the other way round.
        var dupSale = await admin.Post("/api/orders", new CreateOrderRequest
        {
            IssueVoucherNumber = demo.IssueVoucherNumber!, CustomerId = customer.Id, Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 1 } },
        });
        Assert.Equal(HttpStatusCode.Conflict, dupSale.StatusCode);

        // One receive book: a purchase can't reuse a demo return's number.
        var ret = await ReturnAsync(admin, customer.Id, item.Id, demo.Lines[0].Id, 1);
        var purchase = await admin.StockIn(item.Id, 1, supplier);
        var dupPurchase = await admin.Post("/api/stock-ins", new CreateStockInRequest
        {
            SupplierId = supplier.Id, VoucherNumber = ret.VoucherNumber, Lines = { new StockInLineRequest { ItemId = item.Id, Quantity = 1 } },
        });
        Assert.Equal(HttpStatusCode.Conflict, dupPurchase.StatusCode);

        var purchases = (await admin.GetAs<PagedResult<StockInDto>>($"/api/stock-ins?type=Purchase&supplierId={supplier.Id}&pageSize=200")).Items.Select(s => s.Id).ToList();
        Assert.Contains(purchase.Id, purchases);
        Assert.DoesNotContain(ret.Id, purchases);
        var returns = (await admin.GetAs<PagedResult<StockInDto>>($"/api/stock-ins?type=DemoReturn&customerId={customer.Id}&pageSize=200")).Items;
        Assert.Equal(ret.Id, Assert.Single(returns).Id);
    }

    [Fact]
    public async Task Demo_state_filter_follows_what_came_back()
    {
        var admin = await _f.LoginAsync();
        var (_, item, customer, _) = await FixB.SeedAsync(admin);
        var sale = await IssueAsync(admin, IssueType.Sale, customer.Id, TestStock.Voucher(), (item.Id, 1));
        var nothingBack = await IssueAsync(admin, IssueType.Demo, customer.Id, TestStock.Voucher(), (item.Id, 2));
        var partly = await IssueAsync(admin, IssueType.Demo, customer.Id, TestStock.Voucher(), (item.Id, 3));
        var allBack = await IssueAsync(admin, IssueType.Demo, customer.Id, TestStock.Voucher(), (item.Id, 1));
        var cancelled = await IssueAsync(admin, IssueType.Demo, customer.Id, TestStock.Voucher(), (item.Id, 1));
        await ReturnAsync(admin, customer.Id, item.Id, partly.Lines[0].Id, 1);
        await ReturnAsync(admin, customer.Id, item.Id, allBack.Lines[0].Id, 1);
        (await admin.Post($"/api/orders/{cancelled.Id}/cancel", new CancelOrderRequest { Reason = "test" })).EnsureSuccessStatusCode();

        var c = $"customerId={customer.Id}";
        Assert.Equal(new[] { nothingBack.Id, partly.Id }.Order(), (await OrderIdsAsync(admin, $"{c}&demoState=StillOut")).Order());
        Assert.Equal([nothingBack.Id], await OrderIdsAsync(admin, $"{c}&demoState=NothingBack"));
        Assert.Equal([partly.Id], await OrderIdsAsync(admin, $"{c}&demoState=PartlyBack"));
        Assert.Equal([allBack.Id], await OrderIdsAsync(admin, $"{c}&demoState=AllBack"));
        Assert.DoesNotContain(sale.Id, await OrderIdsAsync(admin, $"{c}&demoState=StillOut"));

        // Through the desktop's client too.
        var api = new StoreApiClient(_f.CreateClient());
        await api.LoginAsync(StoreApiFactory.AdminUsername, StoreApiFactory.AdminPassword);
        var viaClient = await api.SearchOrdersAsync(new OrderQuery { CustomerId = customer.Id, DemoState = DemoState.PartlyBack });
        Assert.Equal(partly.Id, Assert.Single(viaClient.Items).Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/orders?{c}&demoState=99")).StatusCode);
    }

    [Fact]
    public async Task Open_demos_can_be_searched_by_voucher_customer_and_item()
    {
        var admin = await _f.LoginAsync();
        var (_, item, customer, _) = await FixB.SeedAsync(admin);
        var (_, other, otherCustomer, _) = await FixB.SeedAsync(admin);
        var voucher = $"DP{FixB.Tag()}";
        var demo = await IssueAsync(admin, IssueType.Demo, customer.Id, voucher, (item.Id, 2));
        var otherDemo = await IssueAsync(admin, IssueType.Demo, otherCustomer.Id, TestStock.Voucher(), (other.Id, 1));

        async Task<List<int>> Search(string s) =>
            (await admin.GetAs<List<OpenDemoLineDto>>($"/api/demos/open?search={Uri.EscapeDataString(s)}")).Select(l => l.OrderId).Distinct().ToList();

        Assert.Equal([demo.Id], await Search(voucher.ToLowerInvariant()));
        Assert.Equal([demo.Id], await Search(customer.CompanyName.ToUpperInvariant()));
        Assert.Equal([demo.Id], await Search(item.Name));
        Assert.Equal([otherDemo.Id], await Search(other.Name));
        if (customer.CustomerNumber is { } number)
            Assert.Contains(demo.Id, await Search(number.ToString()));

        var api = new StoreApiClient(_f.CreateClient());
        await api.LoginAsync(StoreApiFactory.AdminUsername, StoreApiFactory.AdminPassword);
        Assert.Equal(demo.Id, Assert.Single(await api.GetOpenDemosAsync(new OpenDemoQuery { Search = voucher })).OrderId);
    }

    [Fact]
    public async Task Home_summary_counts_demo_units_still_out_and_who_has_them()
    {
        var admin = await _f.LoginAsync();
        var (_, item, customer, _) = await FixB.SeedAsync(admin);
        var (_, _, otherCustomer, _) = await FixB.SeedAsync(admin);
        var before = await admin.GetAs<DashboardSummaryDto>("/api/dashboard/summary");

        var demo = await IssueAsync(admin, IssueType.Demo, customer.Id, TestStock.Voucher(), (item.Id, 4));
        await IssueAsync(admin, IssueType.Demo, otherCustomer.Id, TestStock.Voucher(), (item.Id, 1));
        await IssueAsync(admin, IssueType.Sale, customer.Id, TestStock.Voucher(), (item.Id, 5));
        var mid = await admin.GetAs<DashboardSummaryDto>("/api/dashboard/summary");
        Assert.Equal(before.OpenDemoQuantity + 5, mid.OpenDemoQuantity);
        Assert.Equal(before.OpenDemoCustomers + 2, mid.OpenDemoCustomers);

        var ret = await ReturnAsync(admin, customer.Id, item.Id, demo.Lines[0].Id, 4);
        var after = await admin.GetAs<DashboardSummaryDto>("/api/dashboard/summary");
        Assert.Equal(before.OpenDemoQuantity + 1, after.OpenDemoQuantity);
        Assert.Equal(before.OpenDemoCustomers + 1, after.OpenDemoCustomers);

        // Cancelling the return puts it back out.
        (await admin.Post($"/api/stock-ins/{ret.Id}/cancel", new CancelStockInRequest { Reason = "typo" })).EnsureSuccessStatusCode();
        var reopened = await admin.GetAs<DashboardSummaryDto>("/api/dashboard/summary");
        Assert.Equal(mid.OpenDemoQuantity, reopened.OpenDemoQuantity);
        Assert.Equal(mid.OpenDemoCustomers, reopened.OpenDemoCustomers);
    }

    [Fact]
    public async Task Open_demos_need_add_stock_or_view_orders()
    {
        var admin = await _f.LoginAsync();
        var stockOnly = await _f.UserWithAsync(admin, Permission.ViewStock);
        Assert.Equal(HttpStatusCode.Forbidden, (await stockOnly.GetAsync("/api/demos/open?search=x")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stockOnly.GetAsync("/api/orders?demoState=StillOut")).StatusCode);
        var viewer = await _f.UserWithAsync(admin, Permission.ViewOrders);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/demos/open?search=x")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/orders?demoState=StillOut")).StatusCode);
    }
}
