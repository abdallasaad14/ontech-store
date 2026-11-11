using System.Net;
using System.Text.Json;
using OnTech.Store.Contracts;
using Xunit;

namespace OnTech.Store.Api.Tests;

/// <summary>
/// Issue voucher types (sale / demo) and receive types (purchase / demo return):
/// demos leave stock like a sale, come back only against the demo they went out on, and never come back more than went out.
/// </summary>
public class DemoVoucherTests : IClassFixture<StoreApiFactory>
{
    private readonly StoreApiFactory _f;
    public DemoVoucherTests(StoreApiFactory f) => _f = f;

    private async Task<(HttpClient admin, ItemDto item, CustomerDto customer, SupplierDto supplier)> SetupAsync(int qty = 20)
    {
        var admin = await _f.LoginAsync();
        var tag = FixB.Tag();
        var cat = await (await admin.Post("/api/categories", new SaveCategoryRequest { Name = $"Demo cat {tag}" })).Read<CategoryDto>();
        var item = await admin.AddItem(new CreateItemRequest { CategoryId = cat.Id, Name = $"Demo item {tag}" }, qty);
        var customer = await (await admin.Post("/api/customers", new SaveCustomerRequest { CompanyName = $"Demo customer {tag}" })).Read<CustomerDto>();
        var supplier = await (await admin.Post("/api/suppliers", new SaveSupplierRequest { CompanyName = $"Demo supplier {tag}" })).Read<SupplierDto>();
        return (admin, item, customer, supplier);
    }

    private static async Task<OrderDto> DemoAsync(HttpClient admin, int customerId, int itemId, int qty) =>
        await (await admin.Post("/api/orders", new CreateOrderRequest
        {
            Type = IssueType.Demo, IssueVoucherNumber = TestStock.Voucher(), CustomerId = customerId,
            Lines = { new OrderLineRequest { ItemId = itemId, Quantity = qty } },
        })).Read<OrderDto>();

    private static Task<HttpResponseMessage> ReturnAsync(HttpClient admin, int customerId, params (int itemId, int lineId, int qty)[] lines)
    {
        var req = new CreateStockInRequest { Type = ReceiveType.DemoReturn, CustomerId = customerId, VoucherNumber = TestStock.Voucher() };
        foreach (var (itemId, lineId, qty) in lines)
            req.Lines.Add(new StockInLineRequest { ItemId = itemId, Quantity = qty, DemoOrderLineId = lineId });
        return admin.Post("/api/stock-ins", req);
    }

    private static Task<int> QtyAsync(HttpClient admin, int itemId) =>
        admin.GetAs<ItemDto>($"/api/items/{itemId}").ContinueWith(t => t.Result.Quantity);

