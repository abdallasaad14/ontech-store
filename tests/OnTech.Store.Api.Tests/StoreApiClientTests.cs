using System.Net;
using OnTech.Store.Client;
using OnTech.Store.Contracts;
using Xunit;

namespace OnTech.Store.Api.Tests;

/// <summary>The desktop app's API client, run against the real server in memory.</summary>
public class StoreApiClientTests : IClassFixture<StoreApiFactory>
{
    private readonly StoreApiFactory _f;
    public StoreApiClientTests(StoreApiFactory f) => _f = f;

    private StoreApiClient NewClient() => new(_f.CreateClient());

    private async Task<StoreApiClient> AdminAsync()
    {
        var api = NewClient();
        await api.LoginAsync(StoreApiFactory.AdminEmail, StoreApiFactory.AdminPassword);
        return api;
    }

    [Theory]
    [InlineData("192.168.1.10", "http://192.168.1.10:5080/")]
    [InlineData(" store-pc ", "http://store-pc:5080/")]
    [InlineData("store-pc:6000", "http://store-pc:6000/")]
    [InlineData("http://192.168.1.10:5080", "http://192.168.1.10:5080/")]
    [InlineData("http://192.168.1.10:5080/swagger", "http://192.168.1.10:5080/")]
    [InlineData("https://store.ontech.local", "https://store.ontech.local/")]
    public void Server_address_is_normalized(string input, string expected) =>
        Assert.Equal(expected, StoreApiClient.NormalizeServerUrl(input).ToString());

    [Theory]
    [InlineData("")]
    [InlineData("ftp://server")]
    public void Bad_server_address_is_rejected(string input) =>
        Assert.Throws<ArgumentException>(() => StoreApiClient.NormalizeServerUrl(input));

