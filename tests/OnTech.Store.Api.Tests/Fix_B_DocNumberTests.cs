using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OnTech.Store.Contracts;
using Xunit;

namespace OnTech.Store.Api.Tests;

/// <summary>M-4: document numbers are trimmed and upper-cased on save, and unique without letter case on both databases.</summary>
public class Fix_B_DocNumberTests : IClassFixture<StoreApiFactory>
{
    private readonly StoreApiFactory _f;
    public Fix_B_DocNumberTests(StoreApiFactory f) => _f = f;

    [Fact]
    public async Task Issue_voucher_and_receipt_are_unique_without_letter_case_and_stored_upper_case()
    {
        var admin = await _f.LoginAsync();
        var (_, item, customer, _) = await FixB.SeedAsync(admin);
        var tag = FixB.Tag();
        var order = await (await admin.Post("/api/orders", new CreateOrderRequest
        {
            CustomerId = customer.Id, IssueVoucherNumber = $"  ab-{tag} ", WithReceipt = true, ReceiptNumber = $" rc-{tag}",
            Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 1 } },
        })).Read<OrderDto>();
        Assert.Equal($"AB-{tag}".ToUpperInvariant(), order.IssueVoucherNumber);
        Assert.Equal(order.IssueVoucherNumber, order.OrderNumber);
        Assert.Equal($"RC-{tag}".ToUpperInvariant(), order.ReceiptNumber);

        var sameVoucher = await admin.Post("/api/orders", new CreateOrderRequest
        {
            CustomerId = customer.Id, IssueVoucherNumber = $"AB-{tag}".ToUpperInvariant(), Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 1 } },
        });
        Assert.Equal(HttpStatusCode.Conflict, sameVoucher.StatusCode);
        Assert.Equal(ErrorCodes.Duplicate, (await sameVoucher.ProblemAsync()).Code);

        var sameReceipt = await admin.Post("/api/orders", new CreateOrderRequest
        {
            CustomerId = customer.Id, IssueVoucherNumber = TestStock.Voucher(), WithReceipt = true, ReceiptNumber = $"Rc-{tag}",
            Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 1 } },
        });
        Assert.Equal(HttpStatusCode.Conflict, sameReceipt.StatusCode);
        Assert.Equal(ErrorCodes.Duplicate, (await sameReceipt.ProblemAsync()).Code);

        // Lookups ignore letter case too.
        Assert.Equal(order.Id, (await admin.GetAs<OrderDto>($"/api/orders/by-receipt/rc-{tag}")).Id);
        var found = await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?issueVoucherNumber=ab-{tag}");
        Assert.Equal(order.Id, Assert.Single(found.Items).Id);
        found = await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?receiptNumber=rc-{tag}");
        Assert.Equal(order.Id, Assert.Single(found.Items).Id);
        found = await admin.GetAs<PagedResult<OrderDto>>($"/api/orders?search=ab-{tag}");
        Assert.Equal(order.Id, Assert.Single(found.Items).Id);
    }

    [Fact]
    public async Task Stock_in_voucher_is_unique_without_letter_case_and_invoice_is_upper_case()
    {
        var admin = await _f.LoginAsync();
        var (_, item, _, supplier) = await FixB.SeedAsync(admin);
        var tag = FixB.Tag();
        var stockIn = await (await admin.Post("/api/stock-ins", new CreateStockInRequest
        {
            SupplierId = supplier.Id, VoucherNumber = $"in-{tag}", SupplierInvoiceNumber = $" inv-{tag} ",
            Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 1 } },
        })).Read<StockInDto>();
        Assert.Equal($"IN-{tag}".ToUpperInvariant(), stockIn.VoucherNumber);
        Assert.Equal($"INV-{tag}".ToUpperInvariant(), stockIn.SupplierInvoiceNumber);

        var again = await admin.Post("/api/stock-ins", new CreateStockInRequest
        {
            SupplierId = supplier.Id, VoucherNumber = $"In-{tag}", Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 1 } },
        });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var p = await again.ProblemAsync();
        Assert.Equal(ErrorCodes.Duplicate, p.Code);
        Assert.Equal($"Voucher number '{("IN-" + tag).ToUpperInvariant()}' is already used by another stock-in.", p.Title);

        var found = await admin.GetAs<PagedResult<StockInDto>>($"/api/stock-ins?search=in-{tag}");
        Assert.Contains(found.Items, s => s.Id == stockIn.Id);
    }

    [Fact]
    public async Task Parallel_vouchers_differing_only_in_case_save_once()
    {
        var admin = await _f.LoginAsync();
        var (_, item, customer, _) = await FixB.SeedAsync(admin);
        var tag = FixB.Tag();
        var spellings = new[] { $"x-{tag}", $"X-{tag}", $"x-{tag} ", $" X-{tag}" };
        var clients = await Task.WhenAll(spellings.Select(_ => _f.LoginAsync()));
        var results = await Task.WhenAll(spellings.Select((v, i) => clients[i].Post("/api/orders", new CreateOrderRequest
        {
            CustomerId = customer.Id, IssueVoucherNumber = v, Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 1 } },
        })));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public void New_sqlite_files_have_case_insensitive_voucher_indexes()
    {
        if (FixB.OnSqlServer) return; // SQL Server is CI_AS; see Fix_B_MigrationTests
        foreach (var index in new[] { "IX_Orders_IssueVoucherNumber", "IX_Orders_ReceiptNumber", "IX_StockIns_VoucherNumber" })
            Assert.Equal("NOCASE", _f.WithDb(db => SqliteUpgrade.Collation(db.Database.GetDbConnection().ConnectionString, index)));
    }
}

