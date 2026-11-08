using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using OnTech.Store.Api.Auth;
using OnTech.Store.Contracts;
using Xunit;

namespace OnTech.Store.Api.Tests;

/// <summary>H-2 and L-3: who may read the catalog, customers and suppliers (CONTRACTS.md §5, <see cref="ReadAccess"/>).</summary>
public class Fix_B_ReadPolicyTests : IClassFixture<StoreApiFactory>
{
    private readonly StoreApiFactory _f;
    public Fix_B_ReadPolicyTests(StoreApiFactory f) => _f = f;

    private static readonly string[] CatalogUrls = ["/api/categories", "/api/categories/{cat}", "/api/items", "/api/items/{item}"];
    private static readonly string[] CustomerUrls = ["/api/customers", "/api/customers/{customer}"];
    private static readonly string[] SupplierUrls = ["/api/suppliers", "/api/suppliers/{supplier}"];
    private static readonly string[] StockFigureUrls = ["/api/dashboard/summary", "/api/items/{item}/card", "/api/stock-movements"];

    public static TheoryData<Permission> SinglePermissions => new()
    {
        Permission.None, Permission.ViewStock, Permission.ManageCustomers, Permission.CreateOrders, Permission.AddStock,
        Permission.ViewOrders, Permission.CancelOrders, Permission.ManageCatalog, Permission.ManageUsers, Permission.ManageSuppliers,
    };

    [Theory]
    [MemberData(nameof(SinglePermissions))]
    public async Task Each_permission_reads_exactly_what_the_read_table_allows(Permission held)
    {
        var admin = await _f.LoginAsync();
        var (cat, item, customer, supplier) = await FixB.SeedAsync(admin);
        var user = await _f.UserWithAsync(admin, held);

        async Task Check(string[] urls, Permission anyOf)
        {
            foreach (var template in urls)
            {
                var url = template.Replace("{cat}", cat.Id.ToString()).Replace("{item}", item.Id.ToString())
                    .Replace("{customer}", customer.Id.ToString()).Replace("{supplier}", supplier.Id.ToString());
                var res = await user.GetAsync(url);
                var expected = held.HasAny(anyOf) ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
                Assert.True(expected == res.StatusCode, $"{held} GET {template}: expected {expected}, got {res.StatusCode}");
            }
        }

        await Check(CatalogUrls, ReadAccess.Catalog);
        await Check(CustomerUrls, ReadAccess.Customers);
        await Check(SupplierUrls, ReadAccess.Suppliers);
        await Check(StockFigureUrls, ReadAccess.StockFigures);
    }

    [Fact]
    public async Task User_with_no_permissions_cannot_read_customers_or_suppliers()
    {
        var admin = await _f.LoginAsync();
        var (_, _, customer, supplier) = await FixB.SeedAsync(admin);
        var nobody = await _f.UserWithAsync(admin, Permission.None);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync("/api/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync($"/api/customers/{customer.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync("/api/suppliers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync($"/api/suppliers/{supplier.Id}")).StatusCode);
    }

    [Fact]
    public async Task Order_taker_can_load_everything_the_new_order_page_needs()
    {
        var admin = await _f.LoginAsync();
        await FixB.SeedAsync(admin);
        var clerk = await _f.UserWithAsync(admin, Permission.CreateOrders);
        Assert.Equal(HttpStatusCode.OK, (await clerk.GetAsync("/api/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clerk.GetAsync("/api/items")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clerk.GetAsync("/api/customers")).StatusCode);
        // Stock figures stay behind ViewStock.
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.GetAsync("/api/dashboard/summary")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_are_still_refused()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync("/api/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync("/api/categories")).StatusCode);
    }

    [Fact]
    public async Task Named_any_policies_resolve_and_other_names_go_to_the_permission_provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddAnyPermissionPolicies();
        services.AddAnyPermissionPolicies(); // twice is harmless
        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IAuthorizationPolicyProvider>();
        Assert.IsType<AnyPermissionPolicyProvider>(provider);

        var any = await provider.GetPolicyAsync(HasAnyPermissionAttribute.PolicyName(ReadAccess.Customers));
        var req = Assert.Single(any!.Requirements.OfType<AnyPermissionRequirement>());
        Assert.Equal(ReadAccess.Customers, req.AnyOf);

        var perm = await provider.GetPolicyAsync(HasPermissionAttribute.PolicyName(Permission.ViewStock));
        Assert.Contains(perm!.Requirements, r => r is PermissionRequirement { Permission: Permission.ViewStock });
        Assert.Null(await provider.GetPolicyAsync("any:abc"));
    }
}
