using System.Net;
using OnTech.Store.Api.Infrastructure;
using OnTech.Store.Contracts;
using Xunit;

namespace OnTech.Store.Api.Tests;

/// <summary>M-1: an edit saved from a stale form gets 409 ConcurrencyConflict instead of silently overwriting (CONTRACTS.md §4).</summary>
public class Fix_B_ConcurrencyTests : IClassFixture<StoreApiFactory>
{
    private readonly StoreApiFactory _f;
    public Fix_B_ConcurrencyTests(StoreApiFactory f) => _f = f;

    private const string Title = "This record was changed on another device. It has been reloaded; check it and save again.";

    private static async Task AssertConflict(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var p = await res.ProblemAsync();
        Assert.Equal(ErrorCodes.ConcurrencyConflict, p.Code);
        Assert.Equal(Title, p.Title);
    }

    [Fact]
    public void Message_matches_the_contract() => Assert.Equal(Title, ApiException.ConcurrencyConflictMessage);

    [Fact]
    public async Task Customer_edit_from_a_stale_form_is_refused_and_changes_nothing()
    {
        var admin = await _f.LoginAsync();
        var c = await (await admin.Post("/api/customers", new SaveCustomerRequest { CompanyName = $"Conc {FixB.Tag()}" })).Read<CustomerDto>();
        Assert.Equal(0, c.Version);

        // PC1 saves the phone number.
        var pc1 = await (await admin.Put($"/api/customers/{c.Id}", new SaveCustomerRequest { CompanyName = c.CompanyName, PhoneNumber = "0100", Version = c.Version })).Read<CustomerDto>();
        Assert.Equal(1, pc1.Version);
        Assert.Equal("0100", pc1.PhoneNumber);

        // PC2 still has version 0 on screen.
        await AssertConflict(await admin.Put($"/api/customers/{c.Id}", new SaveCustomerRequest { CompanyName = c.CompanyName, Notes = "stale", Version = c.Version }));
        var now = await admin.GetAs<CustomerDto>($"/api/customers/{c.Id}");
        Assert.Equal("0100", now.PhoneNumber);
        Assert.Null(now.Notes);
        Assert.Equal(1, now.Version);

        // No version = no check (old callers).
        var noCheck = await (await admin.Put($"/api/customers/{c.Id}", new SaveCustomerRequest { CompanyName = c.CompanyName, PhoneNumber = "0100", Notes = "n" })).Read<CustomerDto>();
        Assert.Equal(2, noCheck.Version);

        // Disabling is an edit too.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/customers/{c.Id}")).StatusCode);
        Assert.Equal(3, (await admin.GetAs<CustomerDto>($"/api/customers/{c.Id}")).Version);
    }

    [Fact]
    public async Task Supplier_edit_from_a_stale_form_is_refused()
    {
        var admin = await _f.LoginAsync();
        var s = await (await admin.Post("/api/suppliers", new SaveSupplierRequest { CompanyName = $"Conc sup {FixB.Tag()}" })).Read<SupplierDto>();
        var ok = await (await admin.Put($"/api/suppliers/{s.Id}", new SaveSupplierRequest { CompanyName = s.CompanyName, PhoneNumber = "1", Version = s.Version })).Read<SupplierDto>();
        Assert.Equal(s.Version + 1, ok.Version);
        await AssertConflict(await admin.Put($"/api/suppliers/{s.Id}", new SaveSupplierRequest { CompanyName = s.CompanyName, Notes = "stale", Version = s.Version }));
        Assert.Equal("1", (await admin.GetAs<SupplierDto>($"/api/suppliers/{s.Id}")).PhoneNumber);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/suppliers/{s.Id}")).StatusCode);
        Assert.Equal(ok.Version + 1, (await admin.GetAs<SupplierDto>($"/api/suppliers/{s.Id}")).Version);
    }

