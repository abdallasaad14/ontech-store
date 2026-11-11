using System.Net;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;
using OnTech.Store.Desktop.ViewModels;
using Xunit;

namespace OnTech.Store.Desktop.Tests;

/// <summary>
/// H-2: a page is in the menu only when every list it loads is readable (CONTRACTS.md section 5). The fake server applies the
/// server's read policy (<see cref="ReadAccess"/>) and answers 403 otherwise; opening any menu page must never get a 403.
/// </summary>
public class MenuPermissionTests
{
    public static IEnumerable<object[]> SinglePermissions() =>
        Enum.GetValues<Permission>().Where(p => p != Permission.None && System.Numerics.BitOperations.PopCount((uint)p) == 1)
            .Select(p => new object[] { p }).Append([Permission.None]);

    /// <summary>The read side of the API, as the contract defines it.</summary>
    private static Permission? NeededFor(string method, string path)
    {
        if (method != "GET") return null;
        if (path.StartsWith("api/dashboard") || path.EndsWith("/card") || path.StartsWith("api/stock-movements")) return ReadAccess.StockFigures;
        if (path.StartsWith("api/categories") || path.StartsWith("api/items")) return ReadAccess.Catalog;
        if (path.StartsWith("api/customers")) return ReadAccess.Customers;
        if (path.StartsWith("api/suppliers")) return ReadAccess.Suppliers;
        if (path.StartsWith("api/orders") || path.StartsWith("api/stock-ins")) return Permission.ViewOrders;
        if (path.StartsWith("api/users")) return Permission.ManageUsers;
        return null;
    }

    private static (FakeServer Server, List<string> Forbidden) PolicyServer(Permission held)
    {
        var forbidden = new List<string>();
        var answers = new FakeServer().WithEmptyLists(); // the 200 answers, used once the policy says yes
        var server = new FakeServer();
        server.Fallback = async req =>
        {
            var path = req.RequestUri!.AbsolutePath.TrimStart('/');
            if (NeededFor(req.Method.Method, path) is { } needed && !held.HasAny(needed))
            {
                lock (forbidden) forbidden.Add(req.Method.Method + " " + path);
                return FakeServer.Problem(HttpStatusCode.Forbidden, "Forbidden");
            }
            return await new HttpMessageInvoker(answers).SendAsync(new HttpRequestMessage(req.Method, req.RequestUri), CancellationToken.None);
        };
        return (server, forbidden);
    }

    [Theory]
    [MemberData(nameof(SinglePermissions))]
    public async Task Every_menu_page_loads_without_403(Permission permission)
    {
        var (server, forbidden) = PolicyServer(permission);
        var session = TestSession.Create(server, permission);
        var shell = new ShellViewModel(session);
        foreach (var item in shell.MainMenu.Concat(shell.AdminMenu).ToList())
        {
            shell.Navigate(item.Page);
            var page = shell.CurrentPage!;
            await page.LoadAsync();
            await Wait.For(() => !page.IsBusy);
            Assert.True(page.ErrorMessage is null, $"{permission}: {item.Page} shows '{page.ErrorMessage}'");
        }
        Assert.True(forbidden.Count == 0, $"{permission}: {string.Join(", ", forbidden)}");
    }

    [Theory]
    [InlineData(Permission.None, new[] { Page.Home })]
    [InlineData(Permission.ViewStock, new[] { Page.Home, Page.Customers, Page.Suppliers })]
    [InlineData(Permission.CreateOrders, new[] { Page.Home, Page.NewOrder, Page.Customers })]
    [InlineData(Permission.AddStock, new[] { Page.Home, Page.StockIn, Page.Suppliers })]
    [InlineData(Permission.ManageCustomers, new[] { Page.Home, Page.Customers })]
    [InlineData(Permission.ManageSuppliers, new[] { Page.Home, Page.Suppliers })]
    [InlineData(Permission.ViewOrders, new[] { Page.Home, Page.Orders, Page.StockIns })]
    [InlineData(Permission.ManageCatalog, new[] { Page.Home, Page.Catalog })]
    [InlineData(Permission.ManageUsers, new[] { Page.Home, Page.Users })]
    public void Menu_shows_the_pages_of_each_permission(Permission permission, Page[] expected)
    {
        var shell = new ShellViewModel(TestSession.Create(PolicyServer(permission).Server, permission));
        Assert.Equal(expected, shell.MainMenu.Concat(shell.AdminMenu).Select(i => i.Page).ToArray());
    }

    [Fact]
    public void Admin_sees_every_page()
    {
        var shell = new ShellViewModel(TestSession.Create(new FakeServer().WithEmptyLists(), Permission.None, isAdmin: true));
        Assert.Equal(Enum.GetValues<Page>(), shell.MainMenu.Concat(shell.AdminMenu).Select(i => i.Page).ToArray());
    }

    [Fact]
    public async Task Home_without_ViewStock_loads_no_stock_figures()
    {
        var (server, _) = PolicyServer(Permission.CreateOrders);
        var session = TestSession.Create(server, Permission.CreateOrders);
        var home = new HomeViewModel(session, new ShellViewModel(session));
        await home.LoadAsync();
        Assert.Equal(0, server.Count("GET", "api/dashboard/summary"));
        Assert.True(home.CanCreateOrders);
        Assert.False(home.CanViewStock);
    }

    [Theory]
    [InlineData(Permission.CreateOrders, false)]
    [InlineData(Permission.ManageCatalog, false)]
    [InlineData(Permission.ViewStock, true)]
    public void Item_card_needs_ViewStock(Permission permission, bool canOpen)
    {
        var session = TestSession.Create(new FakeServer().WithEmptyLists(), permission);
        Assert.Equal(canOpen, ItemCards.CanOpen(session));
        Assert.Equal(canOpen, new CatalogViewModel(session).CanOpenItemCard);
    }

    [Theory]
    [InlineData(Permission.ViewStock, false)]
    [InlineData(Permission.CreateOrders, false)]
    [InlineData(Permission.ManageCustomers, true)]
    public void Customer_edit_buttons_need_ManageCustomers(Permission permission, bool canEdit) =>
        Assert.Equal(canEdit, new CustomersViewModel(TestSession.Create(new FakeServer(), permission)).CanEdit);

    [Theory]
    [InlineData(Permission.ViewStock, false)]
    [InlineData(Permission.AddStock, false)]
    [InlineData(Permission.ManageSuppliers, true)]
    public void Supplier_edit_buttons_need_ManageSuppliers(Permission permission, bool canEdit) =>
        Assert.Equal(canEdit, new SuppliersViewModel(TestSession.Create(new FakeServer(), permission)).CanEdit);
}
