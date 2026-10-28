using System.Net;
using OnTech.Store.Contracts;
using Xunit;

namespace OnTech.Store.Api.Tests;

/// <summary>
/// Goes through every feature the staff use, the way the desktop app calls it: who may call what,
/// bad input, the opening data, and several PCs doing the same thing at the same moment.
/// </summary>
public class FeatureCoverageTests : IClassFixture<StoreApiFactory>
{
    private readonly StoreApiFactory _f;
    public FeatureCoverageTests(StoreApiFactory f) => _f = f;

    private static string Tag() => Guid.NewGuid().ToString("N")[..6];

    private async Task<(HttpClient admin, CategoryDto cat, ItemDto item, CustomerDto customer)> SetupAsync(int qty = 50)
    {
        var admin = await _f.LoginAsync();
        var tag = Tag();
        var cat = await (await admin.Post("/api/categories", new SaveCategoryRequest { Name = $"Test cat {tag}" })).Read<CategoryDto>();
        var item = await admin.AddItem(new CreateItemRequest { CategoryId = cat.Id, Name = $"Test item {tag}", LowStockThreshold = 2 }, qty);
        var customer = await (await admin.Post("/api/customers", new SaveCustomerRequest { CompanyName = $"Test customer {tag}" })).Read<CustomerDto>();
        return (admin, cat, item, customer);
    }

    private async Task<HttpClient> UserWithAsync(HttpClient admin, Permission permissions)
    {
        var username = $"u{Tag()}";
        await (await admin.Post("/api/users", new CreateUserRequest
        {
            FullName = username, Username = username, Password = "Password1!", Permissions = permissions, MustChangePassword = false,
        })).Read<UserDto>();
        return await _f.LoginAsync(username, "Password1!");
    }

    // ---------- Permissions: every guarded endpoint refuses a user without the permission ----------

    public static TheoryData<Permission, string, string> GuardedEndpoints => new()
    {
        { Permission.ViewStock, "GET", "/api/items" },
        { Permission.ViewStock, "GET", "/api/categories" },
        { Permission.ViewStock, "GET", "/api/dashboard/summary" },
        { Permission.ViewStock, "GET", "/api/stock-movements" },
        { Permission.ViewStock, "GET", "/api/items/{item}/card" },
        { Permission.ManageCatalog, "POST", "/api/categories" },
        { Permission.ManageCatalog, "POST", "/api/items" },
        { Permission.ManageCatalog, "DELETE", "/api/items/{item}" },
        { Permission.AddStock, "POST", "/api/stock-ins" },
        { Permission.ViewOrders, "GET", "/api/stock-ins" },
        { Permission.CancelOrders, "POST", "/api/stock-ins/1/cancel" },
        { Permission.ManageSuppliers, "POST", "/api/suppliers" },
        { Permission.ManageSuppliers, "DELETE", "/api/suppliers/{supplier}" },
        { Permission.CreateOrders, "POST", "/api/orders" },
        { Permission.ViewOrders, "GET", "/api/orders" },
        { Permission.CancelOrders, "POST", "/api/orders/1/cancel" },
        { Permission.ManageCustomers, "POST", "/api/customers" },
        { Permission.ManageCustomers, "POST", "/api/customers/import" },
        { Permission.ManageCustomers, "DELETE", "/api/customers/{customer}" },
        { Permission.ManageUsers, "GET", "/api/users" },
        { Permission.ManageUsers, "POST", "/api/users" },
    };

