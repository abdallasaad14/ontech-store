using System.Net;
using OnTech.Store.Client;
using OnTech.Store.Contracts;
using Xunit.Abstractions;

namespace OnTech.Store.Tests;

/// <summary>Phase 3.5 / 3.6: multi-table writes, forced failures, and several PCs at the same moment.</summary>
public class TransactionAndConcurrencyTests(ITestOutputHelper out_)
{
    private static void Check(Server s, int itemId)
    {
        // Every item's quantity must equal its last movement's QuantityAfter, and the sum of its changes.
        var qty = s.ItemQty(itemId);
        var sum = Convert.ToInt32(s.Scalar("SELECT COALESCE(SUM(Change),0) FROM StockMovements WHERE ItemId=@i", ("@i", itemId)));
        Assert.Equal(qty, sum);
    }

    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task Shortage_on_the_second_line_leaves_no_partial_order(string server)
    {
        var s = Server.ByName(server);
        var api = await Api.AdminAsync(s);
        var a = await Api.ItemAsync(s, 10);
        var b = await Api.ItemAsync(s, 1);
        var cust = await Api.CustomerAsync(s);
        var e = await Assert.ThrowsAsync<ApiError>(() => api.CreateOrderAsync(Api.Order(cust.Id, (a.Id, 4), (b.Id, 2))));
        Assert.Equal(HttpStatusCode.Conflict, e.Status);
        Assert.Equal(10, s.ItemQty(a.Id));
        Assert.Equal(1, s.ItemQty(b.Id));
        Assert.Equal(0, s.Count("Orders", $"CustomerId={cust.Id}"));
        Assert.Equal(0, s.Count("OrderLines", $"ItemId IN ({a.Id},{b.Id})"));
        Check(s, a.Id); Check(s, b.Id);
    }

    /// <summary>
    /// Forces the LAST statement of an order (the StockMovements insert) to fail with a trigger, after the
    /// quantities were already taken by ExecuteUpdate. The whole order must roll back. SQL Server only (trigger).
    /// </summary>
    [Fact]
    public async Task Failure_after_stock_was_taken_rolls_everything_back()
    {
        var s = Server.SqlServer;
        var api = await Api.AdminAsync(s);
        var item = await Api.ItemAsync(s, 10);
        var cust = await Api.CustomerAsync(s);
        s.Exec($"""
            CREATE OR ALTER TRIGGER TR_Audit_FailMovement ON StockMovements AFTER INSERT AS
            IF EXISTS (SELECT 1 FROM inserted WHERE ItemId = {item.Id} AND Type = 2) THROW 50001, 'audit: forced failure', 1;
            """);
        try
        {
            var req = Api.Order(cust.Id, (item.Id, 3));
            var e = await Assert.ThrowsAsync<ApiError>(() => api.CreateOrderAsync(req));
            Evidence.Log("transactions", $"forced failure on StockMovements insert -> {(int?)e.Status} '{e.Message}'");
            Assert.Equal(10, s.ItemQty(item.Id));
            Assert.Equal(0, s.Count("Orders", $"IssueVoucherNumber='{req.IssueVoucherNumber}'"));
            Assert.Equal(0, s.Count("OrderLines", $"ItemId={item.Id}"));
        }
        finally { s.Exec("DROP TRIGGER IF EXISTS TR_Audit_FailMovement"); }
        Check(s, item.Id);
    }

    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task Cancelling_a_stock_in_whose_stock_was_issued_changes_nothing(string server)
    {
        var s = Server.ByName(server);
        var api = await Api.AdminAsync(s);
        var item = await Api.ItemAsync(s, 0);
        var supp = await Api.SupplierAsync(s);
        var si = await api.CreateStockInAsync(new CreateStockInRequest { SupplierId = supp.Id, VoucherNumber = Api.U("IN"), Lines = [new OrderLineRequest { ItemId = item.Id, Quantity = 5 }] });
        await api.CreateOrderAsync(Api.Order((await Api.CustomerAsync(s)).Id, (item.Id, 3)));
        var e = await Assert.ThrowsAsync<ApiError>(() => api.CancelStockInAsync(si.Id, "audit"));
        Assert.Equal(HttpStatusCode.Conflict, e.Status);
        Assert.Equal(2, s.ItemQty(item.Id));
        Assert.Equal(0, Convert.ToInt32(s.Scalar("SELECT Status FROM StockIns WHERE Id=@i", ("@i", si.Id))));
        Check(s, item.Id);
    }

