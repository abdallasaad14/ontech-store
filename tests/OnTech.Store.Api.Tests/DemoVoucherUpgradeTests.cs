using System.Net;
using Microsoft.Data.Sqlite;
using OnTech.Store.Contracts;
using Xunit;

namespace OnTech.Store.Api.Tests;

/// <summary>A SQLite file from before voucher types is upgraded in place on startup: StockIns is rebuilt and keeps its rows.</summary>
public class DemoVoucherUpgradeTests
{
    /// <param name="handWritten">The oldest files were made by hand-written SQL with unquoted names, not by EF.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pre_voucher_types_sqlite_file_is_upgraded_and_keeps_its_data(bool handWritten)
    {
        if (FixB.OnSqlServer) return; // SQLite-only path
        using var dir = new TempDir();
        var cs = $"Data Source={Path.Combine(dir.Path, "store.db")};Pooling=False";

        // 1. A current file with a purchase and a sale, then turned back into the shape before voucher types.
        int stockInId, orderId;
        await using (var first = new SqliteFileFactory(cs))
        {
            var admin = await first.LoginAsync();
            var (_, item, customer, supplier) = await FixB.SeedAsync(admin);
            stockInId = (await (await admin.Post("/api/stock-ins", new CreateStockInRequest
            {
                SupplierId = supplier.Id, VoucherNumber = "OLD-IN-1", Lines = { new StockInLineRequest { ItemId = item.Id, Quantity = 3 } },
            })).Read<StockInDto>()).Id;
            orderId = (await (await admin.Post("/api/orders", new CreateOrderRequest
            {
                IssueVoucherNumber = "OLD-OUT-1", CustomerId = customer.Id, Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 2 } },
            })).Read<OrderDto>()).Id;
        }
        OldShape.Drop(cs, "Orders", "Type");
        OldShape.Drop(cs, "OrderLines", "ReturnedQuantity");
        OldShape.Drop(cs, "StockIns", "Type", "CustomerId", "CK_StockIns_Party");
        OldShape.Drop(cs, "StockInLines", "DemoOrderLineId");
        if (handWritten) OldShape.Drop(cs, "StockIns", unquote: true);
        Assert.Equal(0L, SqliteUpgrade.Scalar(cs, "SELECT COUNT(*) FROM pragma_table_info('StockIns') WHERE name = 'CustomerId'"));
        Assert.Equal(1L, SqliteUpgrade.Scalar(cs, "SELECT \"notnull\" FROM pragma_table_info('StockIns') WHERE name = 'SupplierId'"));

        // 2. Start on it: the upgrade runs, the rows are still there and demos work.
        await using (var second = new SqliteFileFactory(cs))
        {
            var admin = await second.LoginAsync();
            Assert.Equal(0L, SqliteUpgrade.Scalar(cs, "SELECT \"notnull\" FROM pragma_table_info('StockIns') WHERE name = 'SupplierId'"));
            Assert.Equal(0L, SqliteUpgrade.Scalar(cs, "SELECT COUNT(*) FROM pragma_foreign_key_check"));
            Assert.Equal(1L, SqliteUpgrade.Scalar(cs, "SELECT COUNT(*) FROM pragma_index_list('StockIns') WHERE name = 'IX_StockIns_VoucherNumber'"));
            Assert.Equal("NOCASE", SqliteUpgrade.Collation(cs, "IX_StockIns_VoucherNumber"));
            Assert.Single(Directory.GetFiles(dir.Path, "store-backup-before-voucher-types-*.db"));

            var stockIn = await admin.GetAs<StockInDto>($"/api/stock-ins/{stockInId}");
            Assert.Equal(ReceiveType.Purchase, stockIn.Type);
            Assert.Equal("OLD-IN-1", stockIn.VoucherNumber);
            var sale = await admin.GetAs<OrderDto>($"/api/orders/{orderId}");
            Assert.Equal(IssueType.Sale, sale.Type);
            Assert.Equal(0, sale.ReturnedQuantity);

            // The voucher number is still unique, whatever the case.
            var supplier = (await admin.GetAs<IReadOnlyList<SupplierDto>>("/api/suppliers"))[0];
            var item = (await admin.GetAs<IReadOnlyList<ItemDto>>("/api/items"))[0];
            var customer = (await admin.GetAs<IReadOnlyList<CustomerDto>>("/api/customers"))[0];
            Assert.Equal(HttpStatusCode.Conflict, (await admin.Post("/api/stock-ins", new CreateStockInRequest
            {
                SupplierId = supplier.Id, VoucherNumber = "old-in-1", Lines = { new StockInLineRequest { ItemId = item.Id, Quantity = 1 } },
            })).StatusCode);

            var demo = await (await admin.Post("/api/orders", new CreateOrderRequest
            {
                Type = IssueType.Demo, IssueVoucherNumber = "DEMO-1", CustomerId = customer.Id, Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = 2 } },
            })).Read<OrderDto>();
            var back = await (await admin.Post("/api/stock-ins", new CreateStockInRequest
            {
                Type = ReceiveType.DemoReturn, CustomerId = customer.Id, VoucherNumber = "RET-1",
                Lines = { new StockInLineRequest { ItemId = item.Id, Quantity = 2, DemoOrderLineId = demo.Lines[0].Id } },
            })).Read<StockInDto>();
            Assert.Equal(customer.Id, back.CustomerId);
            // The database itself refuses returning more than went out.
            Assert.Throws<SqliteException>(() => SqliteUpgrade.Exec(cs, $"UPDATE OrderLines SET ReturnedQuantity = 3 WHERE Id = {demo.Lines[0].Id}"));
        }

        // 3. A second start changes nothing (no new backup).
        await using (var third = new SqliteFileFactory(cs)) { await third.LoginAsync(); }
        Assert.Single(Directory.GetFiles(dir.Path, "store-backup-before-voucher-types-*.db"));
    }
}

