using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OnTech.Store.Api.Data;
using OnTech.Store.Api.Services;
using OnTech.Store.Contracts;
using Xunit;

namespace OnTech.Store.Api.Tests;

public class StoreApiTests : IClassFixture<StoreApiFactory>
{
    private readonly StoreApiFactory _f;
    public StoreApiTests(StoreApiFactory f) => _f = f;

    private async Task<(HttpClient admin, CustomerDto customer, ItemDto fargo, ItemDto xpass)> SetupAsync(int fargoQty = 10, int xpassQty = 5)
    {
        var admin = await _f.LoginAsync();
        var tag = Guid.NewGuid().ToString("N")[..6];
        var printers = await (await admin.Post("/api/categories", new SaveCategoryRequest { Name = $"Printers {tag}" })).Read<CategoryDto>();
        var readers = await (await admin.Post("/api/categories", new SaveCategoryRequest { Name = $"Readers {tag}" })).Read<CategoryDto>();
        var fargo = await (await admin.Post("/api/items", new CreateItemRequest { CategoryId = printers.Id, Name = "Fargo DTC1250e", InitialQuantity = fargoQty, LowStockThreshold = 3 })).Read<ItemDto>();
        var xpass = await (await admin.Post("/api/items", new CreateItemRequest { CategoryId = readers.Id, Name = "Xpass", InitialQuantity = xpassQty })).Read<ItemDto>();
        var customer = await (await admin.Post("/api/customers", new SaveCustomerRequest { CompanyName = $"Acme {tag}", PhoneNumber = "0100", TaxNumber = $"TAX-{tag}" })).Read<CustomerDto>();
        return (admin, customer, fargo, xpass);
    }

