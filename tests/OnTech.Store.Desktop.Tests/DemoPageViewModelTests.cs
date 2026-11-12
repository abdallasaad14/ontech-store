using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;
using OnTech.Store.Desktop.ViewModels;
using Xunit;

namespace OnTech.Store.Desktop.Tests;

/// <summary>The Demo page and the logs it takes demos out of (one number book, two places).</summary>
public class DemoPageViewModelTests
{
    private static OpenDemoLineDto Line(int lineId, int orderId, int customerId, string customer, int itemId, string item, int qty, int back = 0) => new()
    {
        OrderLineId = lineId, OrderId = orderId, IssueVoucherNumber = $"D{orderId}", CreatedAt = DateTimeOffset.Now.AddDays(-3),
        CustomerId = customerId, CustomerName = customer, ItemId = itemId, ItemName = item, CategoryName = "Cat",
        Quantity = qty, ReturnedQuantity = back, OpenQuantity = qty - back,
    };

    private static string Query(FakeServer server, string path) =>
        server.Requests.Last(r => r.RequestUri!.AbsolutePath.TrimStart('/') == path).RequestUri!.Query;

    [Fact]
    public async Task Orders_log_asks_for_sales_and_the_demo_tab_for_demos_by_state()
    {
        var server = new FakeServer().WithEmptyLists();
        var session = TestSession.Create(server, Permission.ViewOrders);
        var shell = new ShellViewModel(session);

        var sales = new OrdersViewModel(session, shell);
        await sales.LoadAsync();
        Assert.Contains("type=Sale", Query(server, "api/orders"));
        Assert.DoesNotContain("demoState", Query(server, "api/orders"));

        var demos = new OrdersViewModel(session, shell, IssueType.Demo);
        await demos.LoadAsync();
        Assert.Contains("type=Demo", Query(server, "api/orders"));
        demos.DemoStateFilterIndex = 3; // part back
        await Wait.For(() => Query(server, "api/orders").Contains("demoState=PartlyBack"));

        var purchases = new StockInsViewModel(session, shell);
        await purchases.LoadAsync();
        Assert.Contains("type=Purchase", Query(server, "api/stock-ins"));
        var returns = new StockInsViewModel(session, shell, ReceiveType.DemoReturn);
        await returns.LoadAsync();
        Assert.Contains("type=DemoReturn", Query(server, "api/stock-ins"));
    }

    [Fact]
    public async Task A_demo_number_searched_in_the_sales_log_says_where_it_is()
    {
        var server = new FakeServer().WithEmptyLists()
            .OnJson("GET", "api/orders", req => req.RequestUri!.Query.Contains("type=Demo")
                ? new PagedResult<OrderDto> { Items = [new OrderDto { Id = 7, IssueVoucherNumber = "5501", Type = IssueType.Demo }], TotalCount = 1, Page = 1, PageSize = 1 }
                : new PagedResult<OrderDto> { Items = [] });
        var session = TestSession.Create(server, Permission.ViewOrders);
        var shell = new ShellViewModel(session);
        var vm = new OrdersViewModel(session, shell) { Search = "5501" };
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal("No sales match. 5501 is a demo voucher: it is on the Demo page.", vm.OtherTypeHint);
        vm.OpenOtherTypeCommand.Execute(null);
        var page = Assert.IsType<DemosViewModel>(shell.CurrentPage);
        Assert.True(page.IsIssuesTab);
        Assert.Equal("5501", page.Issues!.Search);

        // A search that finds sales shows no hint.
        vm.Search = null;
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Null(vm.OtherTypeHint);
    }