/// <summary>Takes columns (and the constraints and indexes that name them) back out of a SQLite table by rebuilding it.</summary>
internal static class OldShape
{
    public static void Drop(string cs, string table, params string[] names) => Drop(cs, table, false, names);

    public static void Drop(string cs, string table, bool unquote, params string[] names)
    {
        using var c = new SqliteConnection(cs);
        c.Open();
        Run(c, "PRAGMA foreign_keys = OFF");
        var create = (string)Scalar(c, $"SELECT sql FROM sqlite_master WHERE type = 'table' AND name = '{table}'")!;
        var indexes = Rows(c, $"SELECT sql FROM sqlite_master WHERE type = 'index' AND tbl_name = '{table}' AND sql IS NOT NULL")
            .Where(s => !names.Any(n => s.Contains(n))).ToList();
        var columns = Rows(c, $"SELECT name FROM pragma_table_info('{table}') ORDER BY cid").Where(n => !names.Contains(n)).Select(n => $"\"{n}\"");
        var list = string.Join(", ", columns);

        var open = create.IndexOf('(');
        var parts = SplitTopLevel(create[(open + 1)..create.LastIndexOf(')')])
            .Where(p => !names.Any(n => p.Contains($"\"{n}\"") || p.Contains($"[{n}]")))
            .Select(p => table == "StockIns" ? p.Replace("\"SupplierId\" INTEGER NULL", "\"SupplierId\" INTEGER NOT NULL") : p);
        var oldCreate = $"CREATE TABLE \"{table}_old\" ({string.Join(",", parts)})";
        if (unquote) oldCreate = $"CREATE TABLE {table}_old ({string.Join(",", parts).Replace("\"", "")})";

        using var tx = c.BeginTransaction();
        foreach (var sql in new[] { oldCreate, $"INSERT INTO \"{table}_old\" ({list}) SELECT {list} FROM \"{table}\"", $"DROP TABLE \"{table}\"", $"ALTER TABLE \"{table}_old\" RENAME TO \"{table}\"" }.Concat(indexes))
            Run(c, sql, tx);
        tx.Commit();
    }

    private static IEnumerable<string> SplitTopLevel(string body)
    {
        int depth = 0, start = 0;
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '(') depth++;
            else if (body[i] == ')') depth--;
            else if (body[i] == ',' && depth == 0) { yield return body[start..i]; start = i + 1; }
        }
        yield return body[start..];
    }

    private static void Run(SqliteConnection c, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private static List<string> Rows(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }
}