    [Theory]
    [MemberData(nameof(GuardedEndpoints))]
    public async Task Endpoint_is_refused_without_its_permission_and_allowed_with_it(Permission needed, string method, string url)
    {
        var (admin, _, item, customer) = await SetupAsync();
        var supplier = await (await admin.Post("/api/suppliers", new SaveSupplierRequest { CompanyName = $"Guard supplier {Tag()}" })).Read<SupplierDto>();
        url = url.Replace("{item}", item.Id.ToString()).Replace("{customer}", customer.Id.ToString()).Replace("{supplier}", supplier.Id.ToString());

        var without = await UserWithAsync(admin, Permission.All & ~needed);
        var res = await without.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = method == "GET" ? null : System.Net.Http.Json.JsonContent.Create(new { }) });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        var with = await UserWithAsync(admin, needed);
        res = await with.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = method == "GET" ? null : System.Net.Http.Json.JsonContent.Create(new { }) });
        Assert.NotEqual(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, res.StatusCode);
    }

    [Fact]
    public async Task Permission_change_applies_without_signing_in_again()
    {
        var (admin, _, item, customer) = await SetupAsync();
        var username = $"u{Tag()}";
        var user = await (await admin.Post("/api/users", new CreateUserRequest
        {
            FullName = "Shop user", Username = username, Password = "Password1!", Permissions = Permission.StandardUser, MustChangePassword = false,
        })).Read<UserDto>();
        var client = await _f.LoginAsync(username, "Password1!");
        var order = new CreateOrderRequest { IssueVoucherNumber = TestStock.Voucher(), CustomerId = customer.Id, Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 1 } } };
        Assert.Equal(HttpStatusCode.Created, (await client.Post("/api/orders", order)).StatusCode);

        await (await admin.Put($"/api/users/{user.Id}", new UpdateUserRequest
        {
            FullName = "Shop user", Username = username, IsActive = true, Permissions = Permission.ViewStock,
        })).Read<UserDto>();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.Post("/api/orders", order)).StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_lock_themselves_out_through_edit()
    {
        var admin = await _f.LoginAsync();
        var me = await admin.GetAs<UserDto>("/api/auth/me");
        // Make a second admin so the "last admin" rule is not what stops it.
        var other = $"a{Tag()}";
        await (await admin.Post("/api/users", new CreateUserRequest { FullName = other, Username = other, Password = "Password1!", IsAdmin = true })).Read<UserDto>();

        var res = await admin.Put($"/api/users/{me.Id}", new UpdateUserRequest { FullName = me.FullName, Username = me.Username, IsAdmin = true, IsActive = false, Permissions = me.Permissions });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        res = await admin.Put($"/api/users/{me.Id}", new UpdateUserRequest { FullName = me.FullName, Username = me.Username, IsAdmin = false, IsActive = true, Permissions = Permission.StandardUser });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await admin.GetAs<UserDto>("/api/auth/me")).IsAdmin);
    }

    [Fact]
    public async Task Disabled_user_cannot_sign_in()
    {
        var admin = await _f.LoginAsync();
        var username = $"u{Tag()}";
        var user = await (await admin.Post("/api/users", new CreateUserRequest { FullName = username, Username = username, Password = "Password1!" })).Read<UserDto>();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{user.Id}")).StatusCode);
        var res = await _f.CreateClient().Post("/api/auth/login", new LoginRequest { Username = username, Password = "Password1!" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Reset_password_makes_old_password_stop_working()
    {
        var admin = await _f.LoginAsync();
        var username = $"u{Tag()}";
        var user = await (await admin.Post("/api/users", new CreateUserRequest { FullName = username, Username = username, Password = "Password1!" })).Read<UserDto>();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Post($"/api/users/{user.Id}/reset-password", new ResetPasswordRequest { NewPassword = "NewPassw0rd!" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().Post("/api/auth/login", new LoginRequest { Username = username, Password = "Password1!" })).StatusCode);
        var login = await (await _f.CreateClient().Post("/api/auth/login", new LoginRequest { Username = username, Password = "NewPassw0rd!" })).Read<LoginResponse>();
        Assert.True(login.User.MustChangePassword);
    }

    // ---------- Bad input never reaches the database and never gives a 500 ----------

    [Fact]
    public async Task Bad_order_input_is_rejected_with_400()
    {
        var (admin, _, item, customer) = await SetupAsync();
        var bad = new object[]
        {
            new { customerId = customer.Id, lines = Array.Empty<object>() },
            new { customerId = customer.Id, lines = new[] { new { itemId = item.Id, quantity = 0 } } },
            new { customerId = customer.Id, lines = new[] { new { itemId = item.Id, quantity = -5 } } },
            new { customerId = 0, lines = new[] { new { itemId = item.Id, quantity = 1 } } },
            new { customerId = customer.Id, issueVoucherNumber = TestStock.Voucher(), withReceipt = true, receiptNumber = "  ", lines = new[] { new { itemId = item.Id, quantity = 1 } } },
            new { customerId = customer.Id, lines = new[] { new { itemId = item.Id, quantity = 1 } } }, // no issue voucher
            new { customerId = customer.Id, issueVoucherNumber = "   ", lines = new[] { new { itemId = item.Id, quantity = 1 } } },
        };
        foreach (var body in bad)
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/orders", body)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.Post("/api/orders", new { customerId = 999999, issueVoucherNumber = TestStock.Voucher(), lines = new[] { new { itemId = item.Id, quantity = 1 } } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Post("/api/orders", new { customerId = customer.Id, issueVoucherNumber = TestStock.Voucher(), lines = new[] { new { itemId = 999999, quantity = 1 } } })).StatusCode);
        Assert.Equal(50, (await admin.GetAs<ItemDto>($"/api/items/{item.Id}")).Quantity);
    }

    [Fact]
    public async Task Bad_stock_input_is_rejected_with_400()
    {
        var (admin, cat, item, _) = await SetupAsync();
        var supplier = await (await admin.Post("/api/suppliers", new SaveSupplierRequest { CompanyName = $"Bad input supplier {Tag()}" })).Read<SupplierDto>();
        var bad = new object[]
        {
            new { supplierId = supplier.Id, voucherNumber = TestStock.Voucher(), lines = new[] { new { itemId = item.Id, quantity = 0 } } },
            new { supplierId = supplier.Id, voucherNumber = TestStock.Voucher(), lines = new[] { new { itemId = item.Id, quantity = -3 } } },
            new { supplierId = supplier.Id, voucherNumber = TestStock.Voucher(), lines = Array.Empty<object>() },
            new { supplierId = supplier.Id, lines = new[] { new { itemId = item.Id, quantity = 1 } } }, // no voucher
            new { supplierId = supplier.Id, voucherNumber = "  ", lines = new[] { new { itemId = item.Id, quantity = 1 } } },
            new { supplierId = 0, voucherNumber = TestStock.Voucher(), lines = new[] { new { itemId = item.Id, quantity = 1 } } },
        };
        foreach (var body in bad)
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/stock-ins", body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Post("/api/stock-ins", new { supplierId = 999999, voucherNumber = TestStock.Voucher(), lines = new[] { new { itemId = item.Id, quantity = 1 } } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Post("/api/stock-ins", new { supplierId = supplier.Id, voucherNumber = TestStock.Voucher(), lines = new[] { new { itemId = 999999, quantity = 1 } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/items", new { categoryId = cat.Id, name = "" })).StatusCode);
        // The old ways of typing in a quantity are gone.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Post($"/api/items/{item.Id}/add-quantity", new { quantity = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Post($"/api/items/{item.Id}/adjust", new { newQuantity = 3, reason = "count" })).StatusCode);
        var fresh = await (await admin.Post("/api/items", new { categoryId = cat.Id, name = $"Starts empty {Tag()}", initialQuantity = 40 })).Read<ItemDto>();
        Assert.Equal(0, fresh.Quantity);
        Assert.Equal(50, (await admin.GetAs<ItemDto>($"/api/items/{item.Id}")).Quantity);
    }

    [Fact]
    public async Task Bad_paging_is_rejected_with_400()
    {
        var admin = await _f.LoginAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/orders?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/orders?pageSize=100000")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/stock-movements?page=-1")).StatusCode);
    }

    [Fact]
    public async Task Disabled_item_customer_and_category_cannot_be_ordered_or_restocked()
    {
        var (admin, cat, item, customer) = await SetupAsync();
        var line = new OrderLineRequest { ItemId = item.Id, Quantity = 1 };

        await admin.DeleteAsync($"/api/customers/{customer.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/orders", new CreateOrderRequest { IssueVoucherNumber = TestStock.Voucher(), CustomerId = customer.Id, Lines = { line } })).StatusCode);
        Assert.DoesNotContain(await admin.GetAs<List<CustomerDto>>("/api/customers"), c => c.Id == customer.Id);

        var (_, _, _, customer2) = await SetupAsync();
        await admin.DeleteAsync($"/api/items/{item.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/orders", new CreateOrderRequest { IssueVoucherNumber = TestStock.Voucher(), CustomerId = customer2.Id, Lines = { line } })).StatusCode);
        var supplier = await (await admin.Post("/api/suppliers", new SaveSupplierRequest { CompanyName = $"Disabled test supplier {Tag()}" })).Read<SupplierDto>();
        var stockIn = new CreateStockInRequest { SupplierId = supplier.Id, VoucherNumber = TestStock.Voucher(), Lines = { line } };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/stock-ins", stockIn)).StatusCode);
        Assert.DoesNotContain(await admin.GetAs<List<ItemDto>>($"/api/items?categoryId={cat.Id}"), i => i.Id == item.Id);

        // A category with items is disabled, not deleted; its items drop out of the pickers.
        var (_, cat3, item3, _) = await SetupAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/categories/{cat3.Id}")).StatusCode);
        Assert.False((await admin.GetAs<CategoryDto>($"/api/categories/{cat3.Id}")).IsActive);
        Assert.DoesNotContain(await admin.GetAs<List<ItemDto>>("/api/items"), i => i.Id == item3.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/orders", new CreateOrderRequest { IssueVoucherNumber = TestStock.Voucher(), CustomerId = customer2.Id, Lines = { new OrderLineRequest { ItemId = item3.Id, Quantity = 1 } } })).StatusCode);
    }

    // ---------- Catalog, codes and the opening data ----------

    [Fact]
    public async Task Opening_data_matches_the_store_sheet()
    {
        var admin = await _f.LoginAsync();
        var categories = await admin.GetAs<List<CategoryDto>>("/api/categories");
        var opening = categories.Where(c => c.Code is { Length: 2 } code && string.CompareOrdinal(code, "11") <= 0).ToList();
        Assert.Equal(11, opening.Count);
        Assert.Equal("01", opening[0].Code);

        var items = await admin.GetAs<List<ItemDto>>("/api/items?includeInactive=true");
        var openingItems = items.Where(i => opening.Any(c => c.Id == i.CategoryId)).ToList();
        Assert.Equal(318, openingItems.Count);
        Assert.All(openingItems, i => Assert.Matches(@"^\d{5}$", i.Code!));
        Assert.Equal(openingItems.Count, openingItems.Select(i => i.Code).Distinct().Count());
        Assert.All(openingItems, i => Assert.True(i.Quantity >= 0));

        var customers = await admin.GetAs<List<CustomerDto>>("/api/customers?includeInactive=true");
        Assert.True(customers.Count >= 505);
        Assert.Equal(customers.Count, customers.Select(c => c.CustomerNumber).Distinct().Count());
    }

    [Fact]
    public async Task Item_search_finds_by_code_name_and_category_ignoring_case()
    {
        var (admin, cat, item, _) = await SetupAsync();
        Assert.Contains(await admin.GetAs<List<ItemDto>>($"/api/items?search={item.Code}"), i => i.Id == item.Id);
        Assert.Contains(await admin.GetAs<List<ItemDto>>($"/api/items?search={Uri.EscapeDataString(item.Name.ToUpperInvariant())}"), i => i.Id == item.Id);
        Assert.Contains(await admin.GetAs<List<ItemDto>>($"/api/items?search={Uri.EscapeDataString(cat.Name.ToLowerInvariant())}"), i => i.Id == item.Id);
    }

    [Fact]
    public async Task Item_names_are_unique_per_category_and_can_move_category()
    {
        var (admin, cat, item, _) = await SetupAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Post("/api/items", new CreateItemRequest { CategoryId = cat.Id, Name = item.Name.ToUpperInvariant() })).StatusCode);

        var other = await (await admin.Post("/api/categories", new SaveCategoryRequest { Name = $"Other {Tag()}" })).Read<CategoryDto>();
        var moved = await (await admin.Put($"/api/items/{item.Id}", new UpdateItemRequest { CategoryId = other.Id, Name = item.Name, LowStockThreshold = 5 })).Read<ItemDto>();
        Assert.Equal(other.Id, moved.CategoryId);
        Assert.Equal(item.Code, moved.Code);
        Assert.Equal(50, moved.Quantity);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Post("/api/categories", new SaveCategoryRequest { Name = "x" + Tag(), Code = cat.Code!.ToLowerInvariant() })).StatusCode);
    }

    [Fact]
    public async Task Low_stock_shows_on_the_dashboard_and_filters()
    {
        var (admin, _, item, customer) = await SetupAsync(qty: 3);
        await (await admin.Post("/api/orders", new CreateOrderRequest { IssueVoucherNumber = TestStock.Voucher(), CustomerId = customer.Id, Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 1 } } })).Read<OrderDto>();
        var low = await admin.GetAs<List<ItemDto>>("/api/items?lowStockOnly=true");
        Assert.Contains(low, i => i.Id == item.Id && i.IsLowStock && i.Quantity == 2);

        var before = await admin.GetAs<DashboardSummaryDto>("/api/dashboard/summary");
        await (await admin.Post("/api/orders", new CreateOrderRequest { IssueVoucherNumber = TestStock.Voucher(), CustomerId = customer.Id, Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 2 } } })).Read<OrderDto>();
        var after = await admin.GetAs<DashboardSummaryDto>("/api/dashboard/summary");
        Assert.Equal(before.OrdersToday + 1, after.OrdersToday);
        Assert.Equal(before.UnitsOutToday + 2, after.UnitsOutToday);
        Assert.Equal(before.TotalUnitsInStock - 2, after.TotalUnitsInStock);
        Assert.Equal(before.OutOfStockItemCount + 1, after.OutOfStockItemCount);
        Assert.DoesNotContain(await admin.GetAs<List<ItemDto>>("/api/items?inStockOnly=true"), i => i.Id == item.Id);
    }

    // ---------- Customers ----------

    [Fact]
    public async Task Customer_numbers_are_unique_and_editing_keeps_the_number()
    {
        var admin = await _f.LoginAsync();
        var a = await (await admin.Post("/api/customers", new SaveCustomerRequest { CompanyName = $"Num A {Tag()}" })).Read<CustomerDto>();
        var b = await (await admin.Post("/api/customers", new SaveCustomerRequest { CompanyName = $"Num B {Tag()}" })).Read<CustomerDto>();
        Assert.Equal(a.CustomerNumber + 1, b.CustomerNumber);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.Post("/api/customers", new SaveCustomerRequest { CompanyName = $"Num C {Tag()}", CustomerNumber = a.CustomerNumber })).StatusCode);
        var edited = await (await admin.Put($"/api/customers/{b.Id}", new SaveCustomerRequest { CompanyName = b.CompanyName, PhoneNumber = "0123" })).Read<CustomerDto>();
        Assert.Equal(b.CustomerNumber, edited.CustomerNumber);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Put($"/api/customers/{b.Id}", new SaveCustomerRequest { CompanyName = a.CompanyName })).StatusCode);

        var byNumber = await admin.GetAs<List<CustomerDto>>($"/api/customers?search={b.CustomerNumber}");
        Assert.Equal(b.Id, byNumber[0].Id);
        Assert.Contains(await admin.GetAs<List<CustomerDto>>($"/api/customers?search={Uri.EscapeDataString(b.CompanyName.ToUpperInvariant())}"), c => c.Id == b.Id);
    }

    [Fact]
    public async Task Importing_the_same_rows_without_numbers_twice_adds_them_once()
    {
        var admin = await _f.LoginAsync();
        var tag = Tag();
        var req = new ImportCustomersRequest
        {
            Rows =
            {
                new ImportCustomerRow { CompanyName = $"Imp One {tag}", PhoneNumber = "011" },
                new ImportCustomerRow { CompanyName = $"Imp Two {tag}", TaxNumber = "T-1" },
                new ImportCustomerRow { CompanyName = $"Imp One {tag}" },
            },
        };
        var first = await (await admin.Post("/api/customers/import", req)).Read<ImportCustomersResult>();
        Assert.Equal(2, first.Added);
        var second = await (await admin.Post("/api/customers/import", req)).Read<ImportCustomersResult>();
        Assert.Equal(0, second.Added);
        Assert.Single(await admin.GetAs<List<CustomerDto>>($"/api/customers?search={Uri.EscapeDataString($"Imp One {tag}")}"));
    }

    // ---------- Orders log ----------

    [Fact]
    public async Task Orders_log_filters_sort_and_page()
    {
        var (admin, cat, item, customer) = await SetupAsync(qty: 100);
        var tag = Tag();
        var ids = new List<int>();
        for (var i = 1; i <= 5; i++)
        {
            var o = await (await admin.Post("/api/orders", new CreateOrderRequest
            {
                IssueVoucherNumber = TestStock.Voucher(),
                CustomerId = customer.Id, WithReceipt = i % 2 == 0, ReceiptNumber = i % 2 == 0 ? $"R{tag}-{i}" : null,
                Notes = $"note {tag}", Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = i } },
            })).Read<OrderDto>();
            ids.Add(o.Id);
        }

        var all = await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}&pageSize=2");
        Assert.Equal(5, all.TotalCount);
        Assert.Equal(3, all.TotalPages);
        Assert.Equal(ids[4], all.Items[0].Id);
        var page3 = await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}&pageSize=2&page=3");
        Assert.Single(page3.Items);

        Assert.Equal(2, (await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}&withReceipt=true")).TotalCount);
        Assert.Equal(3, (await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}&withReceipt=false")).TotalCount);
        Assert.Equal(5, (await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?categoryId={cat.Id}")).TotalCount);
        Assert.Equal(1, (await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?receiptNumber=R{tag}-4")).TotalCount);
        Assert.Equal(ids[3], (await admin.GetAs<OrderDto>($"/api/orders/by-receipt/R{tag}-4")).Id);

        var oldest = await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}&sort=OldestFirst");
        Assert.Equal(ids[0], oldest.Items[0].Id);
        var biggest = await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}&sort=TotalQuantity");
        Assert.Equal(5, biggest.Items[0].TotalQuantity);

        // Free-text search: customer name in another case, notes, item code, order number.
        Assert.Equal(5, (await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?search={Uri.EscapeDataString(customer.CompanyName.ToUpperInvariant())}")).TotalCount);
        Assert.Equal(5, (await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?search={Uri.EscapeDataString($"NOTE {tag}")}")).TotalCount);
        Assert.Equal(5, (await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?search={item.Code}")).TotalCount);
        var one = await admin.GetAs<OrderDto>($"/api/orders/{ids[2]}");
        Assert.Equal(1, (await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?search={one.OrderNumber}")).TotalCount);

        // Cancelled orders show with their status and can be filtered.
        await (await admin.Post($"/api/orders/{ids[0]}/cancel", new CancelOrderRequest { Reason = "test" })).Read<OrderDto>();
        Assert.Equal(1, (await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}&status=Cancelled")).TotalCount);
        Assert.Equal(100 - (2 + 3 + 4 + 5), (await admin.GetAs<ItemDto>($"/api/items/{item.Id}")).Quantity);
    }

    [Fact]
    public async Task Orders_log_date_filter_uses_the_whole_range()
    {
        var (admin, _, item, customer) = await SetupAsync();
        var o = await (await admin.Post("/api/orders", new CreateOrderRequest { IssueVoucherNumber = TestStock.Voucher(), CustomerId = customer.Id, Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 1 } } })).Read<OrderDto>();
        var from = Uri.EscapeDataString(o.CreatedAt.AddMinutes(-1).ToString("O"));
        var to = Uri.EscapeDataString(o.CreatedAt.AddMinutes(1).ToString("O"));
        Assert.Equal(1, (await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}&from={from}&to={to}")).TotalCount);
        var later = Uri.EscapeDataString(o.CreatedAt.AddMinutes(1).ToString("O"));
        Assert.Equal(0, (await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?customerId={customer.Id}&from={later}")).TotalCount);
    }

    // ---------- 3 PCs at the same time ----------

    [Fact]
    public async Task Three_pcs_adding_customers_at_once_get_different_numbers()
    {
        var admin = await _f.LoginAsync();
        var pcs = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => _f.LoginAsync()));
        var tag = Tag();
        var results = await Task.WhenAll(Enumerable.Range(0, 9).Select(i =>
            pcs[i % 3].Post("/api/customers", new SaveCustomerRequest { CompanyName = $"Rush {tag} {i}" })));
        foreach (var r in results) Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var created = await Task.WhenAll(results.Select(r => r.Read<CustomerDto>()));
        Assert.Equal(9, created.Select(c => c.CustomerNumber).Distinct().Count());
    }

    [Fact]
    public async Task Three_pcs_adding_items_at_once_get_different_codes()
    {
        var admin = await _f.LoginAsync();
        var cat = await (await admin.Post("/api/categories", new SaveCategoryRequest { Name = $"Rush cat {Tag()}" })).Read<CategoryDto>();
        var pcs = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => _f.LoginAsync()));
        var results = await Task.WhenAll(Enumerable.Range(0, 9).Select(i =>
            pcs[i % 3].Post("/api/items", new CreateItemRequest { CategoryId = cat.Id, Name = $"Rush item {i}" })));
        foreach (var r in results) Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var created = await Task.WhenAll(results.Select(r => r.Read<ItemDto>()));
        Assert.Equal(9, created.Select(c => c.Code).Distinct().Count());
    }

    [Fact]
    public async Task Same_receipt_from_two_pcs_at_once_is_used_once()
    {
        var (admin, _, item, customer) = await SetupAsync();
        var pcs = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => _f.LoginAsync()));
        var receipt = $"DUP-{Tag()}";
        var results = await Task.WhenAll(pcs.Select(pc => pc.Post("/api/orders", new CreateOrderRequest
        {
            IssueVoucherNumber = TestStock.Voucher(),
            CustomerId = customer.Id, WithReceipt = true, ReceiptNumber = receipt, Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 1 } },
        })));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(49, (await admin.GetAs<ItemDto>($"/api/items/{item.Id}")).Quantity);
    }

    [Fact]
    public async Task Same_voucher_from_two_pcs_at_once_is_used_once()
    {
        var (admin, _, item, customer) = await SetupAsync();
        var supplier = await (await admin.Post("/api/suppliers", new SaveSupplierRequest { CompanyName = $"Dup supplier {Tag()}" })).Read<SupplierDto>();
        var pcs = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => _f.LoginAsync()));
        var voucher = $"DUP-{Tag()}";

        var orders = await Task.WhenAll(pcs.Select(pc => pc.Post("/api/orders", new CreateOrderRequest
        {
            IssueVoucherNumber = voucher, CustomerId = customer.Id, Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 1 } },
        })));
        Assert.Single(orders, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(orders.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));

        var stockIns = await Task.WhenAll(pcs.Select(pc => pc.Post("/api/stock-ins", new CreateStockInRequest
        {
            VoucherNumber = voucher, SupplierId = supplier.Id, Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 5 } },
        })));
        Assert.Single(stockIns, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(stockIns.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(54, (await admin.GetAs<ItemDto>($"/api/items/{item.Id}")).Quantity);
    }

    [Fact]
    public async Task Mixed_orders_restocks_and_cancels_from_three_pcs_keep_stock_exact()
    {
        var (admin, _, item, customer) = await SetupAsync(qty: 30);
        var pcs = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => _f.LoginAsync()));
        var ordered = 0; var restocked = 0; var returned = 0;
        var orderIds = new System.Collections.Concurrent.ConcurrentBag<int>();
        var supplier = await (await admin.Post("/api/suppliers", new SaveSupplierRequest { CompanyName = $"Rush supplier {Tag()}" })).Read<SupplierDto>();

        await Task.WhenAll(pcs.Select(async (pc, n) =>
        {
            for (var i = 0; i < 12; i++)
            {
                if ((i + n) % 4 == 0)
                {
                    var r = await pc.Post("/api/stock-ins", new CreateStockInRequest
                    {
                        SupplierId = supplier.Id, VoucherNumber = TestStock.Voucher(), Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 2 } },
                    });
                    Assert.Equal(HttpStatusCode.Created, r.StatusCode);
                    Interlocked.Add(ref restocked, 2);
                }
                else if (i % 5 == 4 && orderIds.TryTake(out var id))
                {
                    var r = await pc.Post($"/api/orders/{id}/cancel", new CancelOrderRequest { Reason = "load" });
                    Assert.Equal(HttpStatusCode.OK, r.StatusCode);
                    var o = await r.Read<OrderDto>();
                    Interlocked.Add(ref returned, o.TotalQuantity);
                }
                else
                {
                    var r = await pc.Post("/api/orders", new CreateOrderRequest { IssueVoucherNumber = TestStock.Voucher(), CustomerId = customer.Id, Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 3 } } });
                    Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
                    if (r.StatusCode == HttpStatusCode.Created) { orderIds.Add((await r.Read<OrderDto>()).Id); Interlocked.Add(ref ordered, 3); }
                }
            }
        }));

        var final = await admin.GetAs<ItemDto>($"/api/items/{item.Id}");
        Assert.Equal(30 + restocked - ordered + returned, final.Quantity);
        Assert.True(final.Quantity >= 0);

        // The movement history adds up to the quantity on the shelf, and its last "after" matches it.
        var moves = await admin.GetAs<PagedResult<StockMovementDto>>($"/api/stock-movements?itemId={item.Id}&pageSize=200");
        Assert.Equal(final.Quantity, moves.Items.Sum(m => m.Change));
        Assert.Equal(final.Quantity, moves.Items[0].QuantityAfter);
    }

    [Fact]
    public async Task Item_card_lists_every_movement_with_its_party_voucher_and_balance()
    {
        var (admin, _, item, customer) = await SetupAsync(qty: 0);
        var supplier = await (await admin.Post("/api/suppliers", new SaveSupplierRequest { CompanyName = $"Card supplier {Tag()}" })).Read<SupplierDto>();
        var stockIn = await admin.StockIn(item.Id, 10, supplier);
        var order = await (await admin.Post("/api/orders", new CreateOrderRequest
        {
            IssueVoucherNumber = TestStock.Voucher(), CustomerId = customer.Id, WithReceipt = true, ReceiptNumber = $"R{Tag()}",
            Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 4 } },
        })).Read<OrderDto>();
        await (await admin.Post($"/api/orders/{order.Id}/cancel", new CancelOrderRequest { Reason = "test" })).Read<OrderDto>();

        var card = await admin.GetAs<ItemCardDto>($"/api/items/{item.Id}/card");

        Assert.Equal(item.Id, card.Item.Id);
        Assert.Equal(10, card.Item.Quantity);
        Assert.Equal(14, card.TotalIn);
        Assert.Equal(4, card.TotalOut);
        Assert.Equal([StockMovementType.StockIn, StockMovementType.Order, StockMovementType.OrderCancelled], card.Lines.Select(l => l.Type));
        Assert.Equal([10, 6, 10], card.Lines.Select(l => l.Balance));

        var received = card.Lines[0];
        Assert.Equal((10, 0), (received.In, received.Out));
        Assert.Equal(supplier.CompanyName, received.PartyName);
        Assert.Equal(stockIn.StockInNumber, received.DocumentNumber);
        Assert.Equal(stockIn.VoucherNumber, received.VoucherNumber);

        var issued = card.Lines[1];
        Assert.Equal((0, 4), (issued.In, issued.Out));
        Assert.Equal(customer.CompanyName, issued.PartyName);
        Assert.Equal(order.OrderNumber, issued.DocumentNumber);
        Assert.Equal(order.IssueVoucherNumber, issued.VoucherNumber);
        Assert.Equal(order.ReceiptNumber, issued.InvoiceNumber);

        Assert.Equal(404, (int)(await admin.GetAsync("/api/items/999999/card")).StatusCode);
    }
}