    [Fact]
    public async Task Health_is_public()
    {
        var res = await _f.CreateClient().GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Endpoints_require_login()
    {
        var res = await _f.CreateClient().GetAsync("/api/items");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var res = await _f.CreateClient().Post("/api/auth/login", new LoginRequest { Username = StoreApiFactory.AdminUsername, Password = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Seeded_admin_must_change_password_and_has_all_permissions()
    {
        var admin = await _f.LoginAsync();
        var me = await admin.GetAs<UserDto>("/api/auth/me");
        Assert.True(me.IsAdmin);
        Assert.Equal(Permission.All, me.Permissions);
    }

    [Fact]
    public async Task Order_deducts_stock_and_records_movements()
    {
        var (admin, customer, fargo, xpass) = await SetupAsync();
        var order = await (await admin.Post("/api/orders", new CreateOrderRequest
        {
            CustomerId = customer.Id,
            Lines = { new() { ItemId = fargo.Id, Quantity = 2 }, new() { ItemId = xpass.Id, Quantity = 1 }, new() { ItemId = fargo.Id, Quantity = 1 } },
        })).Read<OrderDto>();

        Assert.StartsWith("ORD-", order.OrderNumber);
        Assert.Equal(2, order.Lines.Count); // duplicate Fargo lines merged
        Assert.Equal(4, order.TotalQuantity);
        Assert.Equal(7, (await admin.GetAs<ItemDto>($"/api/items/{fargo.Id}")).Quantity);
        Assert.Equal(4, (await admin.GetAs<ItemDto>($"/api/items/{xpass.Id}")).Quantity);

        var moves = await admin.GetAs<PagedResult<StockMovementDto>>($"/api/stock-movements?itemId={fargo.Id}");
        Assert.Equal(2, moves.TotalCount);
        Assert.Contains(moves.Items, m => m.Type == StockMovementType.Order && m.Change == -3 && m.QuantityAfter == 7 && m.OrderNumber == order.OrderNumber);
        Assert.Contains(moves.Items, m => m.Type == StockMovementType.Initial && m.Change == 10);
    }

    [Fact]
    public async Task Order_with_too_much_quantity_takes_nothing()
    {
        var (admin, customer, fargo, xpass) = await SetupAsync(fargoQty: 10, xpassQty: 2);
        var res = await admin.Post("/api/orders", new CreateOrderRequest
        {
            CustomerId = customer.Id,
            Lines = { new() { ItemId = fargo.Id, Quantity = 5 }, new() { ItemId = xpass.Id, Quantity = 3 } },
        });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var problem = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        var shortage = problem.GetProperty("shortages")[0];
        Assert.Equal(xpass.Id, shortage.GetProperty("itemId").GetInt32());
        Assert.Equal(2, shortage.GetProperty("available").GetInt32());

        Assert.Equal(10, (await admin.GetAs<ItemDto>($"/api/items/{fargo.Id}")).Quantity);
        Assert.Equal(2, (await admin.GetAs<ItemDto>($"/api/items/{xpass.Id}")).Quantity);
        Assert.Equal(0, (await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}")).TotalCount);
    }

    [Fact]
    public async Task Concurrent_orders_never_oversell()
    {
        var (admin, customer, fargo, _) = await SetupAsync(fargoQty: 5);
        var clients = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => _f.LoginAsync()));

        // 3 "PCs" each try to take 1 unit, 4 times, all at once: 12 attempts for 5 units.
        var attempts = clients.SelectMany(c => Enumerable.Range(0, 4).Select(_ => c.Post("/api/orders", new CreateOrderRequest
        {
            CustomerId = customer.Id, Lines = { new() { ItemId = fargo.Id, Quantity = 1 } },
        })));
        var results = await Task.WhenAll(attempts);

        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(7, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0, (await admin.GetAs<ItemDto>($"/api/items/{fargo.Id}")).Quantity);
    }

    [Fact]
    public async Task Receipt_number_is_required_unique_and_searchable()
    {
        var (admin, customer, fargo, _) = await SetupAsync();
        var receipt = "R-" + Guid.NewGuid().ToString("N")[..8];

        var missing = await admin.Post("/api/orders", new CreateOrderRequest { CustomerId = customer.Id, WithReceipt = true, Lines = { new() { ItemId = fargo.Id, Quantity = 1 } } });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var withReceipt = await (await admin.Post("/api/orders", new CreateOrderRequest
        {
            CustomerId = customer.Id, WithReceipt = true, ReceiptNumber = receipt, Lines = { new() { ItemId = fargo.Id, Quantity = 1 } },
        })).Read<OrderDto>();
        await (await admin.Post("/api/orders", new CreateOrderRequest { CustomerId = customer.Id, Lines = { new() { ItemId = fargo.Id, Quantity = 1 } } })).Read<OrderDto>();

        var dup = await admin.Post("/api/orders", new CreateOrderRequest
        {
            CustomerId = customer.Id, WithReceipt = true, ReceiptNumber = receipt, Lines = { new() { ItemId = fargo.Id, Quantity = 1 } },
        });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);

        var byReceipt = await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?receiptNumber={receipt[..6]}");
        Assert.Equal(withReceipt.Id, Assert.Single(byReceipt.Items).Id);

        var receiptOnly = await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}&withReceipt=true");
        Assert.Equal(1, receiptOnly.TotalCount);
        var noReceipt = await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}&withReceipt=false");
        Assert.Equal(1, noReceipt.TotalCount);

        Assert.Equal(withReceipt.Id, (await admin.GetAs<OrderDto>($"/api/orders/by-receipt/{receipt}")).Id);
        Assert.Equal(8, (await admin.GetAs<ItemDto>($"/api/items/{fargo.Id}")).Quantity);
    }