    [Fact]
    public async Task With_customers_now_groups_by_customer_or_item_and_counts_what_is_out()
    {
        var server = new FakeServer().WithEmptyLists().OnJson("GET", "api/demos/open", new[]
        {
            Line(1, 10, 1, "Acme", 100, "Scanner", 3, back: 1),
            Line(2, 10, 1, "Acme", 101, "Printer", 1),
            Line(3, 11, 2, "Beta", 100, "Scanner", 4),
        });
        var session = TestSession.Create(server, Permission.ViewOrders | Permission.AddStock);
        var vm = new DemosViewModel(session, new ShellViewModel(session));
        await vm.LoadAsync();

        Assert.Equal(7, vm.OpenUnits);
        Assert.Equal(2, vm.OpenCustomers);
        Assert.Equal("7 unit(s) out with 2 customer(s)", vm.SummaryText);
        Assert.Equal([new DemoGroup("Acme", 3), new DemoGroup("Beta", 4)], vm.Rows.Select(r => r.Group).Distinct());

        vm.GroupMode = 1;
        Assert.Equal([new DemoGroup("Printer", 1), new DemoGroup("Scanner", 6)], vm.Rows.Select(r => r.Group).Distinct());
        Assert.All(vm.Rows, r => Assert.Equal(3, r.DaysOut));

        vm.Search = "acme";
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Contains("search=acme", Query(server, "api/demos/open"));
    }

    [Fact]
    public async Task Return_on_a_line_opens_a_demo_return_for_that_customer_with_the_line_filled()
    {
        var line = Line(5, 20, 3, "Gamma", 100, "Scanner", 4, back: 1);
        var server = new FakeServer().WithEmptyLists()
            .OnJson("GET", "api/customers", new[] { new CustomerDto { Id = 3, CompanyName = "Gamma", IsActive = true }, new CustomerDto { Id = 4, CompanyName = "Delta", IsActive = true } })
            .OnJson("GET", "api/demos/open", new[] { line });
        var session = TestSession.Create(server, Permission.AddStock);
        var shell = new ShellViewModel(session);
        shell.Navigate(Page.Demos);
        var demos = Assert.IsType<DemosViewModel>(shell.CurrentPage);
        await Wait.For(() => demos.Rows.Count == 1);
        Assert.False(demos.CanSeeLogs);
        Assert.Null(demos.Issues);

        demos.ReturnLineCommand.Execute(demos.Rows[0]);
        var form = Assert.IsType<StockInViewModel>(shell.CurrentPage);
        Assert.True(form.IsDemoReturn);
        Assert.Equal(Page.DemoReturn, shell.CurrentPageKind);
        Assert.Equal(Page.Demos, shell.SelectedMainItem!.Page);
        await Wait.For(() => form.SelectedCustomer?.Id == 3 && form.OpenDemos.Count == 1);
        await Wait.For(() => form.OpenDemos[0].Lines[0].ReturnQuantity == 3);
    }

    [Fact]
    public void New_order_and_stock_in_are_fixed_to_one_type()
    {
        var session = TestSession.Create(new FakeServer().WithEmptyLists(), Permission.CreateOrders | Permission.AddStock);
        var shell = new ShellViewModel(session);
        Assert.False(new NewOrderViewModel(session, shell).IsDemo);
        Assert.True(new NewOrderViewModel(session, shell, IssueType.Demo).IsDemo);
        Assert.False(new StockInViewModel(session, shell).IsDemoReturn);
        Assert.True(new StockInViewModel(session, shell, ReceiveType.DemoReturn).IsDemoReturn);

        shell.Navigate(Page.NewDemo);
        Assert.True(Assert.IsType<NewOrderViewModel>(shell.CurrentPage).IsDemo);
        Assert.Equal(Page.Demos, shell.SelectedMainItem!.Page);
    }

    [Fact]
    public void Demo_state_reads_from_the_quantities()
    {
        Assert.Equal("Out", OnTech.Store.Desktop.DemoStateConverter.Describe(new OrderDto { Type = IssueType.Demo, TotalQuantity = 2, OpenQuantity = 2 }));
        Assert.Equal("Part back", OnTech.Store.Desktop.DemoStateConverter.Describe(new OrderDto { Type = IssueType.Demo, ReturnedQuantity = 1, OpenQuantity = 1 }));
        Assert.Equal("All back", OnTech.Store.Desktop.DemoStateConverter.Describe(new OrderDto { Type = IssueType.Demo, ReturnedQuantity = 2, OpenQuantity = 0 }));
        Assert.Equal("Cancelled", OnTech.Store.Desktop.DemoStateConverter.Describe(new OrderDto { Type = IssueType.Demo, Status = OrderStatus.Cancelled }));
    }
}
