using System.Diagnostics;
using Xunit.Abstractions;

namespace OnTech.Store.Tests;

/// <summary>Phase 6 "large data": 50,000 customers and 50,000 orders (one item with 50,001 movements), then time the screens' API calls.</summary>
public class PerformanceTests(ITestOutputHelper out_)
{
    public const int N = 50_000;

    private static readonly SemaphoreSlim SeedGate = new(1, 1);

    public static async Task SeedAsync(Server s)
    {
        await SeedGate.WaitAsync();
        try
        {
            if (s.Count("Orders", "IssueVoucherNumber = 'PERF-1'") > 0) return;
            await Api.AdminAsync(s); // makes sure the admin can sign in (and fixes the SQLite copy's password)
            var sw = Stopwatch.StartNew();
            if (s.SqlServerDb is not null)
            {
                s.Exec($"""
                    SET NOCOUNT ON;
                    DECLARE @u int = (SELECT MIN(Id) FROM Users WHERE IsAdmin = 1);
                    DECLARE @cat int = (SELECT MIN(Id) FROM Categories);
                    INSERT Items (CategoryId, Name, Code, Quantity, LowStockThreshold, IsActive, CreatedAt, UpdatedAt)
                    VALUES (@cat, N'PERF صنف الأداء', '99999999', 0, 0, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
                    DECLARE @item int = SCOPE_IDENTITY();
                    SELECT TOP ({N}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i INTO #n FROM sys.all_objects a CROSS JOIN sys.all_objects b;
                    INSERT Customers (CustomerNumber, CompanyName, PhoneNumber, TaxNumber, IsActive, CreatedAt, CreatedByUserId)
                    SELECT 2000000 + i, N'PERF عميل رقم ' + CAST(i AS nvarchar(10)), '010' + CAST(i AS varchar(10)), 'T' + CAST(i AS varchar(10)), 1,
                           DATEADD(minute, -i, SYSUTCDATETIME()), @u FROM #n;
                    DECLARE @c0 int = (SELECT MIN(Id) FROM Customers WHERE CustomerNumber > 2000000);
                    INSERT Orders (OrderNumber, IssueVoucherNumber, CustomerId, WithReceipt, ReceiptNumber, Status, CreatedByUserId, CreatedAt)
                    SELECT 'PERF-' + CAST(i AS varchar(10)), 'PERF-' + CAST(i AS varchar(10)), @c0 + (i % 5000), CASE WHEN i % 2 = 0 THEN 1 ELSE 0 END,
                           CASE WHEN i % 2 = 0 THEN 'R-PERF-' + CAST(i AS varchar(10)) END, 0, @u, DATEADD(minute, -({N} - i), SYSUTCDATETIME()) FROM #n;
                    INSERT OrderLines (OrderId, ItemId, Quantity, ItemName, ItemCode, CategoryId, CategoryName)
                    SELECT o.Id, @item, 1, N'PERF صنف الأداء', '99999999', @cat, N'perf' FROM Orders o WHERE o.IssueVoucherNumber LIKE 'PERF-%';
                    INSERT StockMovements (ItemId, Type, Change, QuantityAfter, UserId, CreatedAt)
                    VALUES (@item, 0, {N}, {N}, @u, DATEADD(day, -60, SYSUTCDATETIME()));
                    INSERT StockMovements (ItemId, Type, Change, QuantityAfter, OrderId, UserId, CreatedAt)
                    SELECT @item, 2, -1, {N} - CAST(SUBSTRING(o.IssueVoucherNumber, 6, 10) AS int), o.Id, @u, o.CreatedAt FROM Orders o WHERE o.IssueVoucherNumber LIKE 'PERF-%';
                    """);
            }
            else
            {
                s.Exec($"""
                    INSERT INTO Items (CategoryId, Name, Code, Quantity, LowStockThreshold, IsActive, CreatedAt, UpdatedAt)
                    VALUES ((SELECT MIN(Id) FROM Categories), 'PERF صنف الأداء', '99999999', 0, 0, 1, datetime('now'), datetime('now'));
                    CREATE TEMP TABLE n AS WITH RECURSIVE r(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM r WHERE i < {N}) SELECT i FROM r;
                    INSERT INTO Customers (CustomerNumber, CompanyName, PhoneNumber, TaxNumber, IsActive, CreatedAt, CreatedByUserId)
                    SELECT 2000000 + i, 'PERF عميل رقم ' || i, '010' || i, 'T' || i, 1, datetime('now', '-' || i || ' minutes'), 1 FROM n;
                    INSERT INTO Orders (OrderNumber, IssueVoucherNumber, CustomerId, WithReceipt, ReceiptNumber, Status, CreatedByUserId, CreatedAt)
                    SELECT 'PERF-' || i, 'PERF-' || i, (SELECT MIN(Id) FROM Customers WHERE CustomerNumber > 2000000) + (i % 5000), i % 2 = 0,
                           CASE WHEN i % 2 = 0 THEN 'R-PERF-' || i END, 0, 1, strftime('%Y-%m-%d %H:%M:%f', 'now', '-' || ({N} - i) || ' minutes') FROM n;
                    INSERT INTO OrderLines (OrderId, ItemId, Quantity, ItemName, ItemCode, CategoryId, CategoryName)
                    SELECT o.Id, (SELECT Id FROM Items WHERE Code = '99999999'), 1, 'PERF صنف الأداء', '99999999', (SELECT MIN(Id) FROM Categories), 'perf'
                    FROM Orders o WHERE o.IssueVoucherNumber LIKE 'PERF-%';
                    INSERT INTO StockMovements (ItemId, Type, Change, QuantityAfter, UserId, CreatedAt)
                    VALUES ((SELECT Id FROM Items WHERE Code = '99999999'), 0, {N}, {N}, 1, datetime('now', '-60 days'));
                    INSERT INTO StockMovements (ItemId, Type, Change, QuantityAfter, OrderId, UserId, CreatedAt)
                    SELECT (SELECT Id FROM Items WHERE Code = '99999999'), 2, -1, {N} - CAST(substr(o.IssueVoucherNumber, 6) AS INTEGER), o.Id, 1, o.CreatedAt
                    FROM Orders o WHERE o.IssueVoucherNumber LIKE 'PERF-%';
                    """);
            }
            Evidence.Log("performance", $"{s.Name}: seeded {N} customers + {N} orders + {N + 1} movements in {sw.Elapsed.TotalSeconds:F1} s");
        }
        finally { SeedGate.Release(); }
    }