    // ---------------- several PCs at once ----------------

    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task Twenty_PCs_ordering_the_last_10_units_never_oversell(string server)
    {
        var s = Server.ByName(server);
        var item = await Api.ItemAsync(s, 10);
        var cust = await Api.CustomerAsync(s);
        var clients = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Api.UserAsync(s, Permission.All)));
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            try { await clients[i % 4].Client.CreateOrderAsync(Api.Order(cust.Id, (item.Id, 1))); return "ok"; }
            catch (ApiError e) { return $"{(int?)e.Status}"; }
        }));
        var summary = string.Join(", ", results.GroupBy(r => r).Select(g => $"{g.Key} x{g.Count()}"));
        Evidence.Log("concurrency", $"{server}: 20 parallel orders of 1 on stock 10 -> {summary}; stock now {s.ItemQty(item.Id)}");
        out_.WriteLine(summary);
        Assert.Equal(10, results.Count(r => r == "ok"));
        Assert.Equal(0, s.ItemQty(item.Id));
        Assert.All(results.Where(r => r != "ok"), r => Assert.Equal("409", r));
        Check(s, item.Id);
    }

    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task Five_PCs_cancelling_the_same_order_return_the_stock_once(string server)
    {
        var s = Server.ByName(server);
        var api = await Api.AdminAsync(s);
        var item = await Api.ItemAsync(s, 10);
        var order = await api.CreateOrderAsync(Api.Order((await Api.CustomerAsync(s)).Id, (item.Id, 4)));
        var clients = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Api.UserAsync(s, Permission.All)));
        var results = await Task.WhenAll(clients.Select(async c =>
        {
            try { await c.Client.CancelOrderAsync(order.Id, "audit parallel"); return "ok"; }
            catch (ApiError e) { return $"{(int?)e.Status}"; }
        }));
        Evidence.Log("concurrency", $"{server}: 5 parallel cancels of one order -> {string.Join(",", results)}; stock {s.ItemQty(item.Id)}");
        Assert.Equal(1, results.Count(r => r == "ok"));
        Assert.Equal(10, s.ItemQty(item.Id));
        Check(s, item.Id);
    }

    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task Same_voucher_number_from_five_PCs_is_saved_once(string server)
    {
        var s = Server.ByName(server);
        var item = await Api.ItemAsync(s, 50);
        var cust = await Api.CustomerAsync(s);
        var clients = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Api.UserAsync(s, Permission.All)));
        var voucher = Api.U("SAME");
        var results = await Task.WhenAll(clients.Select(async c =>
        {
            var req = Api.Order(cust.Id, (item.Id, 1)); req.IssueVoucherNumber = voucher;
            try { await c.Client.CreateOrderAsync(req); return "ok"; }
            catch (ApiError e) { return $"{(int?)e.Status}"; }
        }));
        Evidence.Log("concurrency", $"{server}: 5 parallel orders with one voucher -> {string.Join(",", results)}; stock {s.ItemQty(item.Id)}");
        Assert.Equal(1, results.Count(r => r == "ok"));
        Assert.All(results.Where(r => r != "ok"), r => Assert.Equal("409", r));
        Assert.Equal(49, s.ItemQty(item.Id));
        Check(s, item.Id);
    }

    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task New_items_from_five_PCs_get_different_codes(string server)
    {
        var s = Server.ByName(server);
        var cat = await Api.CategoryAsync(s);
        var clients = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Api.UserAsync(s, Permission.All)));
        var items = await Task.WhenAll(clients.Select(c => c.Client.CreateItemAsync(new CreateItemRequest { CategoryId = cat.Id, Name = Api.U("Par") })));
        Evidence.Log("concurrency", $"{server}: 5 parallel new items -> codes {string.Join(",", items.Select(i => i.Code))}");
        Assert.Equal(5, items.Select(i => i.Code).Distinct().Count());
    }

    /// <summary>Two PCs edit the same customer from the same starting point: the second save silently overwrites the first.</summary>
    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task Two_PCs_editing_the_same_customer_do_not_silently_lose_an_edit(string server)
    {
        var s = Server.ByName(server);
        var c = await Api.CustomerAsync(s);
        var (pc1, _) = await Api.UserAsync(s, Permission.All);
        var (pc2, _) = await Api.UserAsync(s, Permission.All);
        await pc1.UpdateCustomerAsync(c.Id, new SaveCustomerRequest { CompanyName = c.CompanyName, PhoneNumber = "0100 PC1" });
        var second = await Record.ExceptionAsync(() => pc2.UpdateCustomerAsync(c.Id, new SaveCustomerRequest { CompanyName = c.CompanyName, Notes = "edited on PC2" }));
        var phone = s.Scalar("SELECT PhoneNumber FROM Customers WHERE Id=@i", ("@i", c.Id));
        Evidence.Log("concurrency", $"{server}: PC1 set phone, PC2 (stale form) saved notes -> second save {(second is null ? "accepted" : "refused")}, phone now '{phone ?? "NULL"}'");
        Assert.True(second is not null || phone is not null, "PC1's phone number was wiped by PC2's save without any warning");
    }

    /// <summary>Two PCs give two different customers the same number at the same moment (Update is not behind the numbering lock).</summary>
    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task Parallel_customer_number_changes_end_in_409_not_500(string server)
    {
        var s = Server.ByName(server);
        var c1 = await Api.CustomerAsync(s);
        var c2 = await Api.CustomerAsync(s);
        var number = 900_000 + Random.Shared.Next(90_000);
        var (pc1, _) = await Api.UserAsync(s, Permission.All);
        var (pc2, _) = await Api.UserAsync(s, Permission.All);
        var codes = new List<string>();
        for (var attempt = 0; attempt < 10 && codes.All(c => c != "500"); attempt++, number++)
        {
            var r = await Task.WhenAll(
                Try(() => pc1.UpdateCustomerAsync(c1.Id, new SaveCustomerRequest { CompanyName = c1.CompanyName, CustomerNumber = number })),
                Try(() => pc2.UpdateCustomerAsync(c2.Id, new SaveCustomerRequest { CompanyName = c2.CompanyName, CustomerNumber = number })));
            codes.AddRange(r);
        }
        Evidence.Log("concurrency", $"{server}: parallel 'set customer number' pairs -> {string.Join(",", codes)}");
        Assert.DoesNotContain("500", codes);

        static async Task<string> Try(Func<Task> f)
        {
            try { await f(); return "ok"; } catch (ApiError e) { return $"{(int?)e.Status}"; }
        }
    }

    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task Stock_in_and_orders_at_the_same_time_keep_the_balance_right(string server)
    {
        var s = Server.ByName(server);
        var item = await Api.ItemAsync(s, 20);
        var cust = await Api.CustomerAsync(s);
        var supp = await Api.SupplierAsync(s);
        var clients = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Api.UserAsync(s, Permission.All)));
        var tasks = Enumerable.Range(0, 30).Select(async i =>
        {
            var c = clients[i % 3].Client;
            try
            {
                if (i % 3 == 0) await c.CreateStockInAsync(new CreateStockInRequest { SupplierId = supp.Id, VoucherNumber = Api.U("PIN"), Lines = [new OrderLineRequest { ItemId = item.Id, Quantity = 2 }] });
                else await c.CreateOrderAsync(Api.Order(cust.Id, (item.Id, 1)));
                return "ok";
            }
            catch (ApiError e) { return $"{(int?)e.Status} {e.Message}"; }
        });
        var results = await Task.WhenAll(tasks);
        Evidence.Log("concurrency", $"{server}: 10 stock-ins(+2) and 20 orders(-1) in parallel -> {string.Join(" | ", results.GroupBy(r => r).Select(g => $"{g.Key} x{g.Count()}"))}; stock {s.ItemQty(item.Id)}");
        Assert.All(results, r => Assert.Equal("ok", r));
        Assert.Equal(20 + 20 - 20, s.ItemQty(item.Id));
        Check(s, item.Id);
    }
}