    [Fact]
    public async Task Demo_takes_stock_out_and_shows_as_open()
    {
        var (admin, item, customer, _) = await SetupAsync();
        var demo = await DemoAsync(admin, customer.Id, item.Id, 3);

        Assert.Equal(IssueType.Demo, demo.Type);
        Assert.Equal(3, demo.OpenQuantity);
        Assert.Equal(17, await QtyAsync(admin, item.Id));
        var open = await admin.GetAs<List<OpenDemoLineDto>>($"/api/demos/open?customerId={customer.Id}");
        var line = Assert.Single(open);
        Assert.Equal(demo.Id, line.OrderId);
        Assert.Equal(3, line.OpenQuantity);
        Assert.Contains((await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?type=Demo&customerId={customer.Id}")).Items, o => o.Id == demo.Id);
    }

    [Fact]
    public async Task Order_without_a_type_is_a_sale_and_a_demo_cannot_have_a_receipt()
    {
        var (admin, item, customer, _) = await SetupAsync();
        var sale = await (await admin.Post("/api/orders", new { issueVoucherNumber = TestStock.Voucher(), customerId = customer.Id, lines = new[] { new { itemId = item.Id, quantity = 1 } } })).Read<OrderDto>();
        Assert.Equal(IssueType.Sale, sale.Type);
        Assert.Empty(await admin.GetAs<List<OpenDemoLineDto>>($"/api/demos/open?customerId={customer.Id}"));

        var withReceipt = await admin.Post("/api/orders", new CreateOrderRequest
        {
            Type = IssueType.Demo, IssueVoucherNumber = TestStock.Voucher(), CustomerId = customer.Id, WithReceipt = true, ReceiptNumber = $"R{FixB.Tag()}",
            Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 1 } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, withReceipt.StatusCode);
    }

    [Fact]
    public async Task Partial_return_then_the_rest_closes_the_demo()
    {
        var (admin, item, customer, _) = await SetupAsync();
        var demo = await DemoAsync(admin, customer.Id, item.Id, 5);
        var lineId = demo.Lines[0].Id;

        var first = await (await ReturnAsync(admin, customer.Id, (item.Id, lineId, 2))).Read<StockInDto>();
        Assert.Equal(ReceiveType.DemoReturn, first.Type);
        Assert.Equal(customer.Id, first.CustomerId);
        Assert.Null(first.SupplierId);
        Assert.Equal(17, await QtyAsync(admin, item.Id));
        var after = await admin.GetAs<OrderDto>($"/api/orders/{demo.Id}");
        Assert.Equal(2, after.ReturnedQuantity);
        Assert.Equal(3, after.OpenQuantity);

        await (await ReturnAsync(admin, customer.Id, (item.Id, lineId, 3))).Read<StockInDto>();
        Assert.Equal(20, await QtyAsync(admin, item.Id));
        Assert.Empty(await admin.GetAs<List<OpenDemoLineDto>>($"/api/demos/open?customerId={customer.Id}"));
    }

    [Fact]
    public async Task Returning_more_than_went_out_is_refused_and_nothing_is_saved()
    {
        var (admin, item, customer, _) = await SetupAsync();
        var demo = await DemoAsync(admin, customer.Id, item.Id, 2);
        var lineId = demo.Lines[0].Id;

        var res = await ReturnAsync(admin, customer.Id, (item.Id, lineId, 1), (item.Id, lineId, 2)); // 3 in total
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(ErrorCodes.OverReturn, (await res.ProblemAsync()).Code);
        Assert.Equal(18, await QtyAsync(admin, item.Id));
        Assert.Equal(0, (await admin.GetAs<OrderDto>($"/api/orders/{demo.Id}")).ReturnedQuantity);
    }

    [Fact]
    public async Task Return_must_match_the_customer_a_completed_demo_and_its_item()
    {
        var (admin, item, customer, _) = await SetupAsync();
        var (_, item2, other, _) = await SetupAsync();
        var demo = await DemoAsync(admin, customer.Id, item.Id, 2);
        var lineId = demo.Lines[0].Id;

        Assert.Equal(HttpStatusCode.BadRequest, (await ReturnAsync(admin, other.Id, (item.Id, lineId, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ReturnAsync(admin, customer.Id, (item2.Id, lineId, 1))).StatusCode);

        var sale = await (await admin.Post("/api/orders", new CreateOrderRequest
        {
            IssueVoucherNumber = TestStock.Voucher(), CustomerId = customer.Id, Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 1 } },
        })).Read<OrderDto>();
        Assert.Equal(HttpStatusCode.BadRequest, (await ReturnAsync(admin, customer.Id, (item.Id, sale.Lines[0].Id, 1))).StatusCode);

        var cancelled = await DemoAsync(admin, customer.Id, item.Id, 1);
        (await admin.Post($"/api/orders/{cancelled.Id}/cancel", new CancelOrderRequest { Reason = "test" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await ReturnAsync(admin, customer.Id, (item.Id, cancelled.Lines[0].Id, 1))).StatusCode);

        // A return line without its demo line is refused too.
        var noLine = new CreateStockInRequest
        {
            Type = ReceiveType.DemoReturn, CustomerId = customer.Id, VoucherNumber = TestStock.Voucher(),
            Lines = { new StockInLineRequest { ItemId = item.Id, Quantity = 1 } },
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/stock-ins", noLine)).StatusCode);
    }

    [Fact]
    public async Task Purchase_and_return_each_take_their_own_party()
    {
        var (admin, item, customer, supplier) = await SetupAsync();
        var demo = await DemoAsync(admin, customer.Id, item.Id, 1);

        var purchaseWithCustomer = new CreateStockInRequest
        {
            SupplierId = supplier.Id, CustomerId = customer.Id, VoucherNumber = TestStock.Voucher(),
            Lines = { new StockInLineRequest { ItemId = item.Id, Quantity = 1 } },
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/stock-ins", purchaseWithCustomer)).StatusCode);
        var purchaseWithoutSupplier = new CreateStockInRequest
        {
            VoucherNumber = TestStock.Voucher(), Lines = { new StockInLineRequest { ItemId = item.Id, Quantity = 1 } },
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/stock-ins", purchaseWithoutSupplier)).StatusCode);
        var returnWithSupplier = new CreateStockInRequest
        {
            Type = ReceiveType.DemoReturn, SupplierId = supplier.Id, CustomerId = customer.Id, VoucherNumber = TestStock.Voucher(),
            Lines = { new StockInLineRequest { ItemId = item.Id, Quantity = 1, DemoOrderLineId = demo.Lines[0].Id } },
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/stock-ins", returnWithSupplier)).StatusCode);
    }

    [Fact]
    public async Task Two_PCs_returning_the_last_unit_at_once_only_one_wins()
    {
        var (admin, item, customer, _) = await SetupAsync();
        var demo = await DemoAsync(admin, customer.Id, item.Id, 1);
        var lineId = demo.Lines[0].Id;
        var pcs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _f.LoginAsync()));

        var results = await Task.WhenAll(pcs.Select(pc => ReturnAsync(pc, customer.Id, (item.Id, lineId, 1))));

        Assert.Single(results, r => r.IsSuccessStatusCode);
        Assert.All(results.Where(r => !r.IsSuccessStatusCode), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(20, await QtyAsync(admin, item.Id));
        Assert.Equal(1, (await admin.GetAs<OrderDto>($"/api/orders/{demo.Id}")).ReturnedQuantity);
    }

    [Fact]
    public async Task Demo_with_returns_cannot_be_cancelled_until_its_returns_are()
    {
        var (admin, item, customer, _) = await SetupAsync();
        var demo = await DemoAsync(admin, customer.Id, item.Id, 3);
        var ret = await (await ReturnAsync(admin, customer.Id, (item.Id, demo.Lines[0].Id, 1))).Read<StockInDto>();

        var refused = await admin.Post($"/api/orders/{demo.Id}/cancel", new CancelOrderRequest { Reason = "test" });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        // Cancelling the return puts the unit back on the demo and takes it out of stock again.
        (await admin.Post($"/api/stock-ins/{ret.Id}/cancel", new CancelStockInRequest { Reason = "typo" })).EnsureSuccessStatusCode();
        Assert.Equal(3, (await admin.GetAs<OrderDto>($"/api/orders/{demo.Id}")).OpenQuantity);
        Assert.Equal(17, await QtyAsync(admin, item.Id));

        (await admin.Post($"/api/orders/{demo.Id}/cancel", new CancelOrderRequest { Reason = "test" })).EnsureSuccessStatusCode();
        Assert.Equal(20, await QtyAsync(admin, item.Id));
        Assert.Empty(await admin.GetAs<List<OpenDemoLineDto>>($"/api/demos/open?customerId={customer.Id}"));
    }

    [Fact]
    public async Task Only_an_admin_can_change_an_order_type()
    {
        var (admin, item, customer, _) = await SetupAsync();
        var sale = await (await admin.Post("/api/orders", new CreateOrderRequest
        {
            IssueVoucherNumber = TestStock.Voucher(), CustomerId = customer.Id, Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 2 } },
        })).Read<OrderDto>();
        var clerk = await _f.UserWithAsync(admin, Permission.CreateOrders | Permission.ViewOrders | Permission.CancelOrders | Permission.AddStock);

        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.Post($"/api/orders/{sale.Id}/type", new ChangeOrderTypeRequest { Type = IssueType.Demo })).StatusCode);

        var demo = await (await admin.Post($"/api/orders/{sale.Id}/type", new ChangeOrderTypeRequest { Type = IssueType.Demo })).Read<OrderDto>();
        Assert.Equal(IssueType.Demo, demo.Type);
        Assert.Equal(18, await QtyAsync(admin, item.Id)); // stock is not touched

        await (await ReturnAsync(admin, customer.Id, (item.Id, demo.Lines[0].Id, 1))).Read<StockInDto>();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post($"/api/orders/{sale.Id}/type", new ChangeOrderTypeRequest { Type = IssueType.Sale })).StatusCode); // demo -> sale is never allowed

        var withReceipt = await (await admin.Post("/api/orders", new CreateOrderRequest
        {
            IssueVoucherNumber = TestStock.Voucher(), CustomerId = customer.Id, WithReceipt = true, ReceiptNumber = $"R{FixB.Tag()}",
            Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 1 } },
        })).Read<OrderDto>();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post($"/api/orders/{withReceipt.Id}/type", new ChangeOrderTypeRequest { Type = IssueType.Demo })).StatusCode);
    }

    [Fact]
    public async Task Item_card_shows_the_types_and_top_customers_count_sales_only()
    {
        var (admin, item, customer, _) = await SetupAsync();
        var demo = await DemoAsync(admin, customer.Id, item.Id, 4);
        await (await ReturnAsync(admin, customer.Id, (item.Id, demo.Lines[0].Id, 4))).Read<StockInDto>();

        var card = await admin.GetAs<ItemCardDto>($"/api/items/{item.Id}/card");
        Assert.Contains(card.Lines, l => l.IssueType == IssueType.Demo && l.Out == 4);
        Assert.Contains(card.Lines, l => l.ReceiveType == ReceiveType.DemoReturn && l.In == 4 && l.PartyName == customer.CompanyName);
        Assert.Empty(card.TopCustomers);
        Assert.Equal(20, card.ClosingBalance);
    }
}