    [Fact]
    public async Task Wrong_password_gives_readable_message()
    {
        var ex = await Assert.ThrowsAsync<ApiError>(() => NewClient().LoginAsync(StoreApiFactory.AdminEmail, "wrong-password"));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.Status);
        Assert.Equal("Email or password is incorrect.", ex.Message);
    }

    [Fact]
    public async Task Calls_without_login_report_unauthorized()
    {
        var ex = await Assert.ThrowsAsync<ApiError>(() => NewClient().GetItemsAsync());
        Assert.True(ex.IsUnauthorized);
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public async Task Unreachable_server_is_a_connection_error()
    {
        var api = StoreApiClient.Create("http://127.0.0.1:1", TimeSpan.FromSeconds(5));
        var ex = await Assert.ThrowsAsync<ApiError>(() => api.CheckHealthAsync());
        Assert.True(ex.IsConnectionError);
        Assert.Contains("Cannot reach the server", ex.Message);
    }

    [Fact]
    public async Task Full_new_order_flow()
    {
        var api = await AdminAsync();
        Assert.True(api.CurrentUser!.IsAdmin);
        var tag = Guid.NewGuid().ToString("N")[..6];

        var printers = await api.CreateCategoryAsync(new SaveCategoryRequest { Name = $"Printers {tag}" });
        var fargo = await api.CreateItemAsync(new CreateItemRequest { CategoryId = printers.Id, Name = "Fargo", InitialQuantity = 5, LowStockThreshold = 2 });
        fargo = await api.AddQuantityAsync(fargo.Id, 3, "Delivery 42");
        Assert.Equal(8, fargo.Quantity);

        var customer = await api.CreateCustomerAsync(new SaveCustomerRequest { CompanyName = $"Acme {tag}", TaxNumber = $"T-{tag}" });
        Assert.Contains(await api.GetCustomersAsync(tag), c => c.Id == customer.Id);
        Assert.Contains(await api.GetItemsAsync(printers.Id), i => i.Id == fargo.Id && i.Quantity == 8);

        var order = await api.CreateOrderAsync(new CreateOrderRequest
        {
            CustomerId = customer.Id, WithReceipt = true, ReceiptNumber = $"R-{tag}",
            Lines = [new OrderLineRequest { ItemId = fargo.Id, Quantity = 6 }],
        });
        Assert.Equal(6, order.TotalQuantity);
        Assert.Equal(2, (await api.GetItemAsync(fargo.Id)).Quantity);

        // Not enough stock: the message names the item and nothing is taken.
        var shortage = await Assert.ThrowsAsync<ApiError>(() => api.CreateOrderAsync(new CreateOrderRequest
        {
            CustomerId = customer.Id, Lines = [new OrderLineRequest { ItemId = fargo.Id, Quantity = 3 }],
        }));
        Assert.Equal(HttpStatusCode.Conflict, shortage.Status);
        var line = Assert.Single(shortage.Shortages);
        Assert.Equal(2, line.Available);
        Assert.Contains("Fargo: asked for 3, only 2 available", shortage.Message);

        // Receipt numbers are unique.
        var duplicate = await Assert.ThrowsAsync<ApiError>(() => api.CreateOrderAsync(new CreateOrderRequest
        {
            CustomerId = customer.Id, WithReceipt = true, ReceiptNumber = $"R-{tag}",
            Lines = [new OrderLineRequest { ItemId = fargo.Id, Quantity = 1 }],
        }));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.Status);

        var withReceipt = await api.SearchOrdersAsync(new OrderQuery { Search = tag, WithReceipt = true });
        Assert.Equal(order.Id, Assert.Single(withReceipt.Items).Id);
        var withoutReceipt = await api.SearchOrdersAsync(new OrderQuery { Search = tag, WithReceipt = false });
        Assert.Empty(withoutReceipt.Items);

        var cancelled = await api.CancelOrderAsync(order.Id, "Wrong customer");
        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(8, (await api.GetItemAsync(fargo.Id)).Quantity);

        var dashboard = await api.GetDashboardAsync();
        Assert.True(dashboard.ItemCount >= 1);
    }

    [Fact]
    public async Task Validation_errors_show_field_messages()
    {
        var api = await AdminAsync();
        var ex = await Assert.ThrowsAsync<ApiError>(() => api.CreateUserAsync(new CreateUserRequest
        {
            FullName = "Short Password", Email = $"u{Guid.NewGuid():N}@test.local", Password = "123",
        }));
        Assert.Equal(HttpStatusCode.BadRequest, ex.Status);
        Assert.DoesNotContain("One or more validation errors", ex.Message);
        Assert.Contains("Password", ex.Message);
    }

    [Fact]
    public async Task Users_permissions_and_password_change()
    {
        var admin = await AdminAsync();
        var email = $"clerk{Guid.NewGuid():N}@test.local";
        var clerk = await admin.CreateUserAsync(new CreateUserRequest
        {
            FullName = "Store Clerk", Email = email, Password = "Clerk!12345",
            Permissions = Permission.ViewStock | Permission.CreateOrders, MustChangePassword = true,
        });
        Assert.Contains(await admin.GetUsersAsync(), u => u.Id == clerk.Id);

        var api = NewClient();
        var me = await api.LoginAsync(email, "Clerk!12345");
        Assert.True(me.MustChangePassword);
        await api.ChangePasswordAsync("Clerk!12345", "NewClerk!123");
        Assert.False((await api.RefreshMeAsync()).MustChangePassword);

        // Admin-only screens are refused with a readable message.
        var forbidden = await Assert.ThrowsAsync<ApiError>(() => api.GetUsersAsync());
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.Status);
        Assert.Equal("You don't have permission to do this. Ask an admin.", forbidden.Message);

        await admin.ResetPasswordAsync(clerk.Id, "Reset!12345", mustChangePassword: true);
        await admin.DisableUserAsync(clerk.Id);
        var disabled = await Assert.ThrowsAsync<ApiError>(() => NewClient().LoginAsync(email, "Reset!12345"));
        Assert.Equal(HttpStatusCode.Forbidden, disabled.Status);
        Assert.Contains("disabled", disabled.Message);
    }
}