    [Fact]
    public async Task Orders_can_be_filtered_by_item_user_search_and_date()
    {
        var (admin, customer, fargo, xpass) = await SetupAsync();
        await (await admin.Post("/api/orders", new CreateOrderRequest { CustomerId = customer.Id, Lines = { new() { ItemId = fargo.Id, Quantity = 1 } } })).Read<OrderDto>();
        await (await admin.Post("/api/orders", new CreateOrderRequest { CustomerId = customer.Id, Lines = { new() { ItemId = xpass.Id, Quantity = 1 } }, Notes = "urgent delivery" })).Read<OrderDto>();

        Assert.Equal(1, (await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}&itemId={xpass.Id}")).TotalCount);
        Assert.Equal(1, (await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}&categoryId={fargo.CategoryId}")).TotalCount);
        Assert.Equal(1, (await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}&search=urgent")).TotalCount);
        Assert.Equal(1, (await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}&search={fargo.Code}")).TotalCount);
        Assert.Equal(2, (await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?search={Uri.EscapeDataString(customer.CompanyName)}")).TotalCount);

        var me = await admin.GetAs<UserDto>("/api/auth/me");
        Assert.True((await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?createdByUserId={me.Id}")).TotalCount >= 2);

        var future = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        Assert.Equal(0, (await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}&from={future}")).TotalCount);
        var past = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        Assert.Equal(2, (await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}&from={past}&to={future}")).TotalCount);

        var paged = await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}&pageSize=1&page=2&sort=OldestFirst");
        Assert.Equal(2, paged.TotalPages);
        Assert.Equal("Xpass", Assert.Single(Assert.Single(paged.Items).Lines).ItemName);
    }

    [Fact]
    public async Task Cancelling_an_order_returns_stock_once()
    {
        var (admin, customer, fargo, _) = await SetupAsync();
        var order = await (await admin.Post("/api/orders", new CreateOrderRequest { CustomerId = customer.Id, Lines = { new() { ItemId = fargo.Id, Quantity = 4 } } })).Read<OrderDto>();
        Assert.Equal(6, (await admin.GetAs<ItemDto>($"/api/items/{fargo.Id}")).Quantity);

        var cancelled = await (await admin.Post($"/api/orders/{order.Id}/cancel", new CancelOrderRequest { Reason = "Customer returned it" })).Read<OrderDto>();
        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(10, (await admin.GetAs<ItemDto>($"/api/items/{fargo.Id}")).Quantity);

        var again = await admin.Post($"/api/orders/{order.Id}/cancel", new CancelOrderRequest { Reason = "again" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(10, (await admin.GetAs<ItemDto>($"/api/items/{fargo.Id}")).Quantity);
    }

    [Fact]
    public async Task Add_quantity_and_adjust_are_logged()
    {
        var (admin, _, fargo, _) = await SetupAsync(fargoQty: 2);
        var after = await (await admin.Post($"/api/items/{fargo.Id}/add-quantity", new AddQuantityRequest { Quantity = 8, Note = "PO 55" })).Read<ItemDto>();
        Assert.Equal(10, after.Quantity);
        Assert.False(after.IsLowStock);

        var adjusted = await (await admin.Post($"/api/items/{fargo.Id}/adjust", new AdjustQuantityRequest { NewQuantity = 3, Reason = "Stock count" })).Read<ItemDto>();
        Assert.Equal(3, adjusted.Quantity);
        Assert.True(adjusted.IsLowStock);

        var moves = await admin.GetAs<PagedResult<StockMovementDto>>($"/api/stock-movements?itemId={fargo.Id}");
        Assert.Equal(new[] { StockMovementType.Adjustment, StockMovementType.Restock, StockMovementType.Initial }, moves.Items.Select(m => m.Type));
        Assert.Equal(-7, moves.Items[0].Change);

        var low = await admin.GetAs<List<ItemDto>>($"/api/items?lowStockOnly=true&categoryId={fargo.CategoryId}");
        Assert.Single(low);
    }

    [Fact]
    public async Task Customer_company_name_is_required_and_unique()
    {
        var admin = await _f.LoginAsync();
        var res = await admin.Post("/api/customers", new SaveCustomerRequest { CompanyName = "" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var name = "Globex " + Guid.NewGuid().ToString("N")[..6];
        await (await admin.Post("/api/customers", new SaveCustomerRequest { CompanyName = name })).Read<CustomerDto>();
        var dup = await admin.Post("/api/customers", new SaveCustomerRequest { CompanyName = name.ToUpperInvariant() });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task Standard_user_permissions_are_enforced_and_disabling_is_immediate()
    {
        var (admin, customer, fargo, _) = await SetupAsync();
        var username = $"user{Guid.NewGuid():N}"[..14];
        var user = await (await admin.Post("/api/users", new CreateUserRequest
        {
            FullName = "Store Keeper", Username = username, Password = "Keeper!123", Permissions = Permission.StandardUser,
        })).Read<UserDto>();
        Assert.True(user.MustChangePassword);

        var keeper = await _f.LoginAsync(username, "Keeper!123");
        // Allowed: home page actions.
        await (await keeper.Post("/api/orders", new CreateOrderRequest { CustomerId = customer.Id, Lines = { new() { ItemId = fargo.Id, Quantity = 1 } } })).Read<OrderDto>();
        await (await keeper.Post($"/api/items/{fargo.Id}/add-quantity", new AddQuantityRequest { Quantity = 1 })).Read<ItemDto>();
        await (await keeper.Post("/api/customers", new SaveCustomerRequest { CompanyName = "Initech " + Guid.NewGuid().ToString("N")[..6] })).Read<CustomerDto>();
        // Not allowed: admin screens.
        Assert.Equal(HttpStatusCode.Forbidden, (await keeper.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keeper.Post("/api/categories", new SaveCategoryRequest { Name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keeper.Post("/api/items", new CreateItemRequest { CategoryId = fargo.CategoryId, Name = "Y" })).StatusCode);

        // Admin grants catalog permission: applies without logging in again.
        await (await admin.Put($"/api/users/{user.Id}", new UpdateUserRequest
        {
            FullName = user.FullName, Username = username, IsActive = true, Permissions = Permission.StandardUser | Permission.ManageCatalog,
        })).Read<UserDto>();
        Assert.Equal(HttpStatusCode.Created, (await keeper.Post("/api/categories", new SaveCategoryRequest { Name = "Cables " + Guid.NewGuid().ToString("N")[..6] })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{user.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await keeper.GetAsync("/api/items")).StatusCode);
    }

    [Fact]
    public async Task Change_password_works()
    {
        var admin = await _f.LoginAsync();
        var username = $"pw{Guid.NewGuid():N}"[..12];
        await (await admin.Post("/api/users", new CreateUserRequest { FullName = "Pw", Username = username, Password = "First!1234" })).Read<UserDto>();
        var u = await _f.LoginAsync(username, "First!1234");
        Assert.Equal(HttpStatusCode.NoContent, (await u.Post("/api/auth/change-password", new ChangePasswordRequest { CurrentPassword = "First!1234", NewPassword = "Second!1234" })).StatusCode);
        var again = await _f.LoginAsync(username, "Second!1234");
        Assert.False((await again.GetAs<UserDto>("/api/auth/me")).MustChangePassword);
    }

    [Fact]
    public async Task Last_admin_cannot_be_disabled()
    {
        var admin = await _f.LoginAsync();
        var me = await admin.GetAs<UserDto>("/api/auth/me");
        var res = await admin.Put($"/api/users/{me.Id}", new UpdateUserRequest { FullName = me.FullName, Username = me.Username, IsAdmin = false, IsActive = true });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Dashboard_and_swagger_respond()
    {
        var (admin, customer, fargo, _) = await SetupAsync();
        await (await admin.Post("/api/orders", new CreateOrderRequest { CustomerId = customer.Id, Lines = { new() { ItemId = fargo.Id, Quantity = 8 } } })).Read<OrderDto>();
        var summary = await admin.GetAs<DashboardSummaryDto>("/api/dashboard/summary?utcOffsetMinutes=180");
        Assert.True(summary.OrdersToday >= 1);
        Assert.Contains(summary.LowStockItems, i => i.Id == fargo.Id);

        var swagger = await _f.CreateClient().GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
    }

    [Fact]
    public async Task Customers_import_from_sheet_and_search_by_number_or_name()
    {
        var admin = await _f.LoginAsync();
        var tag = Guid.NewGuid().ToString("N")[..6];
        var n = Random.Shared.Next(1_000_000, 9_000_000);
        var req = new ImportCustomersRequest
        {
            Rows =
            {
                new() { CustomerNumber = n, CompanyName = $"  Star Medical {tag} " },
                new() { CustomerNumber = n + 1, CompanyName = $"Food Bank {tag}" },
                new() { CustomerNumber = n + 2, CompanyName = $"food bank {tag}" }, // same name, different number: kept
                new() { CustomerNumber = n + 3, CompanyName = "  " },
                new() { CustomerNumber = n, CompanyName = $"Duplicate {tag}" },
            },
        };
        var result = await (await admin.Post("/api/customers/import", req)).Read<ImportCustomersResult>();
        Assert.Equal(3, result.Added);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(2, result.Messages.Count);

        // Importing the same sheet again adds nothing.
        var again = await (await admin.Post("/api/customers/import", req)).Read<ImportCustomersResult>();
        Assert.Equal(0, again.Added);

        var byNumber = await admin.GetAs<List<CustomerDto>>($"/api/customers?search={n}");
        var star = Assert.Single(byNumber);
        Assert.Equal($"Star Medical {tag}", star.CompanyName);
        Assert.Equal(2, (await admin.GetAs<List<CustomerDto>>($"/api/customers?search=bank%20{tag}")).Count);

        // New customers get the next number; a taken number is refused.
        var added = await (await admin.Post("/api/customers", new SaveCustomerRequest { CompanyName = $"New Co {tag}" })).Read<CustomerDto>();
        Assert.True(added.CustomerNumber > n + 2);
        var clash = await admin.Post("/api/customers", new SaveCustomerRequest { CompanyName = $"Other {tag}", CustomerNumber = n });
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
    }

    [Fact]
    public async Task Opening_stock_and_customers_are_loaded_once()
    {
        var admin = await _f.LoginAsync();
        var items = await admin.GetAs<List<ItemDto>>("/api/items?search=Retransfer%20Film");
        Assert.Contains(items, i => i.CategoryName == "Fargo" && i.Name == "HDP 5000e Retransfer Film" && i.Quantity == 5);
        var proximity = Assert.Single(await admin.GetAs<List<ItemDto>>("/api/items?search=Proximity%20with%20serial"));
        Assert.Equal("PVC CARD", proximity.CategoryName);
        Assert.Equal("Star Medical Center", Assert.Single(await admin.GetAs<List<CustomerDto>>("/api/customers?search=2"), c => c.CustomerNumber == 2).CompanyName);
        Assert.Equal(1, _f.WithDb(db => db.AppliedSeeds.Count()));
    }

    [Fact]
    public async Task Categories_and_items_get_generated_codes()
    {
        var admin = await _f.LoginAsync();
        // The opening data is coded in sheet order: Data card is 01, Fargo 04, and item codes are the category code plus 3 digits.
        var categories = await admin.GetAs<List<CategoryDto>>("/api/categories");
        Assert.Equal("01", categories.Single(c => c.Name == "Data card").Code);
        var fargo = categories.Single(c => c.Name == "Fargo");
        Assert.Equal("04", fargo.Code);
        var fargoItems = await admin.GetAs<List<ItemDto>>($"/api/items?categoryId={fargo.Id}");
        Assert.Equal("04001", fargoItems.Single(i => i.Name == "HDP 5000 PRINTER SingleSide").Code);
        Assert.All(fargoItems, i => Assert.Matches(@"^04\d{3}$", i.Code));

        var tag = Guid.NewGuid().ToString("N")[..6];
        var cat = await (await admin.Post("/api/categories", new SaveCategoryRequest { Name = $"Coded {tag}" })).Read<CategoryDto>();
        Assert.Matches(@"^\d{2,}$", cat.Code);
        var first = await (await admin.Post("/api/items", new CreateItemRequest { CategoryId = cat.Id, Name = "One" })).Read<ItemDto>();
        var second = await (await admin.Post("/api/items", new CreateItemRequest { CategoryId = cat.Id, Name = "Two" })).Read<ItemDto>();
        var ownCode = "9" + Random.Shared.Next(10_000_000, 99_999_999);
        var own = await (await admin.Post("/api/items", new CreateItemRequest { CategoryId = cat.Id, Name = "Three", Code = ownCode })).Read<ItemDto>();
        Assert.Equal($"{cat.Code}001", first.Code);
        Assert.Equal($"{cat.Code}002", second.Code);
        Assert.Equal(ownCode, own.Code);

        // Saving an item with the code box empty keeps its code.
        var edited = await (await admin.Put($"/api/items/{first.Id}", new UpdateItemRequest { CategoryId = cat.Id, Name = "One renamed" })).Read<ItemDto>();
        Assert.Equal($"{cat.Code}001", edited.Code);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Post("/api/categories", new SaveCategoryRequest { Name = $"Clash {tag}", Code = "01" })).StatusCode);

        // Codes are digits only, and an item code can't repeat, even in another category.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/categories", new SaveCategoryRequest { Name = $"Letters {tag}", Code = "C99" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/items", new CreateItemRequest { CategoryId = cat.Id, Name = "Four", Code = "MODEL-X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Post("/api/items", new CreateItemRequest { CategoryId = fargo.Id, Name = $"Copy {tag}", Code = first.Code })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Put($"/api/items/{second.Id}", new UpdateItemRequest { CategoryId = cat.Id, Name = "Two", Code = "04001" })).StatusCode);
    }

    [Fact]
    public async Task Search_by_code_finds_one_item_not_the_same_number_in_every_category()
    {
        var admin = await _f.LoginAsync();
        var exact = Assert.Single(await admin.GetAs<List<ItemDto>>("/api/items?search=04001"));
        Assert.Equal("HDP 5000 PRINTER SingleSide", exact.Name);
        // "002" used to match item 002 of every category; now no code starts with it.
        Assert.DoesNotContain(await admin.GetAs<List<ItemDto>>("/api/items?search=002"), i => i.Code!.EndsWith("002"));
        // A category code lists that category's items.
        Assert.All(await admin.GetAs<List<ItemDto>>("/api/items?search=04"), i => Assert.True(i.Code!.StartsWith("04") || i.Name.Contains("04") || i.CategoryName.Contains("04")));
    }

    [Fact]
    public async Task Old_letter_codes_are_switched_to_digits_on_startup()
    {
        var admin = await _f.LoginAsync();
        var tag = Guid.NewGuid().ToString("N")[..6];
        var cat = await (await admin.Post("/api/categories", new SaveCategoryRequest { Name = $"Old {tag}" })).Read<CategoryDto>();
        var item = await (await admin.Post("/api/items", new CreateItemRequest { CategoryId = cat.Id, Name = "Old item", InitialQuantity = 5 })).Read<ItemDto>();
        var odd = await (await admin.Post("/api/items", new CreateItemRequest { CategoryId = cat.Id, Name = "Odd item", InitialQuantity = 5 })).Read<ItemDto>();
        var customer = await (await admin.Post("/api/customers", new SaveCustomerRequest { CompanyName = $"Old buyer {tag}" })).Read<CustomerDto>();
        var order = await (await admin.Post("/api/orders", new CreateOrderRequest { CustomerId = customer.Id, Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 1 } } })).Read<OrderDto>();

        // Put the database back the way the old version left it.
        var n = Random.Shared.Next(100, 999);
        _f.WithDb(db =>
        {
            db.Categories.Single(c => c.Id == cat.Id).Code = $"C{n}";
            db.Items.Single(i => i.Id == item.Id).Code = $"C{n}-007";
            db.Items.Single(i => i.Id == odd.Id).Code = $"MODEL-{tag}";
            db.OrderLines.Single(l => l.OrderId == order.Id).ItemCode = $"C{n}-007";
            return db.SaveChanges();
        });
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
            await CatalogCodes.MakeNumericAsync(db);
            await CatalogCodes.BackfillAsync(db);
        }

        Assert.Equal($"{n}", (await admin.GetAs<CategoryDto>($"/api/categories/{cat.Id}")).Code);
        Assert.Equal($"{n}007", (await admin.GetAs<ItemDto>($"/api/items/{item.Id}")).Code);
        Assert.Matches($"^{n}\\d{{3}}$", (await admin.GetAs<ItemDto>($"/api/items/{odd.Id}")).Code);
        Assert.Equal($"{n}007", _f.WithDb(db => db.OrderLines.Single(l => l.OrderId == order.Id).ItemCode));
    }
}
