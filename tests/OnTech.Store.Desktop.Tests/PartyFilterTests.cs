using OnTech.Store.Contracts;
using OnTech.Store.Desktop.ViewModels;
using Xunit;

namespace OnTech.Store.Desktop.Tests;

/// <summary>The customer / supplier filter of the voucher logs and the totals line under them.</summary>
public class PartyFilterTests
{
    private static string Query(FakeServer server, string path) =>
        server.Requests.Last(r => r.RequestUri!.AbsolutePath.TrimStart('/') == path).RequestUri!.Query;

    [Fact]
    public async Task Picking_a_customer_lists_only_their_orders_with_totals()
    {
        var server = new FakeServer().WithEmptyLists()
            .OnJson("GET", "api/customers", new[] { new CustomerDto { Id = 3, CustomerNumber = 12, CompanyName = "Gamma" }, new CustomerDto { Id = 4, CustomerNumber = 13, CompanyName = "Delta" } })
            .OnJson("GET", "api/orders", new PagedResult<OrderDto> { Items = [], Totals = new VoucherTotals { Vouchers = 4, Quantity = 37 } });
        var session = TestSession.Create(server, Permission.ViewOrders);
        var vm = new OrdersViewModel(session, new ShellViewModel(session));
        await vm.LoadAsync();
        Assert.Equal(2, vm.Party.Choices.Count);
        Assert.Null(vm.Party.Selected);
        Assert.DoesNotContain("customerId", Query(server, "api/orders"));
        Assert.Equal("Total: 4 vouchers · 37 pieces", vm.TotalsText);

        vm.Party.Search = "١٣"; // Arabic-Indic digits: one match is picked straight away
        Assert.Equal(4, vm.Party.Selected!.Id);
        await Wait.For(() => Query(server, "api/orders").Contains("customerId=4"));

        vm.Party.ClearCommand.Execute(null);
        Assert.Equal(2, vm.Party.Choices.Count);
        await Wait.For(() => !Query(server, "api/orders").Contains("customerId"));
    }

    [Fact]
    public async Task Stock_in_log_filters_by_supplier_and_demo_returns_by_customer()
    {
        var server = new FakeServer().WithEmptyLists()
            .OnJson("GET", "api/suppliers", new[] { new SupplierDto { Id = 8, SupplierNumber = 1, CompanyName = "Acme Supply" } })
            .OnJson("GET", "api/customers", new[] { new CustomerDto { Id = 3, CustomerNumber = 12, CompanyName = "Gamma" } });
        var session = TestSession.Create(server, Permission.ViewOrders);
        var shell = new ShellViewModel(session);

        var purchases = new StockInsViewModel(session, shell);
        await purchases.LoadAsync();
        Assert.Equal("Supplier", purchases.PartyLabel);
        purchases.Party.Selected = purchases.Party.Choices.Single();
        await Wait.For(() => Query(server, "api/stock-ins").Contains("supplierId=8"));

        var returns = new StockInsViewModel(session, shell, ReceiveType.DemoReturn);
        await returns.LoadAsync();
        Assert.Equal("Customer", returns.PartyLabel);
        returns.Party.Search = "gam";
        await Wait.For(() => Query(server, "api/stock-ins").Contains("customerId=3"));
        Assert.DoesNotContain("supplierId", Query(server, "api/stock-ins"));
    }
}
