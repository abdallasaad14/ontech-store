using System.Net;
using System.Text.Json;
using OnTech.Store.Client;
using OnTech.Store.Contracts;
using Xunit.Abstractions;

namespace OnTech.Store.Tests;

/// <summary>Phase 3.4 + Phase 4: every endpoint the app calls, with realistic and edge-case input, on both servers.</summary>
public class ApiTests(ITestOutputHelper out_)
{
    // ---------------- happy path: every client method the desktop app uses ----------------

    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task Every_endpoint_the_app_calls_answers_and_matches_the_client_dto(string server)
    {
        var s = Server.ByName(server);
        var api = await Api.AdminAsync(s);
        var item = await Api.ItemAsync(s, 5);
        var cust = await Api.CustomerAsync(s);
        var calls = new List<(string Name, Func<Task<object?>> Call)>
        {
            ("health", async () => { await api.CheckHealthAsync(); return "ok"; }),
            ("me", async () => await api.RefreshMeAsync()),
            ("dashboard", async () => await api.GetDashboardAsync()),
            ("categories", async () => await api.GetCategoriesAsync(true)),
            ("items", async () => await api.GetItemsAsync()),
            ("items?search", async () => await api.GetItemsAsync(search: "audit")),
            ("items?lowStock", async () => await api.GetItemsAsync(lowStockOnly: true)),
            ("item", async () => await api.GetItemAsync(item.Id)),
            ("item card", async () => await api.GetItemCardAsync(item.Id)),
            ("item card period", async () => await api.GetItemCardAsync(item.Id, new ItemCardQuery { From = DateTimeOffset.Now.AddDays(-30), To = DateTimeOffset.Now })),
            ("customers", async () => await api.GetCustomersAsync()),
            ("customers?search", async () => await api.GetCustomersAsync("1")),
            ("suppliers", async () => await api.GetSuppliersAsync()),
            ("users", async () => await api.GetUsersAsync()),
            ("orders", async () => await api.SearchOrdersAsync(new OrderQuery())),
            ("orders filtered", async () => await api.SearchOrdersAsync(new OrderQuery { Search = "a", WithReceipt = false, Status = OrderStatus.Completed, Sort = OrderSort.TotalQuantity, From = DateTimeOffset.Now.AddYears(-1) })),
            ("stock-ins", async () => await api.SearchStockInsAsync(new StockInQuery())),
            ("create order", async () => await api.CreateOrderAsync(Api.Order(cust.Id, (item.Id, 1)))),
        };
        var failures = new List<string>();
        foreach (var (name, call) in calls)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { var r = await call(); Evidence.Log($"endpoints-{server}", $"OK   {name} {sw.ElapsedMilliseconds} ms"); }
            catch (Exception e) { failures.Add($"{name}: {e.GetType().Name} {e.Message}"); Evidence.Log($"endpoints-{server}", $"FAIL {name}: {e.Message}"); }
        }
        foreach (var f in failures) out_.WriteLine(f);
        Assert.Empty(failures);
    }

    // ---------------- edge-case input ----------------

    public static IEnumerable<object[]> CustomerNames()
    {
        foreach (var s in new[] { "sqlserver", "sqlite" })
        {
            yield return [s, "شركة النور للتجارة - " + Guid.NewGuid().ToString("N")[..6], true];   // Arabic
            yield return [s, "Mixed عربي English 123 " + Guid.NewGuid().ToString("N")[..6], true];
            yield return [s, new string('x', 194) + Guid.NewGuid().ToString("N")[..6], true];      // exactly 200
            yield return [s, new string('x', 195) + Guid.NewGuid().ToString("N")[..6], false];     // 201
            yield return [s, "", false];
            yield return [s, "    ", false];
            yield return [s, "O'Brien \"; DROP TABLE Customers;-- " + Guid.NewGuid().ToString("N")[..6], true]; // injection text stored as text
        }
    }

    [Theory, MemberData(nameof(CustomerNames))]
    public async Task Customer_names_round_trip_or_are_refused_with_a_message(string server, string name, bool valid)
    {
        var s = Server.ByName(server);
        var api = await Api.AdminAsync(s);
        if (valid)
        {
            var c = await api.CreateCustomerAsync(new SaveCustomerRequest { CompanyName = name });
            Assert.Equal(name.Trim(), c.CompanyName);
            var back = (await api.GetCustomersAsync(name[..Math.Min(20, name.Length)])).Single(x => x.Id == c.Id);
            Assert.Equal(name.Trim(), back.CompanyName);
            Assert.Equal(name.Trim(), s.Scalar("SELECT CompanyName FROM Customers WHERE Id=@id", ("@id", c.Id)));
        }
        else
        {
            var e = await Assert.ThrowsAsync<ApiError>(() => api.CreateCustomerAsync(new SaveCustomerRequest { CompanyName = name }));
            Assert.Equal(HttpStatusCode.BadRequest, e.Status);
            Evidence.Log($"validation-{server}", $"customer name len={name.Length}: {(int?)e.Status} {e.Message.Replace("\n", " | ")}");
        }
    }

    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task Order_quantities_zero_negative_and_too_big_are_refused_and_take_nothing(string server)
    {
        var s = Server.ByName(server);
        var api = await Api.AdminAsync(s);
        var item = await Api.ItemAsync(s, 3);
        var cust = await Api.CustomerAsync(s);
        foreach (var qty in new[] { 0, -1, -5, 1_000_001 })
        {
            var e = await Assert.ThrowsAsync<ApiError>(() => api.CreateOrderAsync(Api.Order(cust.Id, (item.Id, qty))));
            Assert.Equal(HttpStatusCode.BadRequest, e.Status);
            Evidence.Log($"validation-{server}", $"order qty={qty}: {(int?)e.Status} {e.Message.Replace("\n", " | ")}");
        }
        var shortage = await Assert.ThrowsAsync<ApiError>(() => api.CreateOrderAsync(Api.Order(cust.Id, (item.Id, 4))));
        Assert.Equal(HttpStatusCode.Conflict, shortage.Status);
        Assert.Single(shortage.Shortages);
        Assert.Equal(3, s.ItemQty(item.Id));
        Assert.Equal(0, s.Count("Orders", $"CustomerId={cust.Id}"));
    }

    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task Same_item_twice_in_one_order_becomes_one_line(string server)
    {
        var s = Server.ByName(server);
        var api = await Api.AdminAsync(s);
        var item = await Api.ItemAsync(s, 10);
        var cust = await Api.CustomerAsync(s);
        var o = await api.CreateOrderAsync(Api.Order(cust.Id, (item.Id, 2), (item.Id, 3)));
        Assert.Single(o.Lines);
        Assert.Equal(5, o.TotalQuantity);
        Assert.Equal(5, s.ItemQty(item.Id));
    }

    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task Duplicate_and_too_long_voucher_numbers_are_refused(string server)
    {
        var s = Server.ByName(server);
        var api = await Api.AdminAsync(s);
        var item = await Api.ItemAsync(s, 10);
        var cust = await Api.CustomerAsync(s);
        var first = Api.Order(cust.Id, (item.Id, 1));
        await api.CreateOrderAsync(first);
        var dup = Api.Order(cust.Id, (item.Id, 1)); dup.IssueVoucherNumber = first.IssueVoucherNumber;
        Assert.Equal(HttpStatusCode.Conflict, (await Assert.ThrowsAsync<ApiError>(() => api.CreateOrderAsync(dup))).Status);
        var longV = Api.Order(cust.Id, (item.Id, 1)); longV.IssueVoucherNumber = new string('9', 31);
        Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<ApiError>(() => api.CreateOrderAsync(longV))).Status);
        Assert.Equal(9, s.ItemQty(item.Id));
    }

    /// <summary>"AB-1" and "ab-1": the same voucher on SQL Server (case-insensitive collation), two different vouchers on SQLite.</summary>
    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task Voucher_uniqueness_ignores_letter_case_on_both_databases(string server)
    {
        var s = Server.ByName(server);
        var api = await Api.AdminAsync(s);
        var item = await Api.ItemAsync(s, 10);
        var cust = await Api.CustomerAsync(s);
        var upper = Api.Order(cust.Id, (item.Id, 1)); upper.IssueVoucherNumber = "CASE-" + Api.Run + "-AB";
        await api.CreateOrderAsync(upper);
        var lower = Api.Order(cust.Id, (item.Id, 1)); lower.IssueVoucherNumber = upper.IssueVoucherNumber.ToLowerInvariant();
        var ex = await Record.ExceptionAsync(() => api.CreateOrderAsync(lower));
        Evidence.Log("case-sensitivity", $"{server}: second voucher '{lower.IssueVoucherNumber}' -> {(ex is ApiError a ? $"{(int?)a.Status} {a.Message}" : "201 Created (accepted)")}");
        Assert.IsType<ApiError>(ex);
    }

    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task Paging_and_search_limits_return_400_not_500(string server)
    {
        var s = Server.ByName(server);
        var token = await Api.TokenAsync(s, "admin", Server.AdminPassword);
        using var h = Api.Raw(s, token);
        var cases = new[]
        {
            "api/orders?page=0", "api/orders?pageSize=0", "api/orders?pageSize=201", "api/orders?page=-1",
            "api/orders?search=" + new string('a', 101), "api/stock-ins?page=0", "api/orders?from=not-a-date",
            "api/orders?status=99", "api/orders?sort=99", "api/items/2147483647", "api/items/abc",
            "api/orders?page=2147483647", "api/dashboard/summary?utcOffsetMinutes=99999",
        };
        var bad = new List<string>();
        foreach (var url in cases)
        {
            var (st, body, _) = await Api.SendAsync(h, HttpMethod.Get, url);
            Evidence.Log($"paging-{server}", $"{(int)st} GET {url} {body[..Math.Min(160, body.Length)]}");
            if ((int)st >= 500) bad.Add($"{(int)st} {url}");
        }
        foreach (var b in bad) out_.WriteLine(b);
        Assert.Empty(bad);
    }

    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task Malformed_bodies_return_400_not_500(string server)
    {
        var s = Server.ByName(server);
        var token = await Api.TokenAsync(s, "admin", Server.AdminPassword);
        using var h = Api.Raw(s, token);
        var bad = new List<string>();
        foreach (var (url, json) in new[]
                 {
                     ("api/orders", "{}"), ("api/orders", "{\"customerId\":1,\"lines\":null,\"issueVoucherNumber\":\"x\"}"),
                     ("api/orders", "{\"customerId\":1,\"lines\":[{\"itemId\":1,\"quantity\":\"abc\"}],\"issueVoucherNumber\":\"x\"}"),
                     ("api/orders/1/cancel", "{\"reason\":null}"), ("api/stock-ins", "not json"), ("api/customers", "{\"companyName\":null}"),
                     ("api/customers/import", "{\"rows\":null}"), ("api/users", "{\"username\":\"a b\",\"fullName\":\"x\",\"password\":\"12345678\"}"),
                 })
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
            using var res = await h.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            Evidence.Log($"malformed-{server}", $"{(int)res.StatusCode} POST {url} {json} -> {body[..Math.Min(200, body.Length)]}");
            if ((int)res.StatusCode >= 500) bad.Add($"{(int)res.StatusCode} {url} {json}");
        }
        Assert.Empty(bad);
    }

    // ---------------- security ----------------

    /// <summary>The signing keys are in git. A token made from them alone is accepted as admin.</summary>
    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task A_token_forged_from_the_repository_signing_key_is_refused(string server)
    {
        var s = Server.ByName(server);
        using var h = Api.Raw(s, Api.Forge(s, userId: 1));
        var (st, body, _) = await Api.SendAsync(h, HttpMethod.Get, "api/users");
        Evidence.Log("security", $"{server}: forged admin token (sub=1, key from appsettings*.json) GET api/users -> {(int)st} {body[..Math.Min(120, body.Length)]}");
        Assert.Equal(HttpStatusCode.Unauthorized, st);
    }

    /// <summary>MustChangePassword is only enforced by the desktop app; the API serves everything with the old password.</summary>
    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task User_who_must_change_password_cannot_use_the_api_before_changing_it(string server)
    {
        var s = Server.ByName(server);
        var (c, _) = await Api.UserAsync(s, Permission.StandardUser, mustChange: true);
        var ex = await Record.ExceptionAsync(() => c.GetItemsAsync());
        Evidence.Log("security", $"{server}: MustChangePassword user GET api/items -> {(ex is ApiError a ? ((int?)a.Status)?.ToString() : "200 OK")}");
        Assert.NotNull(ex);
    }

    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task Repeated_wrong_passwords_lock_or_slow_down_the_login(string server)
    {
        var s = Server.ByName(server);
        var (_, user) = await Api.UserAsync(s, Permission.ViewStock);
        using var h = Api.Raw(s);
        var codes = new List<int>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 50; i++)
            codes.Add((int)(await Api.SendAsync(h, HttpMethod.Post, "api/auth/login", new LoginRequest { Username = user.Username, Password = "wrong" + i })).Status);
        var ok = (await Api.SendAsync(h, HttpMethod.Post, "api/auth/login", new LoginRequest { Username = user.Username, Password = "Passw0rd!x" })).Status;
        Evidence.Log("security", $"{server}: 50 wrong logins in {sw.ElapsedMilliseconds} ms -> {string.Join(",", codes.Distinct())}; right password afterwards -> {(int)ok}");
        Assert.Contains(codes, c => c == 429 || c == 423 || c == 403);
    }

    /// <summary>A user without ViewStock can't open New Order / Items &amp; Categories, although the menu shows them.</summary>
    [Theory]
    [InlineData("sqlserver", Permission.CreateOrders, "New Order")]
    [InlineData("sqlserver", Permission.AddStock, "Stock In")]
    [InlineData("sqlserver", Permission.ManageCatalog, "Items & Categories")]
    [InlineData("sqlite", Permission.CreateOrders, "New Order")]
    public async Task A_page_shown_in_the_menu_can_load_its_data(string server, Permission perm, string page)
    {
        var s = Server.ByName(server);
        var (c, _) = await Api.UserAsync(s, perm);
        var ex = await Record.ExceptionAsync(() => c.GetCategoriesAsync());
        Evidence.Log("permissions", $"{server}: user with only {perm} opens '{page}' -> GET api/categories {(ex is ApiError a ? ((int?)a.Status)?.ToString() + " " + a.Message : "200")}");
        Assert.Null(ex);
    }

    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task A_user_with_no_permissions_cannot_read_customers_or_suppliers(string server)
    {
        var s = Server.ByName(server);
        var (c, _) = await Api.UserAsync(s, Permission.None);
        var customers = await Record.ExceptionAsync(() => c.GetCustomersAsync());
        var suppliers = await Record.ExceptionAsync(() => c.GetSuppliersAsync());
        Evidence.Log("permissions", $"{server}: Permission.None user: customers -> {(customers is null ? "200 (all rows)" : "refused")}, suppliers -> {(suppliers is null ? "200 (all rows)" : "refused")}");
        Assert.NotNull(customers);
        Assert.NotNull(suppliers);
    }

    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task Disabled_user_token_stops_working_immediately(string server)
    {
        var s = Server.ByName(server);
        var (c, u) = await Api.UserAsync(s, Permission.StandardUser);
        await c.GetItemsAsync();
        await (await Api.AdminAsync(s)).DisableUserAsync(u.Id);
        var e = await Assert.ThrowsAsync<ApiError>(() => c.GetItemsAsync());
        Assert.True(e.IsUnauthorized);
    }

    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task Swagger_is_reachable_without_signing_in(string server)
    {
        using var h = Api.Raw(Server.ByName(server));
        var (st, body, _) = await Api.SendAsync(h, HttpMethod.Get, "swagger/v1/swagger.json");
        Evidence.Log("security", $"{server}: anonymous GET swagger/v1/swagger.json -> {(int)st} ({body.Length} bytes)");
        // Since the fix Swagger is on only in Development: 5082 runs Development, 5081 Production.
        Assert.Equal(server == "sqlite" ? HttpStatusCode.OK : HttpStatusCode.NotFound, st);
    }

    // ---------------- client behaviour when the server is down / slow ----------------

    [Fact]
    public async Task Client_reports_a_readable_error_when_the_server_is_down()
    {
        var api = StoreApiClient.Create("127.0.0.1:5999", TimeSpan.FromSeconds(5));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var e = await Assert.ThrowsAsync<ApiError>(() => api.LoginAsync("admin", "x"));
        Evidence.Log("server-down", $"refused port: {sw.ElapsedMilliseconds} ms, IsConnectionError={e.IsConnectionError}, message='{e.Message}'");
        Assert.True(e.IsConnectionError);
    }

    [Fact]
    public async Task Client_reports_a_readable_error_when_the_host_does_not_answer()
    {
        var api = StoreApiClient.Create("10.255.255.1:5080", TimeSpan.FromSeconds(4)); // non-routable: the connect hangs
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var e = await Assert.ThrowsAsync<ApiError>(() => api.CheckHealthAsync());
        Evidence.Log("server-down", $"black-hole host: {sw.ElapsedMilliseconds} ms, IsConnectionError={e.IsConnectionError}, message='{e.Message}'");
        Assert.True(e.IsConnectionError);
    }

    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task Dates_come_back_as_utc_offsets(string server)
    {
        var s = Server.ByName(server);
        using var h = Api.Raw(s, await Api.TokenAsync(s, "admin", Server.AdminPassword));
        var (_, body, _) = await Api.SendAsync(h, HttpMethod.Get, "api/orders?pageSize=1");
        var created = JsonDocument.Parse(body).RootElement.GetProperty("items")[0].GetProperty("createdAt").GetString();
        Evidence.Log("dates", $"{server}: order createdAt wire format = {created}");
        Assert.EndsWith("+00:00", created);
    }
}