/// <summary>An existing (pre-fix) SQLite file is upgraded in place on startup, with a backup first.</summary>
public class Fix_B_SqliteUpgradeTests
{
    [Fact]
    public async Task Pre_fix_sqlite_file_gets_versions_nocase_indexes_and_upper_case_vouchers()
    {
        if (FixB.OnSqlServer) return; // SQLite-only path
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "store.db");
        var cs = $"Data Source={file};Pooling=False";

        // 1. A current file with a stock-in, then turned back into the pre-fix shape.
        string voucher;
        await using (var first = new SqliteFileFactory(cs))
        {
            var admin = await first.LoginAsync();
            var (_, item, _, supplier) = await FixB.SeedAsync(admin);
            voucher = (await (await admin.Post("/api/stock-ins", new CreateStockInRequest
            {
                SupplierId = supplier.Id, VoucherNumber = "OLD-1", Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 1 } },
            })).Read<StockInDto>()).VoucherNumber;
        }
        SqliteUpgrade.Exec(cs,
            "ALTER TABLE Users DROP COLUMN Version", "ALTER TABLE Items DROP COLUMN Version",
            "ALTER TABLE Customers DROP COLUMN Version", "ALTER TABLE Suppliers DROP COLUMN Version",
            "DROP INDEX IX_StockIns_VoucherNumber", "CREATE UNIQUE INDEX IX_StockIns_VoucherNumber ON StockIns (VoucherNumber COLLATE BINARY)",
            "DROP INDEX IX_Orders_IssueVoucherNumber", "CREATE UNIQUE INDEX IX_Orders_IssueVoucherNumber ON Orders (IssueVoucherNumber COLLATE BINARY) WHERE [IssueVoucherNumber] IS NOT NULL",
            "UPDATE StockIns SET VoucherNumber = 'old-1' WHERE VoucherNumber = 'OLD-1' COLLATE BINARY");
        Assert.Equal("BINARY", SqliteUpgrade.Collation(cs, "IX_StockIns_VoucherNumber"));

        // 2. Start on it: the upgrade runs.
        await using (var second = new SqliteFileFactory(cs))
        {
            var admin = await second.LoginAsync();
            foreach (var t in new[] { "Users", "Items", "Customers", "Suppliers" })
                Assert.Equal(1L, SqliteUpgrade.Scalar(cs, $"SELECT COUNT(*) FROM pragma_table_info('{t}') WHERE name = 'Version'"));
            Assert.Equal("NOCASE", SqliteUpgrade.Collation(cs, "IX_StockIns_VoucherNumber"));
            Assert.Equal("NOCASE", SqliteUpgrade.Collation(cs, "IX_Orders_IssueVoucherNumber"));
            Assert.Equal(1L, SqliteUpgrade.Scalar(cs, "SELECT COUNT(*) FROM StockIns WHERE VoucherNumber = 'OLD-1' COLLATE BINARY"));
            Assert.Single(Directory.GetFiles(dir.Path, "store-backup-before-audit-fixes-*.db"));

            var supplier = (await admin.GetAs<IReadOnlyList<SupplierDto>>("/api/suppliers"))[0];
            var item = (await admin.GetAs<IReadOnlyList<ItemDto>>("/api/items"))[0];
            var dup = await admin.Post("/api/stock-ins", new CreateStockInRequest
            {
                SupplierId = supplier.Id, VoucherNumber = "old-1", Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 1 } },
            });
            Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        }

        // 3. A second start changes nothing (no new backup).
        await using (var third = new SqliteFileFactory(cs)) { await third.LoginAsync(); }
        Assert.Single(Directory.GetFiles(dir.Path, "store-backup-before-audit-fixes-*.db"));
    }

    [Fact]
    public async Task Vouchers_that_differ_only_in_case_are_left_alone_and_the_server_still_starts()
    {
        if (FixB.OnSqlServer) return;
        using var dir = new TempDir();
        var cs = $"Data Source={Path.Combine(dir.Path, "store.db")};Pooling=False";
        await using (var first = new SqliteFileFactory(cs))
        {
            var admin = await first.LoginAsync();
            var (_, item, _, supplier) = await FixB.SeedAsync(admin);
            foreach (var v in new[] { "DUP-1", "DUP-2" })
                await (await admin.Post("/api/stock-ins", new CreateStockInRequest
                {
                    SupplierId = supplier.Id, VoucherNumber = v, Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 1 } },
                })).Read<StockInDto>();
        }
        SqliteUpgrade.Exec(cs,
            "DROP INDEX IX_StockIns_VoucherNumber", "CREATE UNIQUE INDEX IX_StockIns_VoucherNumber ON StockIns (VoucherNumber COLLATE BINARY)",
            "UPDATE StockIns SET VoucherNumber = 'dup-1' WHERE VoucherNumber = 'DUP-2'");

        await using (var second = new SqliteFileFactory(cs))
        {
            await second.LoginAsync();
            Assert.Equal("BINARY", SqliteUpgrade.Collation(cs, "IX_StockIns_VoucherNumber"));
            Assert.Equal(1L, SqliteUpgrade.Scalar(cs, "SELECT COUNT(*) FROM StockIns WHERE VoucherNumber = 'dup-1' COLLATE BINARY"));
            Assert.Equal(1L, SqliteUpgrade.Scalar(cs, "SELECT COUNT(*) FROM StockIns WHERE VoucherNumber = 'DUP-1' COLLATE BINARY"));
        }
    }
}

