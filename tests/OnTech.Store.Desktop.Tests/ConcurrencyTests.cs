using System.Net;
using System.Text.Json;
using OnTech.Store.Client;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.ViewModels;
using Xunit;

namespace OnTech.Store.Desktop.Tests;

/// <summary>M-1: edits send the Version they were loaded with; a 409 ConcurrencyConflict reloads the record and keeps the form open.</summary>
public class ConcurrencyTests
{
    private const string Conflict = "This record was changed on another device. It has been reloaded; check it and save again.";

    private static HttpResponseMessage ConflictResponse() =>
        FakeServer.Problem(HttpStatusCode.Conflict, Conflict, ErrorCodes.ConcurrencyConflict);

    private static int? SentVersion(FakeServer s, string method) =>
        s.LastBody(method).GetProperty("version") is { ValueKind: JsonValueKind.Number } v ? v.GetInt32() : null;

    [Fact]
    public async Task Api_error_exposes_the_problem_code()
    {
        var server = new FakeServer().On("PUT", "api/customers/1", _ => Task.FromResult(ConflictResponse()));
        var api = TestSession.Client(server);
        var ex = await Assert.ThrowsAsync<ApiError>(() => api.UpdateCustomerAsync(1, new SaveCustomerRequest { CompanyName = "x" }));
        Assert.Equal(ErrorCodes.ConcurrencyConflict, ex.Code);
        Assert.Equal(HttpStatusCode.Conflict, ex.Status);
        Assert.Equal(Conflict, ex.Message);
    }

    [Fact]
    public async Task Customer_edit_sends_version_and_reloads_on_conflict()
    {
        var stale = new CustomerDto { Id = 1, CompanyName = "Acme", PhoneNumber = "111", IsActive = true, Version = 3 };
        var fresh = new CustomerDto { Id = 1, CompanyName = "Acme", PhoneNumber = "222", IsActive = true, Version = 4 };
        var server = new FakeServer().WithEmptyLists()
            .OnJson("GET", "api/customers", new[] { fresh })
            .OnJson("GET", "api/customers/1", fresh)
            .On("PUT", "api/customers/1", _ => Task.FromResult(ConflictResponse()));
        var vm = new CustomersViewModel(TestSession.Create(server, Permission.ManageCustomers));
        vm.EditCommand.Execute(stale);
        vm.Notes = "my change";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(3, SentVersion(server, "PUT"));
        Assert.Equal(Conflict, vm.ErrorMessage);
        Assert.True(vm.IsEditing);
        Assert.Equal("222", vm.PhoneNumber); // the record as saved on the other PC
        Assert.Null(vm.Notes);

        // Saving again now sends the fresh version.
        server.OnJson("PUT", "api/customers/1", fresh);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(4, SentVersion(server, "PUT"));
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Conflict_message_is_Arabic_in_Arabic_mode()
    {
        using var _ = new Language("ar");
        var c = new CustomerDto { Id = 1, CompanyName = "Acme", IsActive = true, Version = 1 };
        var server = new FakeServer().WithEmptyLists()
            .OnJson("GET", "api/customers/1", c)
            .On("PUT", "api/customers/1", _ => Task.FromResult(ConflictResponse()));
        var vm = new CustomersViewModel(TestSession.Create(server, Permission.ManageCustomers));
        vm.EditCommand.Execute(c);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.StartsWith("تم تعديل هذا السجل من جهاز آخر", vm.ErrorMessage);
    }

    [Fact]
    public async Task Supplier_edit_sends_version_and_reloads_on_conflict()
    {
        var stale = new SupplierDto { Id = 2, CompanyName = "Parts Co", IsActive = true, Version = 5 };
        var fresh = new SupplierDto { Id = 2, CompanyName = "Parts Company", IsActive = true, Version = 6 };
        var server = new FakeServer().WithEmptyLists()
            .OnJson("GET", "api/suppliers/2", fresh)
            .On("PUT", "api/suppliers/2", _ => Task.FromResult(ConflictResponse()));
        var vm = new SuppliersViewModel(TestSession.Create(server, Permission.ManageSuppliers));
        vm.EditCommand.Execute(stale);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(5, SentVersion(server, "PUT"));
        Assert.True(vm.IsEditing);
        Assert.Equal("Parts Company", vm.CompanyName);
    }

    [Fact]
    public async Task New_customer_sends_no_version()
    {
        var server = new FakeServer().WithEmptyLists().OnJson("POST", "api/customers", new CustomerDto { Id = 9, CompanyName = "New" });
        var vm = new CustomersViewModel(TestSession.Create(server, Permission.ManageCustomers));
        vm.StartNew();
        vm.CompanyName = "New";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(SentVersion(server, "POST"));
    }

    [Fact]
    public async Task Item_edit_sends_version_and_reloads_on_conflict()
    {
        var cat = new CategoryDto { Id = 1, Name = "Cables", IsActive = true };
        var stale = new ItemDto { Id = 4, CategoryId = 1, Name = "HDMI", LowStockThreshold = 2, IsActive = true, Version = 7 };
        var fresh = new ItemDto { Id = 4, CategoryId = 1, Name = "HDMI 2m", LowStockThreshold = 5, IsActive = true, Version = 8 };
        var server = new FakeServer().WithEmptyLists()
            .OnJson("GET", "api/categories", new[] { cat })
            .OnJson("GET", "api/items/4", fresh)
            .On("PUT", "api/items/4", _ => Task.FromResult(ConflictResponse()));
        var vm = new CatalogViewModel(TestSession.Create(server, Permission.ManageCatalog));
        await vm.LoadAsync();
        vm.EditItemCommand.Execute(stale);
        await vm.SaveFormCommand.ExecuteAsync(null);
        Assert.Equal(7, SentVersion(server, "PUT"));
        Assert.True(vm.IsItemForm);
        Assert.Equal("HDMI 2m", vm.ItemName);
        Assert.Equal("5", vm.ItemLowStock);
        Assert.Equal(Conflict, vm.ErrorMessage);
    }

    [Fact]
    public async Task User_edit_sends_version_and_reloads_on_conflict()
    {
        var stale = new UserDto { Id = 20, FullName = "Sara", Username = "sara", IsActive = true, Permissions = Permission.ViewStock, Version = 1 };
        var fresh = new UserDto { Id = 20, FullName = "Sara Ali", Username = "sara", IsActive = true, Permissions = Permission.ViewStock, Version = 2 };
        var server = new FakeServer().WithEmptyLists()
            .OnJson("GET", "api/users/20", fresh)
            .On("PUT", "api/users/20", _ => Task.FromResult(ConflictResponse()));
        var vm = new UsersViewModel(TestSession.Create(server, Permission.All, isAdmin: true));
        vm.EditCommand.Execute(stale);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, SentVersion(server, "PUT"));
        Assert.True(vm.IsEditing);
        Assert.Equal("Sara Ali", vm.FullName);
    }
}