    [Fact]
    public async Task Item_edit_from_a_stale_form_is_refused_and_stock_movements_do_not_bump_the_version()
    {
        var admin = await _f.LoginAsync();
        var (cat, item, customer, _) = await FixB.SeedAsync(admin); // seeding put stock in through a stock-in
        Assert.Equal(0, item.Version);

        await (await admin.Post("/api/orders", new CreateOrderRequest
        {
            CustomerId = customer.Id, IssueVoucherNumber = TestStock.Voucher(), Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 1 } },
        })).Read<OrderDto>();
        var afterOrder = await admin.GetAs<ItemDto>($"/api/items/{item.Id}");
        Assert.Equal(0, afterOrder.Version);
        Assert.Equal(19, afterOrder.Quantity);

        var edit = new UpdateItemRequest { CategoryId = cat.Id, Name = item.Name, LowStockThreshold = 5, IsActive = true, Version = 0 };
        var saved = await (await admin.Put($"/api/items/{item.Id}", edit)).Read<ItemDto>();
        Assert.Equal(1, saved.Version);
        Assert.Equal(19, saved.Quantity);

        await AssertConflict(await admin.Put($"/api/items/{item.Id}", new UpdateItemRequest { CategoryId = cat.Id, Name = item.Name, LowStockThreshold = 9, IsActive = true, Version = 0 }));
        Assert.Equal(5, (await admin.GetAs<ItemDto>($"/api/items/{item.Id}")).LowStockThreshold);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/items/{item.Id}")).StatusCode);
        Assert.Equal(2, (await admin.GetAs<ItemDto>($"/api/items/{item.Id}")).Version);
    }

    [Fact]
    public async Task User_edit_reset_password_and_disable_bump_the_version()
    {
        var admin = await _f.LoginAsync();
        var name = $"cu{FixB.Tag()}";
        var u = await (await admin.Post("/api/users", new CreateUserRequest { FullName = name, Username = name, Password = "Password1!", Permissions = Permission.ViewStock })).Read<UserDto>();
        Assert.Equal(0, u.Version);

        var edited = await (await admin.Put($"/api/users/{u.Id}", new UpdateUserRequest { FullName = "New name", Username = name, IsActive = true, Permissions = Permission.ViewStock, Version = 0 })).Read<UserDto>();
        Assert.Equal(1, edited.Version);
        await AssertConflict(await admin.Put($"/api/users/{u.Id}", new UpdateUserRequest { FullName = "Stale", Username = name, IsActive = true, Permissions = Permission.All, Version = 0 }));
        var now = await admin.GetAs<UserDto>($"/api/users/{u.Id}");
        Assert.Equal("New name", now.FullName);
        Assert.Equal(Permission.ViewStock, now.Permissions);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.Post($"/api/users/{u.Id}/reset-password", new ResetPasswordRequest { NewPassword = "Password2!" })).StatusCode);
        Assert.Equal(2, (await admin.GetAs<UserDto>($"/api/users/{u.Id}")).Version);
        // A form loaded before the reset is now stale.
        await AssertConflict(await admin.Put($"/api/users/{u.Id}", new UpdateUserRequest { FullName = "New name", Username = name, IsActive = true, Permissions = Permission.ViewStock, Version = 1 }));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{u.Id}")).StatusCode);
        Assert.Equal(3, (await admin.GetAs<UserDto>($"/api/users/{u.Id}")).Version);
    }

    [Fact]
    public async Task Parallel_edits_with_the_same_version_let_exactly_one_win()
    {
        var admin = await _f.LoginAsync();
        var c = await (await admin.Post("/api/customers", new SaveCustomerRequest { CompanyName = $"Race {FixB.Tag()}" })).Read<CustomerDto>();
        var clients = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => _f.LoginAsync()));

        var results = await Task.WhenAll(clients.Select((cl, i) =>
            cl.Put($"/api/customers/{c.Id}", new SaveCustomerRequest { CompanyName = c.CompanyName, Notes = $"PC{i}", Version = c.Version })));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        foreach (var r in results.Where(r => r.StatusCode != HttpStatusCode.OK)) await AssertConflict(r);
        Assert.Equal(c.Version + 1, (await admin.GetAs<CustomerDto>($"/api/customers/{c.Id}")).Version);
    }

    [Fact]
    public async Task Parallel_item_edits_without_a_version_never_give_500()
    {
        var admin = await _f.LoginAsync();
        var (cat, item, _, _) = await FixB.SeedAsync(admin);
        var clients = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => _f.LoginAsync()));
        var results = await Task.WhenAll(clients.Select((cl, i) =>
            cl.Put($"/api/items/{item.Id}", new UpdateItemRequest { CategoryId = cat.Id, Name = item.Name, LowStockThreshold = i, IsActive = true })));
        Assert.All(results, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        Assert.Contains(results, r => r.StatusCode == HttpStatusCode.OK);
        foreach (var r in results.Where(r => r.StatusCode == HttpStatusCode.Conflict)) await AssertConflict(r);
    }
}
