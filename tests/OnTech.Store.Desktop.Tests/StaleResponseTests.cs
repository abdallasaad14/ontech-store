using System.Net;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.ViewModels;
using Xunit;

namespace OnTech.Store.Desktop.Tests;

/// <summary>L-8: a quick filter change cancels the load still running, and a late answer never replaces the newer list.</summary>
public class StaleResponseTests
{
    /// <summary>The first matching request waits for <paramref name="release"/>; the rest answer at once.</summary>
    private static Func<HttpRequestMessage, Task<HttpResponseMessage>> SlowWhen(string query, TaskCompletionSource release,
        Func<HttpRequestMessage, object> body) => async req =>
    {
        if (req.RequestUri!.Query.Contains(query)) await release.Task;
        return FakeServer.Json(HttpStatusCode.OK, body(req));
    };

    private static ItemDto[] ItemsFor(HttpRequestMessage req) =>
        req.RequestUri!.Query.Contains("categoryId=1")
            ? [new ItemDto { Id = 1, CategoryId = 1, Name = "Old category item" }]
            : [new ItemDto { Id = 2, CategoryId = 2, Name = "New category item" }];

    private static readonly CategoryDto Cat1 = new() { Id = 1, Name = "One", IsActive = true };
    private static readonly CategoryDto Cat2 = new() { Id = 2, Name = "Two", IsActive = true };

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // the server answers the old request anyway: the version guard drops it
    public async Task Home_keeps_the_latest_category(bool ignoreCancellation)
    {
        var release = new TaskCompletionSource();
        var server = new FakeServer { IgnoreCancellation = ignoreCancellation }.WithEmptyLists()
            .OnJson("GET", "api/categories", new[] { Cat1, Cat2 })
            .On("GET", "api/items", SlowWhen("categoryId=1", release, ItemsFor));
        var session = TestSession.Create(server, Permission.ViewStock);
        var home = new HomeViewModel(session, new ShellViewModel(session, Page.Orders));
        await home.LoadAsync();

        home.SelectedCategory = home.Categories.Single(c => c.Id == 1); // slow
        await Task.Delay(30);
        home.SelectedCategory = home.Categories.Single(c => c.Id == 2); // fast
        await Wait.For(() => home.Items.Any(i => i.Id == 2));
        release.SetResult();
        await Wait.For(() => !home.IsBusy);

        Assert.Equal("New category item", Assert.Single(home.Items).Name);
        Assert.Null(home.ErrorMessage);
        Assert.Equal(1, server.Cancelled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Catalog_keeps_the_latest_category_items(bool ignoreCancellation)
    {
        var release = new TaskCompletionSource();
        var server = new FakeServer { IgnoreCancellation = ignoreCancellation }.WithEmptyLists()
            .OnJson("GET", "api/categories", new[] { Cat1, Cat2 })
            .On("GET", "api/items", SlowWhen("categoryId=1", release, ItemsFor));
        var vm = new CatalogViewModel(TestSession.Create(server, Permission.ManageCatalog));
        _ = vm.LoadAsync(); // selects category 1 -> slow load
        await Wait.For(() => vm.Categories.Count == 2);
        vm.SelectedCategory = vm.Categories.Single(c => c.Id == 2);
        await Wait.For(() => vm.Items.Any(i => i.Id == 2));
        release.SetResult();
        await Wait.For(() => !vm.IsBusy);

        Assert.Equal("New category item", Assert.Single(vm.Items).Name);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Catalog_keeps_the_latest_category_list()
    {
        var release = new TaskCompletionSource();
        var server = new FakeServer { IgnoreCancellation = true }.WithEmptyLists()
            .On("GET", "api/categories", SlowWhen("includeInactive=false", release, _ => new[] { Cat1 }))
            .OnJson("GET", "api/items", Array.Empty<ItemDto>());
        var vm = new CatalogViewModel(TestSession.Create(server, Permission.ManageCatalog));
        _ = vm.LoadAsync();            // without disabled: slow
        await Task.Delay(30);
        server.On("GET", "api/categories", SlowWhen("includeInactive=false", release,
            req => req.RequestUri!.Query.Contains("includeInactive=true") ? new[] { Cat1, Cat2 } : new[] { Cat1 }));
        vm.ShowDisabled = true;        // with disabled: fast
        await Wait.For(() => vm.Categories.Count == 2);
        release.SetResult();
        await Wait.For(() => !vm.IsBusy);
        Assert.Equal(2, vm.Categories.Count);
    }

    [Fact]
    public async Task Orders_keep_the_latest_filter()
    {
        var release = new TaskCompletionSource();
        static PagedResult<OrderDto> Page(string voucher) => new()
        {
            Items = [new OrderDto { Id = voucher.Length, IssueVoucherNumber = voucher }], Page = 1, PageSize = 50, TotalCount = 1,
        };
        var server = new FakeServer().WithEmptyLists()
            .On("GET", "api/orders", SlowWhen("withReceipt=true", release,
                req => req.RequestUri!.Query.Contains("withReceipt=true") ? Page("OLD") : Page("NEWEST")));
        var session = TestSession.Create(server, Permission.ViewOrders);
        var vm = new OrdersViewModel(session, new ShellViewModel(session));
        await vm.LoadAsync();
        vm.ReceiptFilterIndex = 1; // with receipt: slow
        await Task.Delay(30);
        vm.ReceiptFilterIndex = 2; // without: fast
        await Wait.For(() => vm.Orders.Any(o => o.IssueVoucherNumber == "NEWEST"));
        release.SetResult();
        await Wait.For(() => !vm.IsBusy);
        Assert.Equal("NEWEST", Assert.Single(vm.Orders).IssueVoucherNumber);
        Assert.Null(vm.ErrorMessage);
    }
}