    [Theory, MemberData(nameof(Server.Both), MemberType = typeof(Server))]
    public async Task Screens_stay_fast_with_50k_customers_and_orders(string server)
    {
        var s = Server.ByName(server);
        await SeedAsync(s);
        var perfItem = Convert.ToInt32(s.Scalar("SELECT Id FROM Items WHERE Code = '99999999'"));
        using var h = Api.Raw(s, await Api.TokenAsync(s, "admin", Server.AdminPassword));
        var urls = new (string Screen, string Url)[]
        {
            ("Home", "api/dashboard/summary?utcOffsetMinutes=120"),
            ("New Order: all customers (loaded on open)", "api/customers"),
            ("Customers page search", "api/customers?search=" + Uri.EscapeDataString("عميل رقم 4999")),
            ("Orders log page 1", "api/orders?page=1&pageSize=50"),
            ("Orders log last page", $"api/orders?page={N / 50}&pageSize=50"),
            ("Orders log text search", "api/orders?search=" + Uri.EscapeDataString("رقم 123")),
            ("Orders log sort by total qty", "api/orders?sort=TotalQuantity&pageSize=50"),
            ("Items list", "api/items"),
            ("Item card (50,001 movements)", $"api/items/{perfItem}/card"),
            ("Stock movements", "api/stock-movements?pageSize=200"),
        };
        var slow = new List<string>();
        foreach (var (screen, url) in urls)
        {
            var times = new List<double>(); var size = 0; var status = 0;
            for (var i = 0; i < 3; i++)
            {
                var (st, body, ms) = await Api.SendAsync(h, HttpMethod.Get, url);
                times.Add(ms); size = body.Length; status = (int)st;
            }
            var median = times.OrderBy(t => t).ElementAt(1);
            var line = $"{server}: {screen,-45} {status} median {median,8:F0} ms  payload {size / 1024.0,9:F0} KB";
            out_.WriteLine(line); Evidence.Log("performance", line);
            if (median > 2000 || status != 200) slow.Add(line);
        }
        Assert.Empty(slow);
    }
}