internal static class SqliteUpgrade
{
    public static void Exec(string cs, params string[] sql)
    {
        using var c = new SqliteConnection(cs);
        c.Open();
        foreach (var s in sql) { using var cmd = c.CreateCommand(); cmd.CommandText = s; cmd.ExecuteNonQuery(); }
    }

    public static object? Scalar(string cs, string sql)
    {
        using var c = new SqliteConnection(cs);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    public static string? Collation(string cs, string index) =>
        Scalar(cs, $"SELECT coll FROM pragma_index_xinfo('{index}') WHERE cid >= 0") as string;
}

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ontech-b-{Guid.NewGuid():N}");
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path, true); } catch { /* best effort */ }
    }
}

/// <summary>The real API on a given SQLite file (StoreApiFactory always makes a new one).</summary>
internal sealed class SqliteFileFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Store", connectionString);
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-0123456789abcdefghijklmnop");
        builder.UseSetting("Seed:AdminUsername", StoreApiFactory.AdminUsername);
        builder.UseSetting("Seed:AdminPassword", StoreApiFactory.AdminPassword);
    }

    public async Task<HttpClient> LoginAsync()
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Username = StoreApiFactory.AdminUsername, Password = StoreApiFactory.AdminPassword }, StoreApiFactory.Json);
        res.EnsureSuccessStatusCode();
        var login = (await res.Content.ReadFromJsonAsync<LoginResponse>(StoreApiFactory.Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        return client;
    }
}
